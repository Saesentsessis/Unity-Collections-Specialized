using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections.Specialized
{
    public sealed unsafe class StableIndexList<T> : IDisposable
    {
        private T[] _data;
        private StableIndexMetadata* _metaPtr;
        private int* _indicesPtr;

        private int _length;
        private int _capacity;
        private int _idCapacity;
        private Allocator _allocator;

        public int Length => _length;
        public int Capacity => _capacity;

        public bool IsCreated => _data != null;
        public bool IsEmpty => !IsCreated || _length == 0;

        public ReadOnlySpan<T> Data => new(_data, 0, _length);

        public StableIndexList(Allocator allocator) : this(8, allocator) { }

        public StableIndexList(int capacity, Allocator allocator)
        {
            _capacity = Math.Max(capacity, 1);
            _data = new T[_capacity];
            _allocator = allocator;

            long metaSize = _capacity * sizeof(StableIndexMetadata);
            long indexSize = _capacity * sizeof(int);

            _metaPtr = (StableIndexMetadata*)UnsafeUtility.MallocTracked(
                metaSize, UnsafeUtility.AlignOf<StableIndexMetadata>(), _allocator, 1);
            _indicesPtr = (int*)UnsafeUtility.MallocTracked(
                indexSize, UnsafeUtility.AlignOf<int>(), _allocator, 1);

            _length = 0;
            _idCapacity = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StableIndexHandle Add(T item)
        {
            int id = GetFreeSlot(out int version);

            _data[_length] = item;
            _indicesPtr[id] = _length;
            _length++;

            return new StableIndexHandle(id, version);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(in StableIndexHandle handle)
        {
            CollectionUtils.CheckHandleValid(IsValid(in handle), handle);

            int id = handle.Index;
            int denseIndex = _indicesPtr[id];

            int lastDenseIndex = _length - 1;
            int lastId = _metaPtr[lastDenseIndex].ReverseId;

            _metaPtr[denseIndex].Version++;

            _data[denseIndex] = _data[lastDenseIndex];
            _data[lastDenseIndex] = default;

            (_metaPtr[denseIndex], _metaPtr[lastDenseIndex]) = (_metaPtr[lastDenseIndex], _metaPtr[denseIndex]);

            _indicesPtr[lastId] = denseIndex;
            _indicesPtr[id] = lastDenseIndex;

            _length--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(in StableIndexHandle handle)
        {
            if (handle.Index < 0 || handle.Index >= _idCapacity)
                return false;

            int denseIndex = _indicesPtr[handle.Index];

            if (denseIndex < 0 || denseIndex >= _length)
                return false;

            return _metaPtr[denseIndex].Version == handle.Version;
        }

        private int GetFreeSlot(out int version)
        {
            if (_length >= _capacity)
                SetCapacity(_capacity * 2);

            if (_idCapacity > _length)
            {
                ref StableIndexMetadata meta = ref _metaPtr[_length];
                meta.Version++;
                version = meta.Version;
                return meta.ReverseId;
            }

            int newId = _idCapacity++;

            _metaPtr[_length] = new StableIndexMetadata
            {
                ReverseId = newId,
                Version = 1,
            };

            version = 1;
            return newId;
        }

        /// <summary>
        /// Sets the capacity. Capacity will not shrink below <see cref="Length"/>.
        /// </summary>
        public void SetCapacity(int capacity)
        {
            capacity = Math.Max(capacity, 1);
            if (capacity < _length)
                capacity = _length;

            if (capacity == _capacity)
                return;

            Reallocate(capacity);
        }

        /// <summary>
        /// Sets the capacity to match the length (minimum capacity remains 1).
        /// </summary>
        public void TrimExcess()
        {
            var trimmed = Math.Max(_length, 1);
            if (trimmed == _capacity)
                return;

            Reallocate(trimmed);
        }

        private void Reallocate(int newCapacity)
        {
            Array.Resize(ref _data, newCapacity);

            long newMetaSize = newCapacity * sizeof(StableIndexMetadata);
            long newIndexSize = newCapacity * sizeof(int);

            StableIndexMetadata* newMetaPtr = (StableIndexMetadata*)UnsafeUtility.MallocTracked(
                newMetaSize, UnsafeUtility.AlignOf<StableIndexMetadata>(), _allocator, 1);
            int* newIndicesPtr = (int*)UnsafeUtility.MallocTracked(
                newIndexSize, UnsafeUtility.AlignOf<int>(), _allocator, 1);

            UnsafeUtility.MemCpy(newMetaPtr, _metaPtr, _idCapacity * sizeof(StableIndexMetadata));
            UnsafeUtility.MemCpy(newIndicesPtr, _indicesPtr, _idCapacity * sizeof(int));

            UnsafeUtility.FreeTracked(_metaPtr, _allocator);
            UnsafeUtility.FreeTracked(_indicesPtr, _allocator);

            _metaPtr = newMetaPtr;
            _indicesPtr = newIndicesPtr;
            _capacity = newCapacity;
            _idCapacity = Math.Min(_idCapacity, newCapacity);
        }

        /// <summary>
        /// Resets length and ID capacity to 0 without zeroing unmanaged buffers.
        /// Clears the managed data array so references can be collected.
        /// </summary>
        /// <remarks>
        /// Does not change the capacity. All existing handles become invalid.
        /// </remarks>
        public void Clear()
        {
            if (_length > 0)
                Array.Clear(_data, 0, _length);

            _length = 0;
            _idCapacity = 0;
        }

        public void Dispose()
        {
            if (_metaPtr != null)
            {
                UnsafeUtility.FreeTracked(_metaPtr, _allocator);
                UnsafeUtility.FreeTracked(_indicesPtr, _allocator);
                _metaPtr = null;
                _indicesPtr = null;
            }

            _data = null;
            _capacity = _idCapacity = _length = 0;
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
            get => _data[_indicesPtr[handle.Index]];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _data[_indicesPtr[handle.Index]] = value;
        }
    }
}
