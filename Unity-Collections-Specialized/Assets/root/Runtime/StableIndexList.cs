/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Unity Collections Specialized                                             │
│  Custom-made third-party package — not affiliated with or endorsed by      │
│  Unity Technologies.                                                       │
│  Repository: https://github.com/Saesentsessis/Unity-Collections-Specialized│
└────────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections.Specialized
{
    public sealed unsafe class StableIndexList<T> : IDisposable
    {
        // Dense storage for data (Managed)
        private T[] _data;
    
        // Dense storage for Metadata (Unmanaged)
        // Maps: Dense Index -> Metadata { RID, Validity }
        private StableIndexMetadata* _metaPtr;
    
        // Sparse storage for Indices (Unmanaged)
        // Maps: ID -> Dense Index
        private int* _indicesPtr;
        
        private int _count;         // Number of active elements
        private int _capacity;      // Physical allocation size
        private int _idCapacity;    // Number of IDs ever allocated (Active + Free)
        private Allocator _allocator;

        public int Count => _count;
        public int Capacity => _capacity;

        public ReadOnlySpan<T> Data => new(_data, 0, _count);

        public StableIndexList(Allocator allocator) : this(8, allocator) { }

        public StableIndexList(int capacity, Allocator allocator)
        {
            _capacity = Math.Max(capacity, 1);
            _data = new T[_capacity];
            _allocator = allocator;

            // Allocate unmanaged memory
            long metaSize = _capacity * sizeof(StableIndexMetadata);
            long indexSize = _capacity * sizeof(int);

            _metaPtr = (StableIndexMetadata*)UnsafeUtility.MallocTracked(metaSize, UnsafeUtility.AlignOf<StableIndexMetadata>(), _allocator, 1);
            _indicesPtr = (int*)UnsafeUtility.MallocTracked(indexSize, UnsafeUtility.AlignOf<int>(), _allocator, 1);

            _count = 0;
            _idCapacity = 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StableIndexHandle Add(T item)
        {
            // Get a slot (reused or new)
            int id = GetFreeSlot(out int version);

            // Write Data
            _data[_count] = item;
        
            // Update Maps
            // Note: GetFreeSlot has already prepared the metadata at _count
            _indicesPtr[id] = _count;
        
            _count++;
        
            return new StableIndexHandle(id, version);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(in StableIndexHandle handle)
        {   
            int id = handle.Index;
            int denseIndex = _indicesPtr[id];
        
            int lastDenseIndex = _count - 1;
            int lastId = _metaPtr[lastDenseIndex].ReverseId;

            // Increment Version (Invalidate current handle)
            // We increment the version at the dense location BEFORE swapping.
            _metaPtr[denseIndex].Version++;

            // Swap Data (Move last element into the hole)
            _data[denseIndex] = _data[lastDenseIndex];
            _data[lastDenseIndex] = default; // Help GC

            // Swap Metadata.
            (_metaPtr[denseIndex], _metaPtr[lastDenseIndex]) = (_metaPtr[lastDenseIndex], _metaPtr[denseIndex]);

            // Update Indices
            _indicesPtr[lastId] = denseIndex; // ID that was at the end is now at 'denseIndex'
            _indicesPtr[id] = lastDenseIndex; // ID we removed is now at 'lastDenseIndex' (The "Free" zone)

            _count--;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(in StableIndexHandle handle)
        {
            // A handle is valid if:
            // ID is within the allocated range
            // Sparse map points to an active index (< _count)
            // Version in metadata matches the handle's version
        
            if (handle.Index < 0 || handle.Index >= _idCapacity)
                return false;

            int denseIndex = _indicesPtr[handle.Index];
        
            // Check if index is in the "Active" region
            if (denseIndex < 0 || denseIndex >= _count)
                return false;

            // Check Generation
            return _metaPtr[denseIndex].Version == handle.Version;
        }
        
        private int GetFreeSlot(out int version)
        {
            if (_count >= _capacity)
                Reallocate(_capacity * 2);
            
            // Check if we have "Ghost" slots (recycled IDs) at the end of the arrays
            if (_idCapacity > _count)
            {
                ref StableIndexMetadata meta = ref _metaPtr[_count];
                
                // The slot at '_count' is currently "free" memory. 
                // It holds the metadata of the last removed item.
                int recycledId = meta.ReverseId;
                
                // Increment version again so the new object gets a fresh generation
                // (Distinguishes "Deleted" state from "Reused" state)
                meta.Version++;
                
                version = meta.Version;
                return recycledId;
            }

            // Create new ID
            int newId = _idCapacity++;

            _metaPtr[_count] = new StableIndexMetadata
            {
                ReverseId = newId,
                Version = 1, // Start at version 1
            };
            
            version = 1;
            return newId;
        }

        private void Reallocate(int newCapacity)
        {
            Array.Resize(ref _data, newCapacity);

            long newMetaSize = newCapacity * sizeof(StableIndexMetadata);
            long newIndexSize = newCapacity * sizeof(int);

            StableIndexMetadata* newMetaPtr = (StableIndexMetadata*)UnsafeUtility.MallocTracked(newMetaSize, UnsafeUtility.AlignOf<StableIndexMetadata>(), Allocator.Persistent, 1);
            int* newIndicesPtr = (int*)UnsafeUtility.MallocTracked(newIndexSize, UnsafeUtility.AlignOf<int>(), Allocator.Persistent, 1);

            // Copy everything (Active + Free slots)
            UnsafeUtility.MemCpy(newMetaPtr, _metaPtr, _idCapacity * sizeof(StableIndexMetadata));
            UnsafeUtility.MemCpy(newIndicesPtr, _indicesPtr, _idCapacity * sizeof(int));

            UnsafeUtility.FreeTracked(_metaPtr, _allocator);
            UnsafeUtility.FreeTracked(_indicesPtr, _allocator);

            _metaPtr = newMetaPtr;
            _indicesPtr = newIndicesPtr;
            _capacity = newCapacity;
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
            _capacity = _idCapacity = _count = 0;
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