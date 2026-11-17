using System;
using NUnit.Framework;
using Unity.Jobs;

namespace Unity.Collections.Specialized.Tests
{
	/// <summary>
	/// Storage / handle behaviour is the wrapped <see cref="NativeStableIndexList{T}"/>'s (see <see cref="NativeStableIndexListTests"/>).
	/// These tests cover the forwarding surface, read-only access, generation rules, and job-system safety through the wrapper.
	/// </summary>
	public class NativeGenerationalStableIndexListTests
	{
		[Test]
		public void EmptyList_HasZeroLengthIsCreatedAndAtGenerationZero()
		{
			var list = CreateList();
			try
			{
				Assert.IsTrue(list.IsCreated);
				Assert.IsTrue(list.IsEmpty);
				Assert.AreEqual(0, list.Length);
				Assert.GreaterOrEqual(list.Capacity, 1);
				Assert.AreEqual(0U, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Add_ValueReadableByIndexAndHandle_AndBumpsGeneration()
		{
			var list = CreateList();
			try
			{
				var a = list.Add(10);
				var b = list.Add(20);

				Assert.AreEqual(2U, list.Generation);
				Assert.AreEqual(2, list.Length);
				Assert.IsFalse(list.IsEmpty);
				Assert.IsTrue(list.IsValid(a));
				Assert.AreEqual(10, list[a]);
				Assert.AreEqual(20, list[b]);
				Assert.AreEqual(10, list[0]);
				Assert.AreEqual(20, list[1]);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Add_BeyondCapacity_BumpsOncePerAdd()
		{
			var list = new NativeGenerationalStableIndexList<int>(1, Allocator.Temp);
			try
			{
				var count = list.Capacity * 3;

				for (var i = 0; i < count; i++)
					list.Add(i);

				Assert.AreEqual((uint)count, list.Generation);
				Assert.AreEqual(count - 1, list[count - 1]);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Remove_SwapBackPreservesSurvivors_AndBumpsGeneration()
		{
			var list = CreateList();
			try
			{
				var a = list.Add(10);
				var b = list.Add(20);
				var before = list.Generation;

				list.Remove(a);

				Assert.AreEqual(before + 1U, list.Generation);
				Assert.IsFalse(list.IsValid(a));
				Assert.AreEqual(20, list[b]);
				Assert.AreEqual(1, list.Length);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Remove_InvalidHandle_ThrowsAndDoesNotBump()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(1);
				list.Remove(handle);
				var before = list.Generation;

				Assert.Throws<ArgumentException>(() => list.Remove(handle));
				Assert.Throws<ArgumentException>(() => list.Remove(StableIndexHandle.Null));
				Assert.AreEqual(before, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Set_BumpsOnlyWhenValueChanges()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(5);
				var other = list.Add(2);
				var before = list.Generation;

				list.Set(handle, 5);
				Assert.AreEqual(before, list.Generation);

				list.Set(handle, 88);
				Assert.AreEqual(before + 1U, list.Generation);
				Assert.AreEqual(88, list[handle]);
				Assert.AreEqual(2, list[other]);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Set_InvalidHandle_Throws()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(1);
				list.Remove(handle);

				Assert.Throws<ArgumentException>(() => list.Set(handle, 2));
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void SetAt_BumpsOnlyWhenValueChanges()
		{
			var list = CreateList();
			try
			{
				list.Add(5);
				var before = list.Generation;

				list.SetAt(0, 5);
				Assert.AreEqual(before, list.Generation);

				list.SetAt(0, 7);
				Assert.AreEqual(before + 1U, list.Generation);
				Assert.AreEqual(7, list[0]);
			}
			finally
			{
				list.Dispose();
			}
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		[Test]
		public void SetAt_And_DenseIndexer_OutOfRange_Throw()
		{
			var list = CreateList();
			try
			{
				list.Add(1);

				Assert.Throws<IndexOutOfRangeException>(() => list.SetAt(1, 2));
				Assert.Throws<IndexOutOfRangeException>(() => { var _ = list[1]; });
				Assert.Throws<IndexOutOfRangeException>(() => { var _ = list[-1]; });
			}
			finally
			{
				list.Dispose();
			}
		}
#endif

		[Test]
		public void Clear_BumpsGeneration_InsteadOfResetting_AndKeepsHandlesInvalid()
		{
			var list = CreateList();
			try
			{
				var first = list.Add(1);
				list.Add(2);
				var before = list.Generation;

				list.Clear();

				Assert.AreEqual(before + 1U, list.Generation);
				Assert.AreEqual(0, list.Length);
				Assert.IsFalse(list.IsValid(first));

				var reused = list.Add(3);
				Assert.IsFalse(list.IsValid(first));
				Assert.IsTrue(list.IsValid(reused));
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void AsReadOnly_AndConversions_ReflectDenseElements()
		{
			var list = CreateList();
			try
			{
				list.Add(1);
				list.Add(2);
				list.Add(3);

				var view = list.AsReadOnly();
				NativeArray<int>.ReadOnly converted = list;
				var span = list.AsReadOnlySpan();
				ReadOnlySpan<int> convertedSpan = list;

				Assert.AreEqual(3, view.Length);
				Assert.AreEqual(3, span.Length);
				for (var i = 0; i < 3; i++)
				{
					Assert.AreEqual(i + 1, view[i]);
					Assert.AreEqual(i + 1, converted[i]);
					Assert.AreEqual(i + 1, span[i]);
					Assert.AreEqual(i + 1, convertedSpan[i]);
				}
			}
			finally
			{
				list.Dispose();
			}
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		[Test]
		public void AsReadOnly_SurvivesReads_ButIsInvalidatedByWrites()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(1);

				var view = list.AsReadOnly();
				var _ = list[handle] + list[0] + (int)list.Generation;
				Assert.IsTrue(list.IsValid(handle));
				Assert.AreEqual(1, view[0]);

				list.Set(handle, 2);
				Assert.Catch<InvalidOperationException>(() => { var x = view[0]; });

				view = list.AsReadOnly();
				list.Add(3);
				Assert.Catch<InvalidOperationException>(() => { var x = view[0]; });
			}
			finally
			{
				list.Dispose();
			}
		}
#endif

		[Test]
		public void Equals_ComparesBackingPointer()
		{
			var list = CreateList();
			var other = CreateList();
			try
			{
				var copy = list;

				Assert.IsTrue(list.Equals(copy));
				Assert.IsTrue(list == copy);
				Assert.IsTrue(list.Equals((object)copy));
				Assert.AreEqual(list.GetHashCode(), copy.GetHashCode());
				Assert.IsFalse(list.Equals(other));
				Assert.IsTrue(list != other);

				// Copies share the header, so they observe the same generation.
				copy.Add(1);
				Assert.AreEqual(1U, list.Generation);
			}
			finally
			{
				list.Dispose();
				other.Dispose();
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
		public void Dispose_DefaultInstance_DoesNotThrow()
		{
			var list = default(NativeGenerationalStableIndexList<int>);

			Assert.DoesNotThrow(() => list.Dispose());
			Assert.AreEqual(default(JobHandle), list.Dispose(default));
		}

		[Test]
		public void DisposeJob_CompletesAndReleasesList()
		{
			var list = new NativeGenerationalStableIndexList<int>(Allocator.TempJob);
			list.Add(1);

			var jobHandle = list.Dispose(default);
			jobHandle.Complete();

			Assert.IsFalse(list.IsCreated);
		}

		[Test]
		public void Job_ReadsThroughReadOnlyField()
		{
			var list = new NativeGenerationalStableIndexList<int>(Allocator.TempJob);
			var handles = new NativeArray<StableIndexHandle>(3, Allocator.TempJob);
			var result = new NativeArray<int>(2, Allocator.TempJob);
			try
			{
				for (var i = 0; i < handles.Length; i++)
					handles[i] = list.Add((i + 1) * 10);

				new SumJob { List = list, Handles = handles, Result = result }.Schedule().Complete();

				Assert.AreEqual(60, result[0]);
				Assert.AreEqual(3, result[1]);
			}
			finally
			{
				list.Dispose();
				handles.Dispose();
				result.Dispose();
			}
		}

		[Test]
		public void Job_WritesThroughField_UpdateSharedGeneration()
		{
			var list = new NativeGenerationalStableIndexList<int>(Allocator.TempJob);
			try
			{
				new AddJob { List = list, Count = 4 }.Schedule().Complete();

				Assert.AreEqual(4, list.Length);
				Assert.AreEqual(4U, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		[Test]
		public void ReadOnlyJobs_RunConcurrently_ButMainThreadWriteThrows()
		{
			var list = new NativeGenerationalStableIndexList<int>(Allocator.TempJob);
			var handles = new NativeArray<StableIndexHandle>(1, Allocator.TempJob);
			var resultA = new NativeArray<int>(2, Allocator.TempJob);
			var resultB = new NativeArray<int>(2, Allocator.TempJob);
			try
			{
				handles[0] = list.Add(5);

				var jobA = new SumJob { List = list, Handles = handles, Result = resultA }.Schedule();
				var jobB = new SumJob { List = list, Handles = handles, Result = resultB }.Schedule();

				Assert.Catch<InvalidOperationException>(() => list.Add(6));
				Assert.Catch<InvalidOperationException>(() => list.Set(handles[0], 7));

				JobHandle.CombineDependencies(jobA, jobB).Complete();

				Assert.AreEqual(5, resultA[0]);
				Assert.AreEqual(5, resultB[0]);
				Assert.DoesNotThrow(() => list.Add(6));
			}
			finally
			{
				list.Dispose();
				handles.Dispose();
				resultA.Dispose();
				resultB.Dispose();
			}
		}
#endif

		private static NativeGenerationalStableIndexList<int> CreateList()
		{
			return new NativeGenerationalStableIndexList<int>(Allocator.Temp);
		}

		private struct SumJob : IJob
		{
			[ReadOnly] public NativeGenerationalStableIndexList<int> List;
			[ReadOnly] public NativeArray<StableIndexHandle> Handles;
			public NativeArray<int> Result;

			public void Execute()
			{
				var sum = 0;
				for (var i = 0; i < Handles.Length; i++)
					sum += List[Handles[i]];

				Result[0] = sum;
				Result[1] = (int)List.Generation;
			}
		}

		private struct AddJob : IJob
		{
			public NativeGenerationalStableIndexList<int> List;
			public int Count;

			public void Execute()
			{
				for (var i = 0; i < Count; i++)
					List.Add(i);
			}
		}
	}
}
