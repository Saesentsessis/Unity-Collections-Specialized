using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Collections.Specialized.Tests
{
    public class NativeStableIndexListTests
    {
        [Test]
        public void EmptyList_HasZeroLengthAndIsCreated()
        {
            var list = new NativeStableIndexList<int>(Allocator.Temp);

            try
            {
                Assert.AreEqual(0, list.Length);
                Assert.IsTrue(list.IsCreated);
                Assert.GreaterOrEqual(list.Capacity, 1);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Add_SingleElement_ReturnsValidHandleWithCorrectValue()
        {
            var list = CreateList();
            try
            {
                var handle = list.Add(42);

                Assert.AreEqual(1, list.Length);
                Assert.IsTrue(list.IsValid(handle));
                Assert.AreEqual(42, list[handle]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Add_MultipleElements_IncrementsLengthAndPreservesValues()
        {
            var list = CreateList();
            try
            {
                var first = list.Add(10);
                var second = list.Add(20);
                var third = list.Add(30);

                Assert.AreEqual(3, list.Length);
                Assert.AreEqual(10, list[first]);
                Assert.AreEqual(20, list[second]);
                Assert.AreEqual(30, list[third]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void DenseIndexer_GetSet_WorksOnActiveRange()
        {
            var list = CreateList();
            try
            {
                list.Add(10);
                list.Add(20);

                Assert.AreEqual(10, list[0]);
                Assert.AreEqual(20, list[1]);

                list[1] = 99;

                Assert.AreEqual(99, list[1]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void AsReadOnlySpan_ReflectsDenseElements()
        {
            var list = CreateList();
            try
            {
                list.Add(10);
                list.Add(20);
                list.Add(30);

                CollectionAssert.AreEqual(new[] { 10, 20, 30 }, list.AsReadOnlySpan().ToArray());
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void AsSpan_ReflectsDenseElements()
        {
            var list = CreateList();
            try
            {
                list.Add(5);
                list.Add(15);

                CollectionAssert.AreEqual(new[] { 5, 15 }, list.AsSpan().ToArray());
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void AsArray_ReflectsDenseElements()
        {
            var list = CreateList();
            try
            {
                list.Add(7);
                list.Add(8);

                using var array = list.AsArray();

                Assert.AreEqual(2, array.Length);
                Assert.AreEqual(7, array[0]);
                Assert.AreEqual(8, array[1]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void ImplicitSpanConversion_MatchesAsSpan()
        {
            var list = CreateList();
            
            try
            {
                list.Add(1);
                list.Add(2);

                ReadOnlySpan<int> readOnlySpan = list;
                Span<int> span = list;

                CollectionAssert.AreEqual(readOnlySpan.ToArray(), span.ToArray());
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void ImplicitNativeArrayConversion_MatchesAsArray()
        {
            var list = CreateList();
            try
            {
                list.Add(11);
                list.Add(22);

                using NativeArray<int> array = list;

                Assert.AreEqual(11, array[0]);
                Assert.AreEqual(22, array[1]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Remove_MiddleElement_SwapBackPreservesRemainingHandles()
        {
            var list = CreateList();
            try
            {
                var first = list.Add(10);
                var second = list.Add(20);
                var third = list.Add(30);

                list.Remove(first);

                Assert.AreEqual(2, list.Length);
                Assert.IsFalse(list.IsValid(first));
                Assert.IsTrue(list.IsValid(second));
                Assert.IsTrue(list.IsValid(third));
                Assert.AreEqual(20, list[second]);
                Assert.AreEqual(30, list[third]);
                CollectionAssert.AreEqual(new[] { 30, 20 }, list.AsReadOnlySpan().ToArray());
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Remove_LastElement_InvalidatesOnlyRemovedHandle()
        {
            var list = CreateList();
            try
            {
                var first = list.Add(10);
                var second = list.Add(20);

                list.Remove(second);

                Assert.AreEqual(1, list.Length);
                Assert.IsTrue(list.IsValid(first));
                Assert.IsFalse(list.IsValid(second));
                Assert.AreEqual(10, list[first]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void IsValid_ReturnsFalse_ForStaleHandleAfterRemove()
        {
            var list = CreateList();
            try
            {
                var handle = list.Add(99);
                list.Remove(handle);

                Assert.IsFalse(list.IsValid(handle));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void IsValid_ReturnsFalse_ForNeverAllocatedIndex()
        {
            var list = CreateList();
            try
            {
                list.Add(1);

                Assert.IsFalse(list.IsValid(new StableIndexHandle(999, 1)));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void HandleIndexer_Set_UpdatesValueThroughHandle()
        {
            var list = CreateList();
            try
            {
                var handle = list.Add(1);
                list[handle] = 88;

                Assert.AreEqual(88, list[handle]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Add_AfterRemove_ReusesIdWithIncrementedVersion()
        {
            var list = CreateList();
            try
            {
                var first = list.Add(10);
                list.Add(20);

                list.Remove(first);
                var third = list.Add(30);

                Assert.AreEqual(first.Index, third.Index);
                Assert.AreNotEqual(first.Version, third.Version);
                Assert.IsFalse(list.IsValid(first));
                Assert.IsTrue(list.IsValid(third));
                Assert.AreEqual(30, list[third]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Clear_EmptiesLengthWithoutChangingCapacity()
        {
            var list = CreateList();
            try
            {
                list.Add(1);
                list.Add(2);
                var capacityBeforeClear = list.Capacity;

                list.Clear();

                Assert.AreEqual(0, list.Length);
                Assert.AreEqual(capacityBeforeClear, list.Capacity);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Clear_AllowsAddingAgain()
        {
            var list = CreateList();
            try
            {
                list.Add(1);
                list.Clear();

                var handle = list.Add(42);

                Assert.AreEqual(1, list.Length);
                Assert.IsTrue(list.IsValid(handle));
                Assert.AreEqual(42, list[handle]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Growth_ReallocatesWhenCapacityExceeded()
        {
            var list = new NativeStableIndexList<int>(1, Allocator.Temp);
            try
            {
                var initialCapacity = list.Capacity;

                for (var i = 0; i <= initialCapacity; i++)
                    list.Add(i);

                Assert.Greater(list.Capacity, initialCapacity);
                Assert.AreEqual(initialCapacity + 1, list.Length);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Growth_PreservesHandleValidityAcrossReallocation()
        {
            var list = new NativeStableIndexList<int>(1, Allocator.Temp);
            try
            {
                var handles = new StableIndexHandle[9];
                for (var i = 0; i < handles.Length; i++)
                    handles[i] = list.Add(i * 10);

                for (var i = 0; i < handles.Length; i++)
                {
                    Assert.IsTrue(list.IsValid(handles[i]), $"Handle {i} should remain valid after growth.");
                    Assert.AreEqual(i * 10, list[handles[i]]);
                }
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Equals_ComparesBackingPointer()
        {
            var left = CreateList();
            var right = CreateList();
            try
            {
                left.Add(1);
                right.Add(1);

                Assert.IsFalse(left.Equals(right));
                Assert.IsTrue(left.Equals(left));
                Assert.IsTrue(left == left);
                Assert.IsFalse(left != left);
            }
            finally
            {
                left.Dispose();
                right.Dispose();
            }
        }

        [Test]
        public void Dispose_SetsIsCreatedFalse()
        {
            var list = new NativeStableIndexList<int>(Allocator.Temp);
            list.Add(1);

            list.Dispose();

            Assert.IsFalse(list.IsCreated);
        }

        [Test]
        public void DisposeJob_CompletesAndReleasesList()
        {
            var list = new NativeStableIndexList<int>(Allocator.Temp);
            list.Add(1);

            var jobHandle = list.Dispose(default);
            jobHandle.Complete();

            Assert.IsFalse(list.IsCreated);
        }

        private static NativeStableIndexList<int> CreateList()
        {
            return new NativeStableIndexList<int>(Allocator.Temp);
        }
    }
}
