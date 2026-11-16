using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Scripting;

namespace Unity.Collections.Specialized
{
    public static class CollectionUtils
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Preserve]
        public static void CheckSizeMultipleOf8(int sizeInBytes)
        {
            if ((sizeInBytes & 7) != 0)
                throw new ArgumentException($"BitArray invalid arguments: sizeInBytes {sizeInBytes} (must be multiple of 8-bytes, sizeInBytes: {sizeInBytes}).");
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckIndexInRange(int index, int length)
        {
            if ((uint)index >= (uint)length)
                throw new IndexOutOfRangeException($"Index {index} is out of range in container of '{length}' Length.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckHandleValid(bool isValid, StableIndexHandle handle)
        {
            if (!isValid)
                throw new ArgumentException(
                    $"Invalid or stale StableIndexHandle (Index={handle.Index}, Version={handle.Version}).");
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckCapacityInRange(int capacity, int maxCapacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException($"Capacity {capacity} must be positive.");

            if (capacity > maxCapacity)
                throw new ArgumentException($"Capacity {capacity} value too large. Maximum capacity is {maxCapacity}.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Preserve]
        public static void CheckByteSizeInRange(long byteSize, long maxByteSize)
        {
            if ((ulong)byteSize > (ulong)maxByteSize)
                throw new InsufficientMemoryException($"Byte size of {byteSize} is too big. It must be less than or equal to maxByteSize({maxByteSize}).");
        }

        /// <summary>
        /// Tell Burst that an integer can be assumed to map to an always positive value.
        /// </summary>
        /// <param name="value">The integer that is always positive.</param>
        /// <returns>Returns `x`, but allows the compiler to assume it is always positive.</returns>
        [return: AssumeRange(0, int.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AssumePositive(int value)
        {
            return value;
        }

        /// <summary>
        /// Tell Burst that a long can be assumed to map to an always positive value.
        /// </summary>
        /// <param name="value">The long that is always positive.</param>
        /// <returns>Returns `x`, but allows the compiler to assume it is always positive.</returns>
        [return: AssumeRange(0L, long.MaxValue)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long AssumePositive(long value)
        {
            return value;
        }
        
        /// <summary>
        /// Bitwise equality of two unmanaged values.
        /// </summary>
        /// <remarks>
        /// Sizes of 1, 2, 4, 8 and 16 bytes compare as whole integers; <c>sizeof(T)</c> is a constant per instantiation,
        /// so Burst keeps only the matching branch. Other sizes fall back to <see cref="UnsafeUtility.MemCmp"/>, which Burst
        /// lowers to a byte loop. Padding bytes take part in the comparison.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool BitwiseEquals<T>(ref T a, ref T b) where T : unmanaged
        {
            var pa = UnsafeUtility.AddressOf(ref a);
            var pb = UnsafeUtility.AddressOf(ref b);

            switch (sizeof(T))
            {
                case 1: return *(byte*)pa == *(byte*)pb;
                case 2: return *(ushort*)pa == *(ushort*)pb;
                case 4: return *(uint*)pa == *(uint*)pb;
                case 8: return *(ulong*)pa == *(ulong*)pb;
                case 16: return ((ulong*)pa)[0] == ((ulong*)pb)[0] && ((ulong*)pa)[1] == ((ulong*)pb)[1];
                default: return UnsafeUtility.MemCmp(pa, pb, sizeof(T)) == 0;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckAllocator(AllocatorManager.AllocatorHandle allocator)
        {
            if (!ShouldDeallocate(allocator))
                throw new ArgumentException($"Allocator {allocator} must not be None or Invalid");
        }
        
#if UNITY_6000_0_OR_NEWER == false && ENABLE_UNITY_COLLECTIONS_CHECKS
        public static void DisposeSafetyHandle(ref AtomicSafetyHandle safety)
        {
            AtomicSafetyHandle.CheckDeallocateAndThrow(safety);
            // If the safety handle is for a temp allocation, create a new safety handle for this instance which can be marked as invalid
            // Setting it to new AtomicSafetyHandle is not enough since the handle needs a valid node pointer in order to give the correct errors
            if (AtomicSafetyHandle.IsTempMemoryHandle(safety))
            {
                var field = typeof(AtomicSafetyHandle).GetField("staticSafetyId", BindingFlags.Instance | BindingFlags.NonPublic);
                
                if (field != null)
                {
                    var staticSafetyId = (int)field.GetValue(safety);
                    safety = AtomicSafetyHandle.Create();
                    field.SetValue(safety, staticSafetyId);
                }
            }
            AtomicSafetyHandle.Release(safety);
        }
#endif
        
        public static bool ShouldDeallocate(AllocatorManager.AllocatorHandle allocator)
        {
            // Allocator.Invalid == container is not initialized.
            // Allocator.None    == container is initialized, but container doesn't own data.
            return allocator.ToAllocator > Allocator.None;
        }
    }
}