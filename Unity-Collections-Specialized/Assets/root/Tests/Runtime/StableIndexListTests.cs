using System.Linq;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Collections.Specialized.Tests
{
    public class StableIndexListTests
    {
        [Test]
        public void EmptyList_HasZeroCountAndMinimumCapacity()
        {
            var list = new StableIndexList<int>(Allocator.Temp);

            try
            {
                Assert.AreEqual(0, list.Count);
                Assert.GreaterOrEqual(list.Capacity, 1);
                Assert.IsTrue(list.Data.IsEmpty);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Constructor_ClampsCapacityToAtLeastOne()
        {
            var list = new StableIndexList<int>(0, Allocator.Temp);

            try
            {
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
            using var list = CreateList();

            var handle = list.Add(42);

            Assert.AreEqual(1, list.Count);
            Assert.IsTrue(list.IsValid(handle));
            Assert.AreEqual(42, list[handle]);
        }

        [Test]
        public void Add_MultipleElements_IncrementsCountAndPreservesValues()
        {
            using var list = CreateList();

            var first = list.Add(10);
            var second = list.Add(20);
            var third = list.Add(30);

            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(10, list[first]);
            Assert.AreEqual(20, list[second]);
            Assert.AreEqual(30, list[third]);
        }

        [Test]
        public void Data_ReturnsDenseStorageInOrder()
        {
            using var list = CreateList();

            list.Add(10);
            list.Add(20);
            list.Add(30);

            CollectionAssert.AreEqual(new[] { 10, 20, 30 }, list.Data.ToArray());
        }

        [Test]
        public void Remove_MiddleElement_SwapBackPreservesRemainingHandles()
        {
            using var list = CreateList();

            var first = list.Add(10);
            var second = list.Add(20);
            var third = list.Add(30);

            list.Remove(first);

            Assert.AreEqual(2, list.Count);
            Assert.IsFalse(list.IsValid(first));
            Assert.IsTrue(list.IsValid(second));
            Assert.IsTrue(list.IsValid(third));
            Assert.AreEqual(20, list[second]);
            Assert.AreEqual(30, list[third]);
            CollectionAssert.AreEqual(new[] { 30, 20 }, list.Data.ToArray());
        }

        [Test]
        public void Remove_FirstElement_KeepsTrailingHandlesValid()
        {
            using var list = CreateList();

            var first = list.Add(1);
            var second = list.Add(2);

            list.Remove(first);

            Assert.AreEqual(1, list.Count);
            Assert.IsFalse(list.IsValid(first));
            Assert.IsTrue(list.IsValid(second));
            Assert.AreEqual(2, list[second]);
        }

        [Test]
        public void Remove_LastElement_InvalidatesOnlyRemovedHandle()
        {
            using var list = CreateList();

            var first = list.Add(10);
            var second = list.Add(20);

            list.Remove(second);

            Assert.AreEqual(1, list.Count);
            Assert.IsTrue(list.IsValid(first));
            Assert.IsFalse(list.IsValid(second));
            Assert.AreEqual(10, list[first]);
        }

        [Test]
        public void IsValid_ReturnsFalse_ForStaleHandleAfterRemove()
        {
            using var list = CreateList();

            var handle = list.Add(99);
            list.Remove(handle);

            Assert.IsFalse(list.IsValid(handle));
        }

        [Test]
        public void IsValid_ReturnsFalse_ForNeverAllocatedIndex()
        {
            using var list = CreateList();

            list.Add(1);

            var neverAllocated = new StableIndexHandle(999, 1);

            Assert.IsFalse(list.IsValid(neverAllocated));
        }

        [Test]
        public void IsValid_ReturnsFalse_ForNegativeIndex()
        {
            using var list = CreateList();

            Assert.IsFalse(list.IsValid(StableIndexHandle.Null));
        }

        [Test]
        public void Indexer_Set_UpdatesValueThroughHandle()
        {
            using var list = CreateList();

            var handle = list.Add(1);
            list[handle] = 77;

            Assert.AreEqual(77, list[handle]);
        }

        [Test]
        public void Add_AfterRemove_ReusesIdWithIncrementedVersion()
        {
            using var list = CreateList();

            var first = list.Add(10);
            var second = list.Add(20);

            list.Remove(first);
            var third = list.Add(30);

            Assert.AreEqual(first.Index, third.Index);
            Assert.AreNotEqual(first.Version, third.Version);
            Assert.IsFalse(list.IsValid(first));
            Assert.IsTrue(list.IsValid(third));
            Assert.AreEqual(30, list[third]);
        }

        [Test]
        public void Growth_ReallocatesWhenCapacityExceeded()
        {
            using var list = new StableIndexList<int>(1, Allocator.Temp);

            var initialCapacity = list.Capacity;

            for (var i = 0; i <= initialCapacity; i++)
                list.Add(i);

            Assert.Greater(list.Capacity, initialCapacity);
            Assert.AreEqual(initialCapacity + 1, list.Count);
        }

        [Test]
        public void Growth_PreservesHandleValidityAcrossReallocation()
        {
            using var list = new StableIndexList<int>(1, Allocator.Temp);

            var handles = new StableIndexHandle[9];
            for (var i = 0; i < handles.Length; i++)
                handles[i] = list.Add(i * 10);

            for (var i = 0; i < handles.Length; i++)
            {
                Assert.IsTrue(list.IsValid(handles[i]), $"Handle {i} should remain valid after growth.");
                Assert.AreEqual(i * 10, list[handles[i]]);
            }
        }

        [Test]
        public void RemoveSequence_MaintainsValidHandlesForSurvivors()
        {
            using var list = CreateList();

            var handles = new StableIndexHandle[5];
            for (var i = 0; i < handles.Length; i++)
                handles[i] = list.Add(i);

            list.Remove(handles[1]);
            list.Remove(handles[3]);

            Assert.IsTrue(list.IsValid(handles[0]));
            Assert.IsFalse(list.IsValid(handles[1]));
            Assert.IsTrue(list.IsValid(handles[2]));
            Assert.IsFalse(list.IsValid(handles[3]));
            Assert.IsTrue(list.IsValid(handles[4]));
            Assert.AreEqual(3, list.Count);
        }

        [Test]
        public void Dispose_AllowsSecondCallWithoutThrowing()
        {
            var list = new StableIndexList<int>(Allocator.Temp);
            list.Add(1);

            list.Dispose();
            list.Dispose();
        }

        private static StableIndexList<int> CreateList()
        {
            return new StableIndexList<int>(Allocator.Temp);
        }
    }
}
