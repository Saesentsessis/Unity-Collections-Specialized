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
        // DataPtr, then the embedded UnsafeSlotAllocator (_metaAndIndicesPtr, m_length, m_capacity, m_allocator)
        [NativeDisableUnsafePtrRestriction]
        public readonly void* DataPtr;
        [NativeDisableUnsafePtrRestriction]
        public readonly void* MetaAndIndicesPtr;
        public readonly int m_length;
        public readonly int m_capacity;
        public readonly AllocatorManager.AllocatorHandle Allocator;
#pragma warning restore 169
    }

    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    [DebuggerDisplay("Length = {_listData == null ? default : _listData->Length}")]
    public struct NativeStableIndexList<T> : INativeDisposable, IEquatable<NativeStableIndexList<T>> where T : unmanaged
    {
        // Safety is provided by m_Safety; the raw pointer must be allowed inside job structs.
        [NativeDisableUnsafePtrRestriction]
        private unsafe UnsafeStableIndexList<T>* _listData;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        // ReSharper disable once InconsistentNaming
        // The AtomicSafetyHandle field must be named exactly 'm_Safety'.
        internal AtomicSafetyHandle m_Safety;

        // ReSharper disable once InconsistentNaming
        // DisposeSentinel is a class; it must be nulled in job copies or the struct cannot be scheduled.
        [NativeSetClassTypeToNullOnSchedule]
        internal DisposeSentinel m_DisposeSentinel;

        // ReSharper disable once InconsistentNaming
        // Statically register this type with the safety system, using a name derived from the type itself
        internal static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeStableIndexList<T>>();
#endif

        public NativeStableIndexList(Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) : this(8, allocator, options) { }

        public unsafe NativeStableIndexList(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
            : this(CreateListData(capacity, allocator, options), allocator) { }

        /// <summary>
        /// Wraps an already constructed, heap-allocated list header and takes ownership of it.
        /// </summary>
        /// <remarks>
        /// The header may be a larger struct whose first field is an <see cref="UnsafeStableIndexList{T}"/>
        /// (see <see cref="NativeGenerationalStableIndexList{T}"/>); <see cref="Dispose()"/> frees the whole block.
        /// </remarks>
        internal unsafe NativeStableIndexList(UnsafeStableIndexList<T>* listData, Allocator allocator)
        {
            _listData = listData;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeContainerSafety.Create(out m_Safety, out m_DisposeSentinel, allocator, s_staticSafetyId, UnsafeUtility.IsNativeContainerType<T>());
#endif
        }

        private static unsafe UnsafeStableIndexList<T>* CreateListData(int capacity, Allocator allocator, NativeArrayOptions options)
        {
            CollectionUtils.CheckAllocator(allocator);

            var listData = AllocatorManager.Allocate<UnsafeStableIndexList<T>>(allocator);
            *listData = new UnsafeStableIndexList<T>(capacity, allocator, options);
            return listData;
        }

        /// <summary>
        /// The owned list header. Access must be preceded by the matching safety check.
        /// </summary>
        internal readonly unsafe UnsafeStableIndexList<T>* ListData => _listData;

        public unsafe int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                CheckRead();
                return _listData->Length;
            }
        }

        public unsafe int Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                CheckRead();
                return _listData->Capacity;
            }
        }

        public unsafe bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsCreated || Length == 0;
        }

        public unsafe bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _listData != null;
        }

        /// <summary>
        /// Returns a <see cref="NativeArray{T}"/> view of the dense elements. Structural changes (Add / Remove / Clear)
        /// invalidate the view; element access through handles does not.
        /// </summary>
        public readonly unsafe NativeArray<T> AsArray()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return NativeContainerSafety.CreateArrayView<T>(_listData->DataPtr, _listData->Length, m_Safety);
#else
            return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(_listData->DataPtr, _listData->Length, Allocator.None);
#endif
        }

        public readonly unsafe Span<T> AsSpan()
        {
            CheckWrite();
            return new Span<T>(_listData->DataPtr, _listData->Length);
        }

        public readonly unsafe ReadOnlySpan<T> AsReadOnlySpan()
        {
            CheckRead();
            return new ReadOnlySpan<T>(_listData->DataPtr, _listData->Length);
        }

        /// <summary>
        /// Adds an element using the Stable Index routing.
        /// </summary>
        /// <param name="value">The value to add to the list.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe StableIndexHandle Add(T value)
        {
            CheckWriteAndBumpSecondaryVersion();
            return _listData->Add(in value);
        }

        /// <summary>
        /// Removes an element using its Handle, maintaining dense storage contiguity.
        /// Throws for invalid handles when collection checks are enabled.
        /// </summary>
        /// <param name="handle">The handle pointing to the element, that should be removed.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Remove(in StableIndexHandle handle)
        {
            CheckWriteAndBumpSecondaryVersion();
            CollectionUtils.CheckHandleValid(_listData->IsValid(in handle), handle);
            _listData->Remove(in handle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool IsValid(in StableIndexHandle handle)
        {
            CheckRead();
            return _listData->IsValid(in handle);
        }

        /// <summary>
        /// Resets length to 0 without zeroing allocated memory.
        /// </summary>
        /// <remarks>
        /// Does not change the capacity. Versions are kept, so all existing handles stay invalid.
        /// </remarks>
        public unsafe void Clear()
        {
            CheckWriteAndBumpSecondaryVersion();
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
            NativeContainerSafety.CheckExists(m_Safety);
#endif
            if (IsCreated == false)
                return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeContainerSafety.Dispose(ref m_Safety, ref m_DisposeSentinel);
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
            NativeContainerSafety.CheckExists(m_Safety);
#endif
            if (IsCreated == false)
                return inputDeps;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // DisposeSentinel must be cleared on the main thread; memory free can run in the job.
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            var jobHandle = new NativeStableIndexListDisposeJob
            {
                Data = new NativeStableIndexListDispose
                {
                    ListData = (UntypedNativeStableList*)_listData,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety
#endif
                }
            }.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            _listData = null;

            return jobHandle;
        }

        public unsafe T this[int index]
        {
            get
            {
                CheckRead();
                CollectionUtils.CheckIndexInRange(index, _listData->Length);
                return UnsafeUtility.ReadArrayElement<T>(_listData->DataPtr, index);
            }

            set
            {
                CheckWriteAndBumpSecondaryVersion();
                CollectionUtils.CheckIndexInRange(index, _listData->Length);
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
                CheckWrite();
                return ref _listData->ElementAt(in handle);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly void CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly void CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly void CheckWriteAndBumpSecondaryVersion()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }

        public static implicit operator Span<T>(in NativeStableIndexList<T> source) => source.AsSpan();

        public static implicit operator ReadOnlySpan<T>(in NativeStableIndexList<T> source) => source.AsReadOnlySpan();

        public static implicit operator NativeArray<T>(in NativeStableIndexList<T> source) => source.AsArray();

        public static bool operator ==(NativeStableIndexList<T> left, NativeStableIndexList<T> right) => left.Equals(right);

        public static bool operator !=(NativeStableIndexList<T> left, NativeStableIndexList<T> right) => !left.Equals(right);
    }
}
