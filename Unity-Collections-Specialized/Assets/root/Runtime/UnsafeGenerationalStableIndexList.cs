using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Specialized
{
	/// <summary>
	/// <see cref="UnsafeStableIndexList{T}"/> plus a generation counter that changes whenever the contents change.
	/// All storage and handle logic is the wrapped list's; this struct only forwards and bumps.
	/// </summary>
	/// <remarks>
	/// <para>The generation bumps (unchecked <see cref="uint"/> wrap) on <see cref="Add"/>, a successful <see cref="Remove"/> /
	/// <see cref="TryRemove"/>, <see cref="Clear"/>, <see cref="Resize"/> / <see cref="Length"/> set, and on value writes through
	/// <see cref="Set"/> or the dense setter when the new value differs (bitwise comparison).</para>
	/// <para>Capacity changes (growth, <see cref="SetCapacity"/>, <see cref="TrimExcess"/>) do not bump.
	/// Writes through <c>ref</c> accessors (<see cref="ElementAt(int)"/>, the handle indexer) are not tracked;
	/// call <see cref="BumpGeneration"/> after such writes if observers must notice them.</para>
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct UnsafeGenerationalStableIndexList<T>
		: INativeDisposable
		, INativeList<T>
		, IEquatable<UnsafeGenerationalStableIndexList<T>>
		where T : unmanaged
	{
		/// <summary>
		/// The wrapped list. Must stay the first field: <see cref="NativeGenerationalStableIndexList{T}"/> treats a pointer
		/// to this struct as an <see cref="UnsafeStableIndexList{T}"/> pointer (shared dispose path).
		/// </summary>
		internal UnsafeStableIndexList<T> m_List;

		private uint m_generation;

		public static void Destroy(UnsafeGenerationalStableIndexList<T>* listData)
		{
			UnsafeStableIndexList<T>.Destroy((UnsafeStableIndexList<T>*)listData);
		}

		/// <summary>
		/// Initializes and returns an instance of the list.
		/// </summary>
		/// <param name="initialCapacity">The initial capacity of the list.</param>
		/// <param name="allocator">The allocator to use.</param>
		/// <param name="options">Whether newly allocated data bytes should be zeroed out.</param>
		public UnsafeGenerationalStableIndexList(int initialCapacity, AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
		{
			m_List = new UnsafeStableIndexList<T>(initialCapacity, allocator, options);
			m_generation = 0U;
		}

		/// <summary>
		/// Current generation of the container. Wraps around on overflow.
		/// </summary>
		public readonly uint Generation
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_generation;
		}

		/// <summary>
		/// Dense storage for data. Writes through this pointer are not tracked.
		/// </summary>
		public readonly T* DataPtr
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.DataPtr;
		}

		/// <inheritdoc cref="UnsafeStableIndexList{T}.MetaPtr"/>
		public StableIndexMetadata* MetaPtr => m_List.MetaPtr;

		/// <inheritdoc cref="UnsafeStableIndexList{T}.IndicesPtr"/>
		public int* IndicesPtr => m_List.IndicesPtr;

		public readonly AllocatorManager.AllocatorHandle Allocator
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.Allocator;
		}

		/// <summary>
		/// The number of elements. Setting it behaves like <see cref="Resize"/> (bumps the generation).
		/// </summary>
		public int Length
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			readonly get => m_List.Length;
			set => Resize(value);
		}

		/// <summary>
		/// The number of elements that can fit in the internal buffers. Setting is grow-only and does not bump the generation.
		/// </summary>
		public int Capacity
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			readonly get => m_List.Capacity;
			set => m_List.SetCapacity(value);
		}

		public readonly bool IsEmpty
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.IsEmpty;
		}

		public readonly bool IsCreated
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.IsCreated;
		}

		/// <summary>
		/// The element at a dense index. Setting bumps the generation only if the value changes.
		/// </summary>
		public T this[int index]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List[index];
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set => SetIfChanged(ref m_List.ElementAt(index), value);
		}

		/// <summary>
		/// Stable handle accessor (untracked <c>ref</c>). Does not validate the version (use <see cref="IsValid"/>).
		/// </summary>
		public ref T this[in StableIndexHandle handle]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => ref m_List.ElementAt(in handle);
		}

		/// <summary>
		/// Untracked <c>ref</c> to the element at a dense index.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T ElementAt(int index) => ref m_List.ElementAt(index);

		/// <summary>
		/// Untracked <c>ref</c> to the element of a handle.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T ElementAt(in StableIndexHandle handle) => ref m_List.ElementAt(in handle);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValid(in StableIndexHandle handle) => m_List.IsValid(in handle);

		/// <summary>
		/// Adds an element and returns its stable handle. Bumps the generation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public StableIndexHandle Add(in T value)
		{
			var handle = m_List.Add(in value);
			BumpGeneration();
			return handle;
		}

		/// <summary>
		/// Removes an element by handle using swap-back. Invalid / stale handles are ignored (no bump).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(in StableIndexHandle handle) => TryRemove(in handle);

		/// <summary>
		/// Removes an element by handle using swap-back. Bumps the generation if something was removed.
		/// </summary>
		/// <returns>False if the handle was invalid or stale (nothing removed).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryRemove(in StableIndexHandle handle)
		{
			if (!m_List.TryRemove(in handle))
				return false;

			BumpGeneration();
			return true;
		}

		/// <summary>
		/// Writes a value through a handle. Bumps the generation only if the value changes.
		/// Does not validate the version (use <see cref="IsValid"/>).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(in StableIndexHandle handle, in T value) => SetIfChanged(ref m_List.ElementAt(in handle), value);

		/// <summary>
		/// Resets length to 0 without zeroing memory. Bumps the generation; versions are kept, so existing handles stay invalid.
		/// </summary>
		public void Clear()
		{
			m_List.Clear();
			BumpGeneration();
		}

		/// <summary>
		/// Sets the length, expanding the capacity if necessary. Bumps the generation.
		/// </summary>
		public void Resize(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
		{
			m_List.Resize(length, options);
			BumpGeneration();
		}

		/// <summary>
		/// Grows the capacity. Does not bump the generation.
		/// </summary>
		public void SetCapacity(int capacity) => m_List.SetCapacity(capacity);

		/// <summary>
		/// Shrinks capacity without invalidating live handles. Does not bump the generation.
		/// </summary>
		public void TrimExcess() => m_List.TrimExcess();

		/// <summary>
		/// Advances the generation (unchecked wrap). Use after untracked writes through <c>ref</c> accessors or <see cref="DataPtr"/>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void BumpGeneration()
		{
			unchecked
			{
				m_generation++;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetIfChanged(ref T slot, T value)
		{
			if (CollectionUtils.BitwiseEquals(ref slot, ref value))
				return;

			slot = value;
			BumpGeneration();
		}

		public bool Equals(UnsafeGenerationalStableIndexList<T> other)
		{
			return m_List.Equals(other.m_List) && m_generation == other.m_generation;
		}

		[BurstDiscard]
		public override bool Equals(object obj)
		{
			return obj is UnsafeGenerationalStableIndexList<T> other && Equals(other);
		}

		public override int GetHashCode() => m_List.GetHashCode() * 397 ^ (int)m_generation;

		public static bool operator ==(UnsafeGenerationalStableIndexList<T> left, UnsafeGenerationalStableIndexList<T> right) => left.Equals(right);

		public static bool operator !=(UnsafeGenerationalStableIndexList<T> left, UnsafeGenerationalStableIndexList<T> right) => !left.Equals(right);

		/// <summary>
		/// Releases all resources (memory) and resets the generation.
		/// </summary>
		public void Dispose()
		{
			m_List.Dispose();
			m_generation = 0U;
		}

		/// <summary>
		/// Creates and schedules a job that frees the memory of this list, and resets the generation.
		/// </summary>
		public JobHandle Dispose(JobHandle inputDeps)
		{
			m_generation = 0U;
			return m_List.Dispose(inputDeps);
		}
	}
}
