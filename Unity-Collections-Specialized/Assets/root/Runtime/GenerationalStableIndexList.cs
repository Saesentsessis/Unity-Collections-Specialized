using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Unity.Collections.Specialized
{
	/// <summary>
	/// <see cref="StableIndexList{T}"/> plus a generation counter that changes whenever the contents change.
	/// All storage and handle logic is the wrapped list's; this class only forwards and bumps.
	/// </summary>
	/// <remarks>
	/// The generation bumps (unchecked <see cref="uint"/> wrap) on <see cref="Add"/>, a successful <see cref="Remove"/> /
	/// <see cref="TryRemove"/>, <see cref="Clear"/>, and on indexer writes when the new value differs
	/// (<see cref="EqualityComparer{T}.Default"/>). Capacity changes (growth, <see cref="SetCapacity"/>, <see cref="TrimExcess"/>) do not bump.
	/// </remarks>
	public sealed class GenerationalStableIndexList<T> : IDisposable
	{
		private readonly StableIndexList<T> _list;
		private uint _generation;

		public int Length => _list.Length;

		public int Capacity => _list.Capacity;

		/// <summary>
		/// Current generation of the container. Wraps around on overflow.
		/// </summary>
		public uint Generation => _generation;

		public bool IsCreated => _list.IsCreated;
		public bool IsEmpty => _list.IsEmpty;

		public ReadOnlySpan<T> Data => _list.Data;

		public GenerationalStableIndexList(Allocator allocator) : this(8, allocator) { }

		public GenerationalStableIndexList(int capacity, Allocator allocator)
		{
			_list = new StableIndexList<T>(capacity, allocator);
		}

		/// <summary>
		/// Adds an element and returns its stable handle. Bumps the generation.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public StableIndexHandle Add(T item)
		{
			var handle = _list.Add(item);
			BumpGeneration();
			return handle;
		}

		/// <summary>
		/// Removes an element by handle using swap-back. Bumps the generation.
		/// Throws for invalid handles when collection checks are enabled.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Remove(in StableIndexHandle handle)
		{
			CollectionUtils.CheckHandleValid(IsValid(in handle), handle);
			TryRemove(in handle);
		}

		/// <summary>
		/// Removes an element by handle using swap-back. Bumps the generation if something was removed. Never throws.
		/// </summary>
		/// <returns>False if the handle was invalid or stale (nothing removed).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryRemove(in StableIndexHandle handle)
		{
			if (!_list.TryRemove(in handle))
				return false;

			BumpGeneration();
			return true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValid(in StableIndexHandle handle) => _list.IsValid(in handle);

		/// <summary>
		/// Grows the capacity. Does not bump the generation.
		/// </summary>
		public void SetCapacity(int capacity) => _list.SetCapacity(capacity);

		/// <summary>
		/// Shrinks capacity without invalidating live handles. Does not bump the generation.
		/// </summary>
		public void TrimExcess() => _list.TrimExcess();

		/// <summary>
		/// Resets length to 0 and clears the managed data array. Bumps the generation; versions are kept, so existing handles stay invalid.
		/// </summary>
		public void Clear()
		{
			_list.Clear();
			BumpGeneration();
		}

		/// <summary>
		/// Releases all resources and resets the generation.
		/// </summary>
		public void Dispose()
		{
			_list.Dispose();
			_generation = 0U;
		}

		/// <summary>
		/// The element at a dense index. Setting bumps the generation only if the value changes.
		/// </summary>
		public T this[int index]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _list[index];
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set
			{
				if (EqualityComparer<T>.Default.Equals(_list[index], value))
					return;

				_list[index] = value;
				BumpGeneration();
			}
		}

		/// <summary>
		/// The element of a handle. Setting bumps the generation only if the value changes. Does not validate the version.
		/// </summary>
		public T this[in StableIndexHandle handle]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _list[handle];
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set
			{
				if (EqualityComparer<T>.Default.Equals(_list[handle], value))
					return;

				_list[handle] = value;
				BumpGeneration();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void BumpGeneration()
		{
			unchecked
			{
				_generation++;
			}
		}
	}
}
