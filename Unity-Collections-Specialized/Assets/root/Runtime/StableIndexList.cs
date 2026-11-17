using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Unity.Collections.Specialized
{
    /// <summary>
    /// Managed stable-index list. Elements live in a managed <c>T[]</c> (any <typeparamref name="T"/>, including reference types);
    /// handle / ID bookkeeping is delegated to an unmanaged <see cref="UnsafeSlotAllocator"/>, so <see cref="Dispose"/> is required.
    /// </summary>
    /// <remarks>
    /// Invariant: <c>_data.Length</c> always equals the slot allocator capacity, so the allocator never grows on its own.
    /// </remarks>
    public sealed unsafe class StableIndexList<T> : IDisposable
    {
        private T[] _data;
        private UnsafeSlotAllocator _slots;

        public int Length => _slots.m_length;
        public int Capacity => _slots.m_capacity;

        public bool IsCreated => _data != null;
        public bool IsEmpty => !IsCreated || _slots.m_length == 0;

        public ReadOnlySpan<T> Data => new(_data, 0, _slots.m_length);

        public StableIndexList(Allocator allocator) : this(8, allocator) { }

        public StableIndexList(int capacity, Allocator allocator)
        {
            CollectionUtils.CheckAllocator(allocator);

            _slots = new UnsafeSlotAllocator(capacity, allocator);
            _data = new T[_slots.m_capacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StableIndexHandle Add(T item)
        {
            if (_slots.m_length == _slots.m_capacity)
                Grow();

            var id = _slots.Allocate(out var version);
            _data[_slots.m_length - 1] = item;

            return new StableIndexHandle(id, version);
        }

        /// <summary>
        /// Removes an element by handle using swap-back. Throws for invalid handles when collection checks are enabled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(in StableIndexHandle handle)
        {
            CollectionUtils.CheckHandleValid(IsValid(in handle), handle);
            TryRemove(in handle);
        }

        /// <summary>
        /// Removes an element by handle using swap-back. Never throws.
        /// </summary>
        /// <returns>False if the handle was invalid or stale (nothing removed).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove(in StableIndexHandle handle)
        {
            if (!_slots.Release(handle.Index, handle.Version, out var removedDense, out var movedFromDense))
                return false;

            if (removedDense != movedFromDense)
                _data[removedDense] = _data[movedFromDense];

            // Drop the vacated reference so it can be collected.
            _data[movedFromDense] = default;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(in StableIndexHandle handle) => _slots.IsValid(handle.Index, handle.Version);

        /// <summary>
        /// Grows the capacity (rounded by the slot allocator). Never shrinks; use <see cref="TrimExcess"/>.
        /// </summary>
        public void SetCapacity(int capacity)
        {
            var oldCapacity = _slots.m_capacity;

            _slots.SetCapacity(capacity);

            if (_slots.m_capacity != oldCapacity)
                Array.Resize(ref _data, _slots.m_capacity);
        }

        /// <summary>
        /// Shrinks capacity to the smallest ID space that still covers every live ID (minimum one allocator block).
        /// </summary>
        public void TrimExcess()
        {
            var oldCapacity = _slots.m_capacity;

            _slots.TrimExcess();

            // An empty allocator trims to zero capacity (and a null buffer); keep the list created.
            if (_slots.m_capacity == 0)
                _slots.SetCapacity(1);

            if (_slots.m_capacity != oldCapacity)
                Array.Resize(ref _data, _slots.m_capacity);
        }

        /// <summary>
        /// Resets length to 0 without zeroing unmanaged buffers. Clears the managed data array so references can be collected.
        /// </summary>
        /// <remarks>
        /// Does not change the capacity. Versions are kept, so all existing handles stay invalid.
        /// </remarks>
        public void Clear()
        {
            if (_slots.m_length > 0)
                Array.Clear(_data, 0, _slots.m_length);

            _slots.Clear();
        }

        public void Dispose()
        {
            _slots.Dispose();
            _data = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            if (_slots.m_capacity >= UnsafeSlotAllocator.MaxCapacity)
                throw new InvalidOperationException("StableIndexList is at maximum capacity.");

            SetCapacity(_slots.m_capacity == 0 ? 8 : (int)math.min((long)_slots.m_capacity * 2, UnsafeSlotAllocator.MaxCapacity));
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data[index];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _data[index] = value;
        }

        public T this[in StableIndexHandle handle]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data[_slots.IndicesPtr[handle.Index]];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _data[_slots.IndicesPtr[handle.Index]] = value;
        }
    }
}
