using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Collections.Specialized
{
    [BurstCompile(DisableSafetyChecks = true)]
    public unsafe struct UnsafeStableIndexListDisposeJob : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public void* DataPtr;
        [NativeDisableUnsafePtrRestriction]
        public void* MetaIndicesPackedPtr;
        
        public AllocatorManager.AllocatorHandle Allocator;
        
        public void Execute()
        {
            AllocatorManager.Free(Allocator, DataPtr);
            AllocatorManager.Free(Allocator, MetaIndicesPackedPtr);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UnsafeStableIndexList<T>
        : INativeDisposable
        , INativeList<T>
        , IEquatable<UnsafeStableIndexList<T>>
        where T : unmanaged
    {
        /// <summary>
        /// The maximum number of elements this type of container can hold.
        /// </summary>
        public const int MaxCapacity = int.MaxValue;
        
        public static readonly int PackedElementSize = sizeof(StableIndexMetadata) + sizeof(int);

        public static void Destroy(UnsafeStableIndexList<T>* listData)
        {
            CheckNull(listData);
            var allocator = listData->Allocator;
            listData->Dispose();
            AllocatorManager.Free(allocator, listData);
        }
        
        /// <summary>
        /// Dense storage for data.
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        public T* DataPtr;

        /// <summary>
        /// Container 
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        private void* _metaAndIndicesPtr;

        /// <summary>
        /// Dense storage for Metadata.
        /// Maps: Dense Index -> Metadata { RID, Validity }.
        /// </summary>
        public StableIndexMetadata* MetaPtr => (StableIndexMetadata*)_metaAndIndicesPtr;

        /// <summary>
        /// Sparse storage for Indices.
        /// Maps: ID -> Dense Index.
        /// Offset is capacity * (sizeof(StableIndexMetadata) / sizeof(int)) because
        /// meta and indices share one SoA allocation: [Meta × capacity][Indices × capacity].
        /// </summary>
        public int* IndicesPtr => (int*)_metaAndIndicesPtr + m_capacity * (sizeof(StableIndexMetadata) / sizeof(int));

        /// <summary>
        /// The number of elements.
        /// </summary>
        public int m_length;

        /// <summary>
        /// The number of elements that can fit in the internal buffer.
        /// </summary>
        public int m_capacity;
        
        /// <summary>
        /// The number of IDs ever allocated (Active + Free)
        /// </summary>
        public int m_idCapacity;

        /// <summary>
        /// The allocator used to create the internal buffer.
        /// </summary>
        public AllocatorManager.AllocatorHandle Allocator;

        /// <summary>
        /// Initializes and returns an instance of UnsafeList.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the list.</param>
        /// <param name="allocator">The allocator to use.</param>
        /// <param name="options">Whether newly allocated bytes should be zeroed out.</param>
        public UnsafeStableIndexList(int initialCapacity, AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            DataPtr = null;
            _metaAndIndicesPtr = null;
            m_length = 0;
            m_capacity = 0;
            m_idCapacity = 0;
            Allocator = allocator;
            
            SetCapacity(math.max(initialCapacity, 1));

            if ((options & NativeArrayOptions.ClearMemory) != NativeArrayOptions.ClearMemory)
                return;
            
            if (DataPtr != null)
                UnsafeUtility.MemClear(DataPtr, (long)m_capacity * sizeof(T));
            if (_metaAndIndicesPtr != null)
                UnsafeUtility.MemClear(_metaAndIndicesPtr, (long)m_capacity * PackedElementSize);
        }
        
        /// <summary>
        /// The number of elements.
        /// </summary>
        /// <value>The number of elements.</value>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => CollectionUtils.AssumePositive(m_length);

            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Length must be non-negative.");

                // Shrink: truncate dense length. Handles past the new length fail IsValid via bounds.
                if (value <= m_length)
                {
                    m_length = value;
                    return;
                }

                // Grow: ensure capacity and mint stable IDs for every new dense slot.
                Resize(value, NativeArrayOptions.UninitializedMemory);
            }
        }
        
        /// <summary>
        /// The number of elements that can fit in the internal buffer.
        /// </summary>
        /// <value>The number of elements that can fit in the internal buffer.</value>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => CollectionUtils.AssumePositive(m_capacity);
            set => SetCapacity(value);
        }
        
        /// <summary>
        /// Whether the list is empty.
        /// </summary>
        /// <value>True if the list is empty or the list has not been constructed.</value>
        public readonly bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsCreated || m_length == 0;
        }

        /// <summary>
        /// Whether this list has been allocated (and not yet deallocated).
        /// </summary>
        /// <value>True if this list has been allocated (and not yet deallocated).</value>
        public readonly bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => DataPtr != null;
        }
        
        /// <summary>
        /// The element at an index.
        /// </summary>
        /// <param name="index">An index.</param>
        /// <value>The element at the index.</value>
        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                CollectionUtils.CheckIndexInRange(index, m_length);
                return DataPtr[CollectionUtils.AssumePositive(index)];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                CollectionUtils.CheckIndexInRange(index, m_length);
                DataPtr[CollectionUtils.AssumePositive(index)] = value;
            }
        }
        
        /// <summary>
        /// Stable Handle Accessor. Maps Handle to Dense Index.
        /// </summary>
        /// <param name="handle">A handle.</param>
        /// <value>The element of that handle.</value>
        public ref T this[in StableIndexHandle handle]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                CollectionUtils.CheckIndexInRange(handle.Index, m_idCapacity);
                return ref DataPtr[CollectionUtils.AssumePositive(IndicesPtr[CollectionUtils.AssumePositive(handle.Index)])];
            } 
        }
        
        /// <summary>
        /// Returns a reference to the element at a given index.
        /// </summary>
        /// <param name="index">The index to access. Must be in the range of [0..Length).</param>
        /// <returns>A reference to the element at the index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T ElementAt(int index)
        {
            CollectionUtils.CheckIndexInRange(index, m_length);
            return ref DataPtr[CollectionUtils.AssumePositive(index)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T ElementAt(in StableIndexHandle handle)
        {
            CollectionUtils.CheckIndexInRange(handle.Index, m_idCapacity);
            return ref DataPtr[CollectionUtils.AssumePositive(IndicesPtr[CollectionUtils.AssumePositive(handle.Index)])];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(in StableIndexHandle handle)
        {
            if ((uint)handle.Index >= (uint)m_idCapacity)
                return false;

            var denseIndex = IndicesPtr[handle.Index];
        
            if ((uint)denseIndex >= (uint)m_length)
                return false;

            return MetaPtr[denseIndex].Version == handle.Version;
        }
        
        /// <summary>
        /// Adds an element using the Stable Index routing.
        /// </summary>
        /// <param name="value">The value to add to the list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StableIndexHandle Add(in T value)
        {
            int id = GetFreeSlot(out int version);

            DataPtr[m_length] = value;
            IndicesPtr[id] = m_length;
    
            m_length++;
    
            return new StableIndexHandle(id, version);
        }
        
        /// <summary>
        /// Removes an element using its Handle, maintaining dense storage contiguity.
        /// </summary>
        /// <param name="handle">The handle pointing to the element, that should be removed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(in StableIndexHandle handle)
        {   
            int id = handle.Index;
            int denseIndex = IndicesPtr[id];
    
            int lastDenseIndex = m_length - 1;
            int lastId = MetaPtr[lastDenseIndex].ReverseId;

            ref var meta = ref MetaPtr[denseIndex];
            meta.Version++;

            DataPtr[denseIndex] = DataPtr[lastDenseIndex];

            (MetaPtr[denseIndex], MetaPtr[lastDenseIndex]) = (MetaPtr[lastDenseIndex], MetaPtr[denseIndex]);

            IndicesPtr[lastId] = denseIndex;
            IndicesPtr[id] = lastDenseIndex; 

            m_length--;
        }

        /// <summary>
        /// Resets length and ID capacity to 0 without zeroing allocated memory.
        /// </summary>
        /// <remarks>
        /// Does not change the capacity. All existing handles become invalid.
        /// </remarks>
        public void Clear()
        {
            m_length = 0;
            m_idCapacity = 0;
        }
        
        /// <summary>
        /// Sets the capacity to match the length.
        /// </summary>
        public void TrimExcess()
        {
            if (Capacity == m_length)
                return;
            
            ResizeExact(m_length);
        }
        
        /// <summary>
        /// Sets the length, expanding the capacity if necessary.
        /// Growing mints stable-index metadata and sparse IDs for each new dense slot
        /// so handles remain consistent with <see cref="Add"/>.
        /// </summary>
        /// <param name="length">The new length.</param>
        /// <param name="options">Whether newly allocated data bytes should be zeroed out.</param>
        /// <remarks>
        /// ClearMemory zeroes <see cref="DataPtr"/> only for the grown range; meta/indices
        /// are initialized by ID minting (clearing meta would destroy recycled reverse IDs).
        /// Shrinking truncates length without walking versions.
        /// </remarks>
        public void Resize(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

            int oldLength = m_length;

            if (length > Capacity)
                SetCapacity(length);

            if (length > oldLength)
            {
                if ((options & NativeArrayOptions.ClearMemory) == NativeArrayOptions.ClearMemory)
                {
                    long num = length - oldLength;
                    UnsafeUtility.MemClear(DataPtr + oldLength, num * sizeof(T));
                }

                MintIdsForRange(oldLength, length);
            }

            m_length = length;
        }

        /// <summary>
        /// Prepares meta + sparse index entries for dense slots [fromInclusive, toExclusive).
        /// Mirrors <see cref="GetFreeSlot"/> recycle vs mint rules without writing element data.
        /// </summary>
        private void MintIdsForRange(int fromInclusive, int toExclusive)
        {
            for (int denseIndex = fromInclusive; denseIndex < toExclusive; denseIndex++)
            {
                if (m_idCapacity > denseIndex)
                {
                    ref StableIndexMetadata meta = ref MetaPtr[denseIndex];
                    meta.Version++;
                    IndicesPtr[meta.ReverseId] = denseIndex;
                }
                else
                {
                    int newId = m_idCapacity++;
                    MetaPtr[denseIndex] = new StableIndexMetadata
                    {
                        ReverseId = newId,
                        Version = 1,
                    };
                    IndicesPtr[newId] = denseIndex;
                }
            }
        }
        
        /// <summary>
        /// Sets the capacity.
        /// </summary>
        /// <param name="capacity">The new capacity.</param>
        public void SetCapacity(int capacity)
        {
            SetCapacity(ref Allocator, capacity);
        }
        
        private void SetCapacity<U>(ref U allocator, int capacity) where U : unmanaged, AllocatorManager.IAllocator
        {
            CollectionUtils.CheckCapacityInRange(capacity, MaxCapacity);

            var sizeOf = sizeof(T);
            var newCapacity = math.max(capacity, CollectionHelper.CacheLineSize / sizeOf);
            long ceilPow2 = math.ceilpow2((long)newCapacity);
            newCapacity = (int)math.min(MaxCapacity, ceilPow2);

            if (newCapacity == Capacity)
                return;

            ResizeExact(ref allocator, newCapacity);
        }
        
        private void ResizeExact(int capacity)
        {
            ResizeExact(ref Allocator, capacity);
        }
        
        private void ResizeExact<U>(ref U allocator, int newCapacity) where U : unmanaged, AllocatorManager.IAllocator
        {
            newCapacity = math.max(0, newCapacity);

            CollectionUtils.CheckAllocator(Allocator);
            T* newDataPtr = null;
            void* newMetaAndIndicesPtr = null;

            if (newCapacity > 0)
            {
                newDataPtr = (T*)AllocatorManager.Allocate(
                    allocator.Handle,
                    sizeof(T),
                    UnsafeUtility.AlignOf<T>(),
                    newCapacity
                );
                newMetaAndIndicesPtr = AllocatorManager.Allocate(
                    allocator.Handle,
                    PackedElementSize,
                    UnsafeUtility.AlignOf<StableIndexMetadata>(),
                    newCapacity
                );

                if (m_capacity > 0)
                {
                    var oldCapacity = m_capacity;
                    var itemsToCopy = math.min(newCapacity, oldCapacity);
                    
                    if (DataPtr != null)
                    {
                        var bytesToCopy = itemsToCopy * (long)sizeof(T);
                        UnsafeUtility.MemCpy(newDataPtr, DataPtr, bytesToCopy);
                    }

                    // SoA layout: [Meta × capacity][Indices × capacity]. Copy each region
                    // separately — the indices offset depends on capacity and moves on grow/shrink.
                    if (_metaAndIndicesPtr != null)
                    {
                        var metaIntsPerElement = sizeof(StableIndexMetadata) / sizeof(int);
                        var metaBytes = (long)itemsToCopy * sizeof(StableIndexMetadata);
                        var indexBytes = (long)itemsToCopy * sizeof(int);

                        UnsafeUtility.MemCpy(newMetaAndIndicesPtr, _metaAndIndicesPtr, metaBytes);

                        var oldIndices = (int*)_metaAndIndicesPtr + oldCapacity * metaIntsPerElement;
                        var newIndices = (int*)newMetaAndIndicesPtr + newCapacity * metaIntsPerElement;
                        UnsafeUtility.MemCpy(newIndices, oldIndices, indexBytes);
                    }
                }
            }

            if (CollectionUtils.ShouldDeallocate(allocator.Handle))
            {
                AllocatorManager.Free(allocator.Handle, DataPtr);
                AllocatorManager.Free(allocator.Handle, _metaAndIndicesPtr);
            }

            DataPtr = newDataPtr;
            _metaAndIndicesPtr = newMetaAndIndicesPtr;
            m_capacity = newCapacity;
            m_idCapacity = math.min(m_idCapacity, newCapacity);
            m_length = math.min(m_length, newCapacity);
        }
        
        private int GetFreeSlot(out int version)
        {
            if (m_length >= m_capacity)
                SetCapacity(m_capacity == 0 ? 8 : m_capacity * 2);
        
            if (m_idCapacity > m_length)
            {
                ref StableIndexMetadata meta = ref MetaPtr[m_length];
                
                meta.Version++;
                version = meta.Version;
                
                return meta.ReverseId;
            }

            int newId = m_idCapacity++;
            MetaPtr[m_length] = new StableIndexMetadata
            {
                ReverseId = newId,
                Version = 1, 
            };
        
            version = 1;
            return newId;
        }

        public bool Equals(UnsafeStableIndexList<T> other)
        {
            return DataPtr == other.DataPtr && m_length == other.m_length;
        }

        [BurstDiscard]
        public override bool Equals(object obj)
        {
            return obj is UnsafeStableIndexList<T> other && Equals(other);
        }

        public override int GetHashCode() => (int)DataPtr * 397 ^ m_capacity;
        
        public static bool operator ==(UnsafeStableIndexList<T> left, UnsafeStableIndexList<T> right) => left.Equals(right);

        public static bool operator !=(UnsafeStableIndexList<T> left, UnsafeStableIndexList<T> right) => !left.Equals(right);

        /// <summary>
        /// Releases all resources (memory).
        /// </summary>
        public void Dispose()
        {
            if (IsCreated == false)
                return;
            
            if (CollectionUtils.ShouldDeallocate(Allocator))
            {
                AllocatorManager.Free(Allocator, DataPtr);
                AllocatorManager.Free(Allocator, _metaAndIndicesPtr);
                Allocator = AllocatorManager.Invalid;
            }

            DataPtr = null;
            _metaAndIndicesPtr = null;
            m_length = 0;
            m_capacity = 0;
            m_idCapacity = 0;
        }
        
        /// <summary>
        /// Creates and schedules a job that frees the memory of this list.
        /// </summary>
        /// <param name="inputDeps">The dependency for the new job.</param>
        /// <returns>The handle of the new job. The job depends upon `inputDeps` and frees the memory of this list.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (IsCreated == false)
                return inputDeps;

            if (CollectionUtils.ShouldDeallocate(Allocator))
            {
                var jobHandle = new UnsafeStableIndexListDisposeJob
                {
                    DataPtr = DataPtr,
                    MetaIndicesPackedPtr = _metaAndIndicesPtr,
                    Allocator = Allocator
                }.Schedule(inputDeps);

                DataPtr = null;
                _metaAndIndicesPtr = null;
                Allocator = AllocatorManager.Invalid;

                return jobHandle;
            }

            DataPtr = null;
            _metaAndIndicesPtr = null;

            return inputDeps;
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckNull(void* listData)
        {
            if (listData == null)
                throw new InvalidOperationException("UnsafeList has yet to be created or has been destroyed!");
        }
    }
}