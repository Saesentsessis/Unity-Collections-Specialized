using System.Reflection;
using NUnit.Framework;

namespace Unity.Collections.Specialized.Tests
{
	/// <summary>
	/// Storage / handle behaviour is the wrapped <see cref="UnsafeStableIndexList{T}"/>'s (see <see cref="UnsafeStableIndexListTests"/>).
	/// These tests cover the forwarding surface and the generation rules.
	/// </summary>
	public class UnsafeGenerationalStableIndexListTests
	{
		[Test]
		public void EmptyList_IsEmptyCreatedAndAtGenerationZero()
		{
			var list = CreateList();
			try
			{
				Assert.IsTrue(list.IsCreated);
				Assert.IsTrue(list.IsEmpty);
				Assert.AreEqual(0, list.Length);
				Assert.GreaterOrEqual(list.Capacity, 1);
				Assert.AreEqual(0U, list.Generation);
				Assert.AreEqual(Allocator.Temp, list.Allocator.ToAllocator);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Add_ReturnsValidHandle_ValueReadableEverywhere_AndBumpsGeneration()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(42);

				Assert.AreEqual(1U, list.Generation);
				Assert.AreEqual(1, list.Length);
				Assert.IsTrue(list.IsValid(handle));
				Assert.AreEqual(42, list[handle]);
				Assert.AreEqual(42, list[0]);
				Assert.AreEqual(42, list.ElementAt(0));
				Assert.AreEqual(42, list.ElementAt(handle));
				unsafe
				{
					Assert.AreEqual(42, list.DataPtr[0]);
					Assert.AreEqual(handle.Index, list.MetaPtr[0].ReverseId);
					Assert.AreEqual(0, list.IndicesPtr[handle.Index]);
				}
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Add_BeyondCapacity_BumpsOncePerAdd()
		{
			var list = new UnsafeGenerationalStableIndexList<int>(1, Allocator.Temp);
			try
			{
				var initialCapacity = list.Capacity;
				var count = initialCapacity * 3;

				for (var i = 0; i < count; i++)
					list.Add(i);

				Assert.Greater(list.Capacity, initialCapacity);
				Assert.AreEqual((uint)count, list.Generation);
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
				var c = list.Add(30);
				var before = list.Generation;

				list.Remove(a);

				Assert.AreEqual(before + 1U, list.Generation);
				Assert.IsFalse(list.IsValid(a));
				Assert.AreEqual(20, list[b]);
				Assert.AreEqual(30, list[c]);
				Assert.AreEqual(2, list.Length);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void TryRemove_StaleHandle_ReturnsFalseAndDoesNotBump()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(1);
				Assert.IsTrue(list.TryRemove(handle));
				var before = list.Generation;

				Assert.IsFalse(list.TryRemove(handle));
				Assert.IsFalse(list.TryRemove(StableIndexHandle.Null));
				list.Remove(handle);

				Assert.AreEqual(before, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

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
				Assert.IsTrue(list.IsEmpty);

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
		public void Clear_OnEmptyList_StillBumps()
		{
			var list = CreateList();
			try
			{
				list.Clear();

				Assert.AreEqual(1U, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public unsafe void Resize_Grow_BumpsGenerationAndMintsValidHandles()
		{
			var list = CreateList();
			try
			{
				list.Resize(5, NativeArrayOptions.ClearMemory);

				Assert.AreEqual(1U, list.Generation);
				Assert.AreEqual(5, list.Length);
				for (var dense = 0; dense < list.Length; dense++)
				{
					var handle = new StableIndexHandle(list.MetaPtr[dense].ReverseId, list.MetaPtr[dense].Version);
					Assert.IsTrue(list.IsValid(handle));
					Assert.AreEqual(0, list[handle]);
				}
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void LengthSetter_BumpsGenerationLikeResize()
		{
			var list = CreateList();
			try
			{
				list.Length = 3;
				Assert.AreEqual(1U, list.Generation);

				list.Length = 1;
				Assert.AreEqual(2U, list.Generation);
				Assert.AreEqual(1, list.Length);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void CapacityChanges_DoNotBumpGeneration()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(7);
				var before = list.Generation;

				list.SetCapacity(64);
				list.Capacity = 128;
				Assert.GreaterOrEqual(list.Capacity, 128);

				list.TrimExcess();

				Assert.AreEqual(before, list.Generation);
				Assert.IsTrue(list.IsValid(handle));
				Assert.AreEqual(7, list[handle]);
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
				var before = list.Generation;

				list.Set(handle, 5);
				Assert.AreEqual(before, list.Generation);

				list.Set(handle, 6);
				Assert.AreEqual(before + 1U, list.Generation);
				Assert.AreEqual(6, list[handle]);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void DenseSetter_BumpsOnlyWhenValueChanges()
		{
			var list = CreateList();
			try
			{
				list.Add(5);
				var before = list.Generation;

				list[0] = 5;
				Assert.AreEqual(before, list.Generation);

				list[0] = 9;
				Assert.AreEqual(before + 1U, list.Generation);
				Assert.AreEqual(9, list[0]);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void RefAccessors_WritesAreUntracked_UntilBumpGeneration()
		{
			var list = CreateList();
			try
			{
				var handle = list.Add(1);
				var before = list.Generation;

				list[handle] = 2;
				list.ElementAt(handle) = 3;
				list.ElementAt(0) += 1;

				Assert.AreEqual(4, list[handle]);
				Assert.AreEqual(before, list.Generation);

				list.BumpGeneration();
				Assert.AreEqual(before + 1U, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Generation_WrapsAroundUnchecked()
		{
			var list = CreateList();
			try
			{
				object boxed = list;
				typeof(UnsafeGenerationalStableIndexList<int>)
					.GetField("m_generation", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(boxed, uint.MaxValue);
				list = (UnsafeGenerationalStableIndexList<int>)boxed;

				list.Add(1);

				Assert.AreEqual(0U, list.Generation);
			}
			finally
			{
				list.Dispose();
			}
		}

		[Test]
		public void Equals_ComparesListAndGeneration()
		{
			var list = CreateList();
			var other = CreateList();
			try
			{
				list.Add(1);
				var copy = list;

				Assert.IsTrue(list.Equals(copy));
				Assert.IsTrue(list == copy);
				Assert.IsTrue(list.Equals((object)copy));
				Assert.AreEqual(list.GetHashCode(), copy.GetHashCode());
				Assert.IsFalse(list.Equals(other));

				copy.BumpGeneration();

				Assert.IsFalse(list == copy);
				Assert.IsTrue(list != copy);
			}
			finally
			{
				list.Dispose();
				other.Dispose();
			}
		}

		[Test]
		public void Dispose_ClearsIsCreatedAndResetsGeneration()
		{
			var list = CreateList();
			list.Add(1);

			list.Dispose();

			Assert.IsFalse(list.IsCreated);
			Assert.IsTrue(list.IsEmpty);
			Assert.AreEqual(0U, list.Generation);
		}

		[Test]
		public void DisposeJob_CompletesAndReleasesList()
		{
			var list = new UnsafeGenerationalStableIndexList<int>(4, Allocator.TempJob);
			list.Add(1);

			var jobHandle = list.Dispose(default);
			jobHandle.Complete();

			Assert.IsFalse(list.IsCreated);
			Assert.AreEqual(0U, list.Generation);
		}

		[Test]
		public unsafe void Destroy_FreesHeapAllocation()
		{
			var listPtr = AllocatorManager.Allocate<UnsafeGenerationalStableIndexList<int>>(Allocator.Persistent);
			*listPtr = new UnsafeGenerationalStableIndexList<int>(4, Allocator.Persistent);
			listPtr->Add(1);

			// Frees the inner buffers and the header block through the plain list's Destroy (first-field layout).
			UnsafeGenerationalStableIndexList<int>.Destroy(listPtr);
		}

		// One case per CollectionUtils.BitwiseEquals branch: 1, 2, 4, 8, 16 bytes and the MemCmp fallback (12 bytes).
		[Test]
		public void Set_ChangeDetection_WorksForEveryValueSize()
		{
			AssertSetChangeDetection<byte>(1, 2);
			AssertSetChangeDetection<short>(1, 2);
			AssertSetChangeDetection<float>(1f, 2f);
			AssertSetChangeDetection<long>(1L, 1L << 40);
			AssertSetChangeDetection(new Pair64 { A = 1, B = 2 }, new Pair64 { A = 1, B = 3 });
			AssertSetChangeDetection(new Triple32 { A = 1, B = 2, C = 3 }, new Triple32 { A = 1, B = 2, C = 4 });
		}

		private static void AssertSetChangeDetection<TValue>(TValue initial, TValue changed) where TValue : unmanaged
		{
			var list = new UnsafeGenerationalStableIndexList<TValue>(4, Allocator.Temp);
			try
			{
				var handle = list.Add(initial);
				var before = list.Generation;

				list.Set(handle, initial);
				Assert.AreEqual(before, list.Generation, typeof(TValue).Name);

				list.Set(handle, changed);
				Assert.AreEqual(before + 1U, list.Generation, typeof(TValue).Name);
				Assert.AreEqual(changed, list[handle], typeof(TValue).Name);
			}
			finally
			{
				list.Dispose();
			}
		}

		private struct Pair64
		{
			public long A;
			public long B;
		}

		private struct Triple32
		{
			public int A;
			public int B;
			public int C;
		}

		private static UnsafeGenerationalStableIndexList<int> CreateList()
		{
			return new UnsafeGenerationalStableIndexList<int>(4, Allocator.Temp);
		}
	}
}
