/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Unity Collections Specialized                                             │
│  Custom-made third-party package — not affiliated with or endorsed by      │
│  Unity Technologies.                                                       │
│  Repository: https://github.com/Saesentsessis/Unity-Collections-Specialized│
└────────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections.Specialized
{
    public static class CollectionUtils
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        public static void CheckCapacityInRange(int capacity, int maxCapacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException($"Capacity {capacity} must be positive.");

            if (capacity > maxCapacity)
                throw new ArgumentException($"Capacity {capacity} value too large. Maximum capacity is {maxCapacity}.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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