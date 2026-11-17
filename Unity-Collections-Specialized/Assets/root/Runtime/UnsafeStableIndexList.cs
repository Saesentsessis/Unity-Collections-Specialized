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

    /// <summary>
    /// Stable-index list whose handle / ID bookkeeping is delegated to an <see cref="UnsafeSlotAllocator"/>.
    /// This struct only owns the dense element buffer and mirrors the allocator's swap-back moves into it.
    /// </summary>
    /// <remarks>
    /// Invariant: the data buffer capacity always equals <c>m_slots.Capacity</c>, so the allocator never grows on its own
    /// (the list grows it first, then reallocates <see cref="DataPtr"/> to match).
    /// Behaviour notes:
    /// <list type="bullet">
    /// <item><see cref="Remove"/> validates the handle (via <see cref="UnsafeSlotAllocator.Release"/>); an invalid handle is a no-op.</item>
    /// <item><see cref="Clear"/> keeps slot versions, so handles taken before <c>Clear</c> stay invalid afterwards.</item>
    /// <item><see cref="SetCapacity"/> is grow-only; <see cref="TrimExcess"/> shrinks without orphaning live IDs.</item>
    /// <item><see cref="Resize"/> re-uses pre-minted IDs; handed-out IDs are not necessarily equal to dense indices.</item>
    /// </list>
    /// </remarks>
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
        public const int MaxCapacity = UnsafeSlotAllocator.MaxCapacity;

        public static void Destroy(UnsafeStableIndexList<T>* listData)
        {
            CheckNull(listData);
            var allocator = listData->Allocator;
            listData->Dispose();
            AllocatorManager.Free(allocator, listData);
        }

        /// <summary>
        /// Dense storage for data. Capacity is always <see cref="Capacity"/>.
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        public T* DataPtr;

        /// <summary>
        /// Stable ID / version bookkeeping (dense metadata + sparse indices).
        /// </summary>
        internal UnsafeSlotAllocator m_slots;

        public UnsafeStableIndexList(int initialCapacity, AllocatorManager.AllocatorHandle allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            DataPtr = null;
            m_slots = new UnsafeSlotAllocator(initialCapacity, allocator);

            ReallocateData(m_slots.m_capacity);

            if ((options & NativeArrayOptions.ClearMemory) == NativeArrayOptions.ClearMemory && DataPtr != null)
                UnsafeUtility.MemClear(DataPtr, (long)m_slots.m_capacity * sizeof(T));
        }

        /// <summary>
        /// Dense storage for Metadata. Maps: Dense Index -> Metadata { RID, Version }.
        /// </summary>
        public StableIndexMetadata* MetaPtr => m_slots.MetaPtr;

        /// <summary>
        /// Sparse storage for Indices. Maps: ID -> Dense Index.
        /// </summary>
        public int* IndicesPtr => m_slots.IndicesPtr;

        public readonly AllocatorManager.AllocatorHandle Allocator
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_slots.m_allocator;
        }

        /// <summary>
        /// The number of elements. Growing mints handle-consistent slots; shrinking truncates.
        /// </summary>
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => CollectionUtils.AssumePositive(m_slots.m_length);
            set => Resize(value);
        }

        /// <summary>
        /// The number of elements that can fit in the internal buffers. Setting is grow-only.
        /// </summary>
        public int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => CollectionUtils.AssumePositive(m_slots.m_capacity);
            set => SetCapacity(value);
        }

        public readonly bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsCreated || m_slots.m_length == 0;
        }

        public readonly bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => DataPtr != null;
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                CollectionUtils.CheckIndexInRange(index, m_slots.m_length);
                return DataPtr[CollectionUtils.AssumePositive(index)];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                CollectionUtils.CheckIndexInRange(index, m_slots.m_length);
                DataPtr[CollectionUtils.AssumePositive(index)] = value;
            }
        }

        /// <summary>
        /// Stable handle accessor. Does not validate the version (use <see cref="IsValid"/>).
        /// </summary>
        public ref T this[in StableIndexHandle handle]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref ElementAt(in handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T ElementAt(int index)
        {
            CollectionUtils.CheckIndexInRange(index, m_slots.m_length);
            return ref DataPtr[CollectionUtils.AssumePositive(index)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T ElementAt(in StableIndexHandle handle)
        {
            CollectionUtils.CheckIndexInRange(handle.Index, m_slots.m_capacity);
            return ref DataPtr[CollectionUtils.AssumePositive(m_slots.IndicesPtr[CollectionUtils.AssumePositive(handle.Index)])];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid(in StableIndexHandle handle) => m_slots.IsValid(handle.Index, handle.Version);

        /// <summary>
        /// Adds an element and returns its stable handle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StableIndexHandle Add(in T value)
        {
            if (m_slots.m_length == m_slots.m_capacity)
                Grow();

            var id = m_slots.Allocate(out var version);
            DataPtr[m_slots.m_length - 1] = value;

            return new StableIndexHandle(id, version);
        }

        /// <summary>
        /// Removes an element by handle using swap-back. Invalid / stale handles are ignored.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(in StableIndexHandle handle) => TryRemove(in handle);

        /// <summary>
        /// Removes an element by handle using swap-back.
        /// </summary>
        /// <returns>False if the handle was invalid or stale (nothing removed).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove(in StableIndexHandle handle)
        {
            if (!m_slots.Release(handle.Index, handle.Version, out var removedDense, out var movedFromDense))
                return false;

            if (removedDense != movedFromDense)
                DataPtr[removedDense] = DataPtr[movedFromDense];

            return true;
        }

        /// <summary>
        /// Resets length to 0 without zeroing memory. Versions are kept, so all existing handles stay invalid.
        /// </summary>
        public void Clear() => m_slots.Clear();

        /// <summary>
        /// Shrinks capacity to the smallest ID space that still covers every live ID (minimum one allocator block).
        /// </summary>
        public void TrimExcess()
        {
            var oldCapacity = m_slots.m_capacity;

            m_slots.TrimExcess();

            // An empty allocator trims to zero capacity (and a null buffer); keep the list created.
            if (m_slots.m_capacity == 0)
                m_slots.SetCapacity(1);

            if (m_slots.m_capacity != oldCapacity)
                ReallocateData(m_slots.m_capacity);
        }

        /// <summary>
        /// Sets the length, expanding the capacity if necessary.
        /// </summary>
        /// <param name="length">The new length.</param>
        /// <param name="options">Whether the grown data range should be zeroed.</param>
        public void Resize(int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

            var oldLength = m_slots.m_length;

            if (length > m_slots.m_capacity)
                SetCapacity(length);

            m_slots.Resize(length);

            if (length > oldLength && (options & NativeArrayOptions.ClearMemory) == NativeArrayOptions.ClearMemory)
                UnsafeUtility.MemClear(DataPtr + oldLength, (long)(length - oldLength) * sizeof(T));
        }

        /// <summary>
        /// Grows the capacity (rounded by the slot allocator). Never shrinks; use <see cref="TrimExcess"/>.
        /// </summary>
        public void SetCapacity(int capacity)
        {
            var oldCapacity = m_slots.m_capacity;

            m_slots.SetCapacity(capacity);

            if (m_slots.m_capacity != oldCapacity)
                ReallocateData(m_slots.m_capacity);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            if (m_slots.m_capacity >= MaxCapacity)
                throw new InvalidOperationException("UnsafeStableIndexList is at maximum capacity.");

            SetCapacity(m_slots.m_capacity == 0 ? 8 : (int)math.min((long)m_slots.m_capacity * 2, MaxCapacity));
        }

        /// <summary>
        /// Reallocates <see cref="DataPtr"/> to <paramref name="newCapacity"/>, preserving the live dense prefix.
        /// </summary>
        private void ReallocateData(int newCapacity)
        {
            var allocator = m_slots.m_allocator;
            CollectionUtils.CheckAllocator(allocator);

            T* newDataPtr = null;

            if (newCapacity > 0)
            {
                newDataPtr = (T*)AllocatorManager.Allocate(allocator, sizeof(T), UnsafeUtility.AlignOf<T>(), newCapacity);

                var itemsToCopy = math.min(m_slots.m_length, newCapacity);
                if (DataPtr != null && itemsToCopy > 0)
                    UnsafeUtility.MemCpy(newDataPtr, DataPtr, (long)itemsToCopy * sizeof(T));
            }

            if (CollectionUtils.ShouldDeallocate(allocator))
                AllocatorManager.Free(allocator, DataPtr);

            DataPtr = newDataPtr;
        }

        public bool Equals(UnsafeStableIndexList<T> other)
        {
            return DataPtr == other.DataPtr && m_slots.m_length == other.m_slots.m_length;
        }

        [BurstDiscard]
        public override bool Equals(object obj)
        {
            return obj is UnsafeStableIndexList<T> other && Equals(other);
        }

        public override int GetHashCode() => (int)DataPtr * 397 ^ m_slots.m_capacity;

        public static bool operator ==(UnsafeStableIndexList<T> left, UnsafeStableIndexList<T> right) => left.Equals(right);

        public static bool operator !=(UnsafeStableIndexList<T> left, UnsafeStableIndexList<T> right) => !left.Equals(right);

        /// <summary>
        /// Releases all resources (memory).
        /// </summary>
        public void Dispose()
        {
            if (IsCreated == false)
                return;

            if (CollectionUtils.ShouldDeallocate(m_slots.m_allocator))
                AllocatorManager.Free(m_slots.m_allocator, DataPtr);

            DataPtr = null;
            m_slots.Dispose();
        }

        /// <summary>
        /// Creates and schedules a job that frees the memory of this list.
        /// </summary>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (IsCreated == false)
                return inputDeps;

            var allocator = m_slots.m_allocator;

            if (CollectionUtils.ShouldDeallocate(allocator))
            {
                var jobHandle = new UnsafeStableIndexListDisposeJob
                {
                    DataPtr = DataPtr,
                    MetaIndicesPackedPtr = m_slots.MetaPtr,
                    Allocator = allocator
                }.Schedule(inputDeps);

                DataPtr = null;
                m_slots = default;
                m_slots.m_allocator = AllocatorManager.Invalid;

                return jobHandle;
            }

            DataPtr = null;
            m_slots = default;

            return inputDeps;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckNull(void* listData)
        {
            if (listData == null)
                throw new InvalidOperationException("UnsafeStableIndexList has yet to be created or has been destroyed!");
        }
    }
}
