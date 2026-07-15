using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Specialized
{
    [NativeContainer]
    public unsafe struct NativeStableIndexListDispose
    {
        [NativeDisableUnsafePtrRestriction]
        public UntypedNativeStableList* ListData;
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public AtomicSafetyHandle m_Safety;
#endif
        
        public void Dispose()
        {
            var listData = (UnsafeStableIndexList<int>*)ListData;
            UnsafeStableIndexList<int>.Destroy(listData);
        }
    }

    [BurstCompile]
    public struct NativeStableIndexListDisposeJob : IJob
    {
        public NativeStableIndexListDispose Data;
        
        public void Execute()
        {
            Data.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct UntypedNativeStableList
    {
#pragma warning disable 169
        // Binary layout must match UnsafeStableIndexList<T> (T* is pointer-sized for all T):
        // DataPtr, _metaAndIndicesPtr, m_length, m_capacity, m_idCapacity, Allocator
        [NativeDisableUnsafePtrRestriction]
        public readonly void* DataPtr;
        [NativeDisableUnsafePtrRestriction]
        public readonly void* MetaAndIndicesPtr;
        public readonly int m_length;
        public readonly int m_capacity;
        public readonly int m_idCapacity;
        public readonly AllocatorManager.AllocatorHandle Allocator;
#pragma warning restore 169
    }
    
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    [DebuggerDisplay("Length = {_listData == null ? default : _listData->m_length}")]
    public struct NativeStableIndexList<T> : IDisposable, IEquatable<NativeStableIndexList<T>> where T : unmanaged
    {
        private unsafe UnsafeStableIndexList<T>* _listData;
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        // ReSharper disable once InconsistentNaming
        // The AtomicSafetyHandle field must be named exactly 'm_Safety'.
        internal AtomicSafetyHandle m_Safety;
        
        // ReSharper disable once InconsistentNaming
        internal DisposeSentinel m_DisposeSentinel;
        
        // ReSharper disable once InconsistentNaming
        // Statically register this type with the safety system, using a name derived from the type itself
        internal static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeStableIndexList<T>>();
#endif
        
        public NativeStableIndexList(Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) : this(8, allocator, options) { }
        
        public unsafe NativeStableIndexList(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            _listData = AllocatorManager.Allocate<UnsafeStableIndexList<T>>(allocator);
            *_listData = new UnsafeStableIndexList<T>(capacity, allocator, options);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, allocator);

            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);

            if (UnsafeUtility.IsNativeContainerType<T>()) 
                AtomicSafetyHandle.SetNestedContainer(m_Safety, true);
#endif
        }
        
        public unsafe int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Check that you can read from the native container right now.
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                return _listData->m_length;
            }
        }

        public unsafe int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Check that you can read from the native container right now.
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif          
                return _listData->m_capacity;
            }
        }
        
        public unsafe bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _listData != null;
        }
        
        public readonly unsafe NativeArray<T> AsArray()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Check that it's safe for you to use the buffer pointer to construct a view right now.
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
        
            // Make a copy of the AtomicSafetyHandle, and mark the copy to use the secondary version instead of the primary
            var arraySafety = m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);
#endif

            // Create a new NativeArray which aliases the buffer, using the current size. This doesn't allocate or copy
            // any data, it just sets up a NativeArray<T> which points at the m_Buffer.
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(_listData->DataPtr, _listData->m_length, Allocator.None);
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Set the AtomicSafetyHandle on the newly created NativeArray to be the one that you copied from your handle
            // and made to use the secondary version.
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);
#endif
        
            return array;
        }

        public readonly unsafe Span<T> AsSpan()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            return new Span<T>(_listData->DataPtr, _listData->m_length);
        }

        public readonly unsafe ReadOnlySpan<T> AsReadOnlySpan()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return new ReadOnlySpan<T>(_listData->DataPtr, _listData->m_length);
        }
        
        /// <summary>
        /// Adds an element using the Stable Index routing.
        /// </summary>
        /// <param name="value">The value to add to the list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe StableIndexHandle Add(T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            return _listData->Add(in value);
        }
        
        /// <summary>
        /// Removes an element using its Handle, maintaining dense storage contiguity.
        /// </summary>
        /// <param name="handle">The handle pointing to the element, that should be removed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Remove(in StableIndexHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            CollectionUtils.CheckHandleValid(_listData->IsValid(in handle), handle);
            _listData->Remove(in handle);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsValid(in StableIndexHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return _listData->IsValid(in handle);
        }
        
        /// <summary>
        /// Resets length and ID capacity to 0 without zeroing allocated memory.
        /// </summary>
        /// <remarks>
        /// Does not change the capacity. All existing handles become invalid.
        /// </remarks>
        public unsafe void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            _listData->Clear();
        }
        
        public unsafe bool Equals(NativeStableIndexList<T> other)
        {
            return _listData == other._listData;
        }

        [BurstDiscard]
        public override bool Equals(object obj)
        {
            return obj is NativeStableIndexList<T> other && Equals(other);
        }

        public override unsafe int GetHashCode() => _listData->GetHashCode();
        
        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// </summary>
        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (AtomicSafetyHandle.IsDefaultValue(m_Safety) == false)
                AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
            if (IsCreated == false)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            UnsafeStableIndexList<T>.Destroy(_listData);
            _listData = null;
        }
        
        /// <summary>
        /// Creates and schedules a job that releases all resources (memory and safety handles) of this list.
        /// </summary>
        /// <param name="inputDeps">The dependency for the new job.</param>
        /// <returns>The handle of the new job. The job depends upon `inputDeps` and releases all resources (memory and safety handles) of this list.</returns>
        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (AtomicSafetyHandle.IsDefaultValue(m_Safety) == false)
                AtomicSafetyHandle.CheckExistsAndThrow(m_Safety);
#endif
            if (IsCreated == false)
                return inputDeps;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // DisposeSentinel must be cleared on the main thread; memory free can run in the job.
            DisposeSentinel.Clear(ref m_DisposeSentinel);

            var jobHandle = new NativeStableIndexListDisposeJob
            {
                Data = new NativeStableIndexListDispose
                {
                    ListData = (UntypedNativeStableList*)_listData,
                    m_Safety = m_Safety
                }
            }.Schedule(inputDeps);
            AtomicSafetyHandle.Release(m_Safety);
#else
            var jobHandle = new NativeStableIndexListDisposeJob
            {
                Data = new NativeStableIndexListDispose
                {
                    ListData = (UntypedNativeStableList*)_listData
                }
            }.Schedule(inputDeps);
#endif
            _listData = null;

            return jobHandle;
        }
        
        public unsafe T this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Check that you can read from the native container right now.
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                
                // Read from the buffer and return the value
                return UnsafeUtility.ReadArrayElement<T>(_listData->DataPtr, index);
            }

            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Check that you can write to the native container right now.
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                // Write the value into the buffer
                UnsafeUtility.WriteArrayElement(_listData->DataPtr, index, value);
            }
        }
        
        /// <summary>
        /// Stable Handle Accessor. Maps Handle to Dense Index.
        /// </summary>
        /// <param name="handle">A handle.</param>
        /// <value>The element of that handle.</value>
        /// <remarks>
        /// Returns a writable <c>ref</c>. Safety uses <see cref="AtomicSafetyHandle.CheckWriteAndThrow"/>
        /// without bumping the secondary version, so existing <see cref="AsArray"/> views stay valid
        /// across handle access. Structural mutations (Add/Remove/Clear/dense set) still bump.
        /// </remarks>
        public unsafe ref T this[in StableIndexHandle handle]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return ref _listData->ElementAt(in handle);
            }
        }
        
        public static implicit operator Span<T>(in NativeStableIndexList<T> source) => source.AsSpan();
        
        public static implicit operator ReadOnlySpan<T>(in NativeStableIndexList<T> source) => source.AsReadOnlySpan();

        public static implicit operator NativeArray<T>(in NativeStableIndexList<T> source) => source.AsArray();

        public static bool operator ==(NativeStableIndexList<T> left, NativeStableIndexList<T> right) => left.Equals(right);

        public static bool operator !=(NativeStableIndexList<T> left, NativeStableIndexList<T> right) => !left.Equals(right);
    }
}