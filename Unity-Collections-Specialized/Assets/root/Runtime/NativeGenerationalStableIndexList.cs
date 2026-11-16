using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Specialized
{
	/// <summary>
	/// Safety-checked stable-index list with a generation counter and <b>read-only</b> element access.
	/// </summary>
	/// <remarks>
	/// <para>Wraps a <see cref="NativeStableIndexList{T}"/> whose header is an <see cref="UnsafeGenerationalStableIndexList{T}"/>
	/// (its first field is the plain list), so all safety handles, disposal and job disposal are the plain list's.
	/// Like Unity's <c>NativeParallelHashSet</c>, this wrapper is not itself a <c>[NativeContainer]</c>; the job system
	/// finds the safety handle on the nested field.</para>
	/// <para>Elements can only be written through <see cref="Set"/> / <see cref="SetAt"/>, which bump the generation when the
	/// value changes, so <see cref="Generation"/> observes every change. See <see cref="UnsafeGenerationalStableIndexList{T}"/>
	/// for the full bump rules.</para>
	/// </remarks>
	[DebuggerDisplay("Length = {m_List.IsCreated ? m_List.Length : default}")]
	public struct NativeGenerationalStableIndexList<T> : INativeDisposable, IEquatable<NativeGenerationalStableIndexList<T>>
		where T : unmanaged
	{
		internal NativeStableIndexList<T> m_List;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		// ReSharper disable once InconsistentNaming
		// Statically register this type with the safety system, so errors name the generational list.
		internal static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeGenerationalStableIndexList<T>>();
#endif

		public NativeGenerationalStableIndexList(Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) : this(8, allocator, options) { }

		public unsafe NativeGenerationalStableIndexList(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
		{
			CollectionUtils.CheckAllocator(allocator);

			var listData = AllocatorManager.Allocate<UnsafeGenerationalStableIndexList<T>>(allocator);
			*listData = new UnsafeGenerationalStableIndexList<T>(capacity, allocator, options);

			m_List = new NativeStableIndexList<T>((UnsafeStableIndexList<T>*)listData, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.SetStaticSafetyId(ref m_List.m_Safety, s_staticSafetyId);
#endif
		}

		private readonly unsafe UnsafeGenerationalStableIndexList<T>* ListData
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => (UnsafeGenerationalStableIndexList<T>*)m_List.ListData;
		}

		public int Length
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.Length;
		}

		public int Capacity
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.Capacity;
		}

		/// <summary>
		/// Current generation of the container. Wraps around on overflow.
		/// </summary>
		public unsafe uint Generation
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				m_List.CheckRead();
				return ListData->Generation;
			}
		}

		public bool IsEmpty
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.IsEmpty;
		}

		public bool IsCreated
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_List.IsCreated;
		}

		/// <summary>
		/// Read-only view of the dense elements. Structural changes and <see cref="Set"/> / <see cref="SetAt"/> invalidate it.
		/// </summary>
		public NativeArray<T>.ReadOnly AsReadOnly() => m_List.AsArray().AsReadOnly();

		public ReadOnlySpan<T> AsReadOnlySpan() => m_List.AsReadOnlySpan();

		/// <summary>
		/// Adds an element using the Stable Index routing. Bumps the generation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe StableIndexHandle Add(T value)
		{
			m_List.CheckWriteAndBumpSecondaryVersion();
			return ListData->Add(in value);
		}

		/// <summary>
		/// Removes an element by handle, maintaining dense storage contiguity. Bumps the generation.
		/// Throws for invalid handles when collection checks are enabled.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe void Remove(in StableIndexHandle handle)
		{
			m_List.CheckWriteAndBumpSecondaryVersion();
			CollectionUtils.CheckHandleValid(ListData->IsValid(in handle), handle);
			ListData->Remove(in handle);
		}

		/// <summary>
		/// Updates an element through its handle. Bumps the generation only if the value changes (bitwise comparison).
		/// Throws for invalid handles when collection checks are enabled.
		/// </summary>
		public unsafe void Set(in StableIndexHandle handle, T value)
		{
			m_List.CheckWriteAndBumpSecondaryVersion();
			CollectionUtils.CheckHandleValid(ListData->IsValid(in handle), handle);
			ListData->Set(in handle, in value);
		}

		/// <summary>
		/// Updates an element at a dense index. Bumps the generation only if the value changes (bitwise comparison).
		/// </summary>
		public unsafe void SetAt(int index, T value)
		{
			m_List.CheckWriteAndBumpSecondaryVersion();
			CollectionUtils.CheckIndexInRange(index, ListData->Length);
			(*ListData)[index] = value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValid(in StableIndexHandle handle) => m_List.IsValid(in handle);

		/// <summary>
		/// Resets length to 0 without zeroing memory. Bumps the generation; versions are kept, so existing handles stay invalid.
		/// </summary>
		public unsafe void Clear()
		{
			m_List.CheckWriteAndBumpSecondaryVersion();
			ListData->Clear();
		}

		/// <summary>
		/// The element at a dense index (read-only; use <see cref="SetAt"/> to write).
		/// </summary>
		public unsafe T this[int index]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				m_List.CheckRead();
				CollectionUtils.CheckIndexInRange(index, ListData->Length);
				return ListData->m_List.DataPtr[index];
			}
		}

		/// <summary>
		/// The element of a handle (read-only; use <see cref="Set"/> to write). Does not validate the version.
		/// </summary>
		public unsafe T this[in StableIndexHandle handle]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				m_List.CheckRead();
				return ListData->ElementAt(in handle);
			}
		}

		public bool Equals(NativeGenerationalStableIndexList<T> other) => m_List.Equals(other.m_List);

		[BurstDiscard]
		public override bool Equals(object obj)
		{
			return obj is NativeGenerationalStableIndexList<T> other && Equals(other);
		}

		public override int GetHashCode() => m_List.GetHashCode();

		/// <summary>
		/// Releases all resources (memory and safety handles).
		/// </summary>
		public void Dispose() => m_List.Dispose();

		/// <summary>
		/// Creates and schedules a job that releases all resources (memory and safety handles) of this list.
		/// </summary>
		public JobHandle Dispose(JobHandle inputDeps) => m_List.Dispose(inputDeps);

		public static implicit operator ReadOnlySpan<T>(in NativeGenerationalStableIndexList<T> source) => source.AsReadOnlySpan();

		public static implicit operator NativeArray<T>.ReadOnly(in NativeGenerationalStableIndexList<T> source) => source.AsReadOnly();

		public static bool operator ==(NativeGenerationalStableIndexList<T> left, NativeGenerationalStableIndexList<T> right) => left.Equals(right);

		public static bool operator !=(NativeGenerationalStableIndexList<T> left, NativeGenerationalStableIndexList<T> right) => !left.Equals(right);
	}
}
