using System;
using NUnit.Framework;

namespace Unity.Collections.Specialized.Tests
{
	public class UnsafeSlotAllocatorTests
	{
		[Test]
		public void Constructor_RoundsCapacityUpAndStartsEmpty()
		{
			var slots = new UnsafeSlotAllocator(1, Allocator.Temp);
			try
			{
				Assert.IsTrue(slots.IsCreated);
				Assert.IsTrue(slots.IsEmpty);
				Assert.AreEqual(0, slots.Length);
				Assert.GreaterOrEqual(slots.Capacity, 1);
				Assert.AreEqual(Allocator.Temp, slots.Allocator.ToAllocator);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Allocate_ReturnsDistinctValidIds()
		{
			var slots = CreateSlots();
			try
			{
				var a = slots.Allocate(out var va);
				var b = slots.Allocate(out var vb);

				Assert.AreNotEqual(a, b);
				Assert.IsTrue(slots.IsValid(a, va));
				Assert.IsTrue(slots.IsValid(b, vb));
				Assert.AreEqual(2, slots.Length);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Allocate_BeyondCapacity_GrowsAndKeepsIdsValid()
		{
			var slots = CreateSlots();
			try
			{
				var count = slots.Capacity * 2 + 1;
				var ids = new int[count];
				var versions = new int[count];

				for (var i = 0; i < count; i++)
					ids[i] = slots.Allocate(out versions[i]);

				Assert.GreaterOrEqual(slots.Capacity, count);
				for (var i = 0; i < count; i++)
					Assert.IsTrue(slots.IsValid(ids[i], versions[i]));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public unsafe void Release_Middle_ReportsSwapBackMove()
		{
			var slots = CreateSlots();
			try
			{
				var a = slots.Allocate(out var va);
				var b = slots.Allocate(out var vb);
				var c = slots.Allocate(out var vc);

				Assert.IsTrue(slots.Release(a, va, out var removedDense, out var movedFromDense));

				Assert.AreEqual(0, removedDense);
				Assert.AreEqual(2, movedFromDense);
				Assert.AreEqual(2, slots.Length);
				Assert.IsFalse(slots.IsValid(a, va));
				Assert.IsTrue(slots.IsValid(b, vb));
				Assert.IsTrue(slots.IsValid(c, vc));
				Assert.AreEqual(0, slots.IndicesPtr[c]);
				Assert.AreEqual(c, slots.MetaPtr[0].ReverseId);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Release_Last_ReportsNoMove()
		{
			var slots = CreateSlots();
			try
			{
				slots.Allocate(out _);
				var last = slots.Allocate(out var version);

				Assert.IsTrue(slots.Release(last, version, out var removedDense, out var movedFromDense));
				Assert.AreEqual(removedDense, movedFromDense);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Release_StaleOrOutOfRange_ReturnsFalseAndChangesNothing()
		{
			var slots = CreateSlots();
			try
			{
				var id = slots.Allocate(out var version);
				var other = slots.Allocate(out var otherVersion);
				slots.Release(id, version, out _, out _);

				Assert.IsFalse(slots.Release(id, version, out var removed, out var moved));
				Assert.AreEqual(-1, removed);
				Assert.AreEqual(-1, moved);
				Assert.IsFalse(slots.Release(-1, 1, out _, out _));
				Assert.IsFalse(slots.Release(slots.Capacity, 1, out _, out _));
				Assert.AreEqual(1, slots.Length);
				Assert.IsTrue(slots.IsValid(other, otherVersion));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Allocate_AfterRelease_ReusesIdWithNewVersion()
		{
			var slots = CreateSlots();
			try
			{
				var id = slots.Allocate(out var version);
				slots.Release(id, version, out _, out _);

				var reused = slots.Allocate(out var reusedVersion);

				Assert.AreEqual(id, reused);
				Assert.AreNotEqual(version, reusedVersion);
				Assert.IsFalse(slots.IsValid(id, version));
				Assert.IsTrue(slots.IsValid(reused, reusedVersion));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Clear_KeepsVersions_SoOldIdsNeverRevalidate()
		{
			var slots = CreateSlots();
			try
			{
				var id = slots.Allocate(out var version);

				slots.Clear();

				Assert.AreEqual(0, slots.Length);
				Assert.IsFalse(slots.IsValid(id, version));

				var reused = slots.Allocate(out var reusedVersion);
				Assert.AreEqual(id, reused);
				Assert.AreNotEqual(version, reusedVersion);
				Assert.IsFalse(slots.IsValid(id, version));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public unsafe void Resize_Grow_MintsValidSlots_ShrinkInvalidatesTail()
		{
			var slots = CreateSlots();
			try
			{
				slots.Resize(5);

				Assert.AreEqual(5, slots.Length);
				var lastId = slots.MetaPtr[4].ReverseId;
				var lastVersion = slots.MetaPtr[4].Version;
				for (var dense = 0; dense < 5; dense++)
					Assert.IsTrue(slots.IsValid(slots.MetaPtr[dense].ReverseId, slots.MetaPtr[dense].Version));

				slots.Length = 2;

				Assert.AreEqual(2, slots.Length);
				Assert.IsFalse(slots.IsValid(lastId, lastVersion));
				Assert.Throws<ArgumentOutOfRangeException>(() => slots.Resize(-1));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void SetCapacity_IsGrowOnly()
		{
			var slots = CreateSlots();
			try
			{
				slots.SetCapacity(100);
				var grown = slots.Capacity;
				Assert.GreaterOrEqual(grown, 100);

				slots.SetCapacity(1);
				slots.Capacity = 2;

				Assert.AreEqual(grown, slots.Capacity);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void TrimExcess_ShrinksToHighestLiveId_AndKeepsLiveIdsValid()
		{
			var slots = CreateSlots();
			try
			{
				slots.SetCapacity(64);
				var ids = new int[6];
				var versions = new int[6];
				for (var i = 0; i < ids.Length; i++)
					ids[i] = slots.Allocate(out versions[i]);

				// Keep ids[1] and ids[3]; highest live id is ids[3].
				slots.Release(ids[5], versions[5], out _, out _);
				slots.Release(ids[4], versions[4], out _, out _);
				slots.Release(ids[0], versions[0], out _, out _);
				slots.Release(ids[2], versions[2], out _, out _);

				slots.TrimExcess();

				Assert.AreEqual(Math.Max(ids[1], ids[3]) + 1, slots.Capacity);
				Assert.IsTrue(slots.IsValid(ids[1], versions[1]));
				Assert.IsTrue(slots.IsValid(ids[3], versions[3]));

				// Freed IDs that still fit are re-used without colliding with live ones.
				var fresh = slots.Allocate(out var freshVersion);
				Assert.AreNotEqual(ids[1], fresh);
				Assert.AreNotEqual(ids[3], fresh);
				Assert.IsTrue(slots.IsValid(fresh, freshVersion));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void TrimExcess_OnEmpty_ReleasesStorage()
		{
			var slots = CreateSlots();
			try
			{
				slots.TrimExcess();

				Assert.AreEqual(0, slots.Capacity);
				Assert.IsFalse(slots.IsCreated);

				var id = slots.Allocate(out var version);
				Assert.IsTrue(slots.IsCreated);
				Assert.IsTrue(slots.IsValid(id, version));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Dispose_IsIdempotent()
		{
			var slots = CreateSlots();
			slots.Allocate(out _);

			slots.Dispose();

			Assert.IsFalse(slots.IsCreated);
			Assert.AreEqual(0, slots.Length);
			Assert.DoesNotThrow(() => slots.Dispose());
		}

		[Test]
		public void DisposeJob_CompletesAndReleasesStorage()
		{
			var slots = new UnsafeSlotAllocator(4, Allocator.TempJob);
			slots.Allocate(out _);

			slots.Dispose(default).Complete();

			Assert.IsFalse(slots.IsCreated);
		}

		[Test]
		public unsafe void Destroy_FreesHeapAllocation()
		{
			var ptr = AllocatorManager.Allocate<UnsafeSlotAllocator>(Allocator.Persistent);
			*ptr = new UnsafeSlotAllocator(4, Allocator.Persistent);
			ptr->Allocate(out _);

			UnsafeSlotAllocator.Destroy(ptr);
		}

		private static UnsafeSlotAllocator CreateSlots()
		{
			return new UnsafeSlotAllocator(4, Allocator.Temp);
		}
	}
}
