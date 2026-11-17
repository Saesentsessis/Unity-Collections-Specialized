#if ENABLE_UNITY_COLLECTIONS_CHECKS
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections.Specialized
{
    /// <summary>
    /// Shared safety-system boilerplate for this package's <c>[NativeContainer]</c> types.
    /// </summary>
    /// <remarks>
    /// Only compiled with <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c>; every call site sits inside the same <c>#if</c>.
    /// The <c>m_Safety</c> / <c>m_DisposeSentinel</c> fields must stay on each container (the job system looks them up by name),
    /// so only the logic lives here.
    /// </remarks>
    internal static unsafe class NativeContainerSafety
    {
        /// <summary>
        /// Creates the safety handle and dispose sentinel of a newly allocated container.
        /// </summary>
        public static void Create(out AtomicSafetyHandle safety, out DisposeSentinel disposeSentinel, Allocator allocator, int staticSafetyId, bool isNestedContainer = false)
        {
            DisposeSentinel.Create(out safety, out disposeSentinel, 1, allocator);

            AtomicSafetyHandle.SetStaticSafetyId(ref safety, staticSafetyId);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(safety, true);

            if (isNestedContainer)
                AtomicSafetyHandle.SetNestedContainer(safety, true);
        }

        /// <summary>
        /// Throws if a non-default handle was already released (e.g. disposing a copy of a disposed container).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckExists(AtomicSafetyHandle safety)
        {
            if (AtomicSafetyHandle.IsDefaultValue(safety) == false)
                AtomicSafetyHandle.CheckExistsAndThrow(safety);
        }

        /// <summary>
        /// Releases the safety handle and dispose sentinel on the main thread (synchronous dispose).
        /// </summary>
        public static void Dispose(ref AtomicSafetyHandle safety, ref DisposeSentinel disposeSentinel)
        {
            DisposeSentinel.Dispose(ref safety, ref disposeSentinel);
        }

        /// <summary>
        /// Creates a <see cref="NativeArray{T}"/> view over a container's dense buffer that is guarded by the
        /// container's secondary version (structural changes invalidate the view, element access does not).
        /// </summary>
        public static NativeArray<T> CreateArrayView<T>(void* data, int length, AtomicSafetyHandle safety) where T : unmanaged
        {
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(safety);

            var arraySafety = safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(data, length, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);

            return array;
        }
    }
}
#endif
