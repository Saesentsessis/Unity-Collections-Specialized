using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Specialized.Tests
{
    public class UnsafeStableIndexListTests
    {
        [Test]
        public void EmptyList_IsEmptyAndCreated()
        {
            var list = new UnsafeStableIndexList<int>(4, Allocator.Temp);

            try
            {
                Assert.IsTrue(list.IsCreated);
                Assert.IsTrue(list.IsEmpty);
                Assert.AreEqual(0, list.Length);
                Assert.GreaterOrEqual(list.Capacity, 1);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Constructor_ClampsInitialCapacityToAtLeastOne()
        {
            var list = new UnsafeStableIndexList<int>(0, Allocator.Temp);

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
            var list = CreateList();
            try
            {
                var handle = list.Add(42);

                Assert.AreEqual(1, list.Length);
                Assert.IsFalse(list.IsEmpty);
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
        public void ElementAt_ByIndexAndHandle_ReturnSameValues()
        {
            var list = CreateList();
            try
            {
                var handle = list.Add(55);

                Assert.AreEqual(55, list.ElementAt(0));
                Assert.AreEqual(55, list.ElementAt(handle));
                Assert.AreEqual(55, list[handle]);
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
                Assert.AreEqual(30, list[0]);
                Assert.AreEqual(20, list[1]);
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
                Assert.IsTrue(list.IsEmpty);
                Assert.AreEqual(capacityBeforeClear, list.Capacity);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Clear_InvalidatesExistingHandlesAndResetsIdPool()
        {
            var list = CreateList();
            try
            {
                var first = list.Add(1);
                var second = list.Add(2);

                list.Clear();

                Assert.IsFalse(list.IsValid(first));
                Assert.IsFalse(list.IsValid(second));

                var reused = list.Add(99);

                // ID pool was reset, so the first reminted ID starts at 0 again.
                Assert.AreEqual(0, reused.Index);
                Assert.AreEqual(1, reused.Version);
                Assert.IsTrue(list.IsValid(reused));
                Assert.AreEqual(99, list[reused]);
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
            var list = new UnsafeStableIndexList<int>(1, Allocator.Temp);
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
            var list = new UnsafeStableIndexList<int>(1, Allocator.Temp);
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
        public void Resize_IncreasesLengthAndCapacityWhenNeeded()
        {
            var list = CreateList();
            try
            {
                list.Resize(5, NativeArrayOptions.ClearMemory);

                Assert.AreEqual(5, list.Length);
                Assert.GreaterOrEqual(list.Capacity, 5);
                Assert.AreEqual(0, list[0]);
                Assert.AreEqual(0, list[4]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public unsafe void Length_Grow_MintsHandlesForNewSlots()
        {
            var list = CreateList();
            try
            {
                list.Length = 3;

                Assert.AreEqual(3, list.Length);

                for (var dense = 0; dense < 3; dense++)
                {
                    var reverseId = list.MetaPtr[dense].ReverseId;
                    var version = list.MetaPtr[dense].Version;
                    var handle = new StableIndexHandle(reverseId, version);

                    Assert.IsTrue(list.IsValid(handle), $"Dense slot {dense} should have a valid handle.");
                    Assert.AreEqual(dense, list.IndicesPtr[reverseId]);
                }
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Length_SetSmaller_TruncatesWithoutThrowing()
        {
            var list = CreateList();
            try
            {
                list.Add(1);
                list.Add(2);
                list.Add(3);

                list.Length = 1;

                Assert.AreEqual(1, list.Length);
                Assert.AreEqual(1, list[0]);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public unsafe void Resize_Grow_ClearMemory_MintsAndZeroesData()
        {
            var list = CreateList();
            try
            {
                list.Add(42);
                list.Resize(4, NativeArrayOptions.ClearMemory);

                Assert.AreEqual(4, list.Length);
                Assert.AreEqual(42, list[0]);
                Assert.AreEqual(0, list[1]);
                Assert.AreEqual(0, list[2]);
                Assert.AreEqual(0, list[3]);

                for (var dense = 1; dense < 4; dense++)
                {
                    var handle = new StableIndexHandle(list.MetaPtr[dense].ReverseId, list.MetaPtr[dense].Version);
                    Assert.IsTrue(list.IsValid(handle));
                }
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void TrimExcess_SetsCapacityToLength()
        {
            var list = CreateList();
            try
            {
                list.Add(1);
                list.Add(2);
                list.Add(3);
                list.Add(4);
                var fourth = list.Add(5);

                list.Remove(fourth);

                list.TrimExcess();

                Assert.AreEqual(4, list.Length);
                Assert.AreEqual(4, list.Capacity);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void SetCapacity_IncreasesCapacityWithoutChangingLength()
        {
            var list = CreateList();
            try
            {
                list.Add(1);
                list.Add(2);
                var lengthBefore = list.Length;

                list.SetCapacity(32);

                Assert.AreEqual(lengthBefore, list.Length);
                Assert.GreaterOrEqual(list.Capacity, 32);
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void Equals_ComparesDataPointerAndLength()
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
            var list = CreateList();
            list.Add(1);

            list.Dispose();

            Assert.IsFalse(list.IsCreated);
            Assert.IsTrue(list.IsEmpty);
        }

        [Test]
        public void DisposeJob_CompletesAndReleasesList()
        {
            var list = CreateList();
            list.Add(1);

            var jobHandle = list.Dispose(default);
            jobHandle.Complete();

            Assert.IsFalse(list.IsCreated);
        }

        [Test]
        public void Destroy_FreesHeapAllocation()
        {
            unsafe
            {
                var listPtr = AllocatorManager.Allocate<UnsafeStableIndexList<int>>(Allocator.Temp);
                *listPtr = new UnsafeStableIndexList<int>(4, Allocator.Temp);
                listPtr->Add(7);

                UnsafeStableIndexList<int>.Destroy(listPtr);
            }
        }

        [Test]
        public void RemoveSequence_MaintainsValidHandlesForSurvivors()
        {
            var list = CreateList();
            try
            {
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
                Assert.AreEqual(3, list.Length);
            }
            finally
            {
                list.Dispose();
            }
        }

        private static UnsafeStableIndexList<int> CreateList()
        {
            return new UnsafeStableIndexList<int>(4, Allocator.Temp);
        }
    }
}
