using System;
using NUnit.Framework;
using Unity.Jobs;

namespace Unity.Collections.Specialized.Tests
{
	/// <summary>
	/// Slot semantics are covered by <see cref="UnsafeSlotAllocatorTests"/>; these tests cover the safety-checked wrapper.
	/// </summary>
	public class NativeSlotAllocatorTests
	{
		[Test]
		public void AllocateReleaseIsValid_Forward()
		{
			var slots = new NativeSlotAllocator(Allocator.Temp);
			try
			{
				Assert.IsTrue(slots.IsCreated);
				Assert.IsTrue(slots.IsEmpty);

				var a = slots.Allocate(out var va);
				var b = slots.Allocate(out var vb);

				Assert.AreEqual(2, slots.Length);
				Assert.GreaterOrEqual(slots.Capacity, 2);
				Assert.IsTrue(slots.IsValid(a, va));

				Assert.IsTrue(slots.Release(a, va, out var removed, out var moved));
				Assert.AreEqual(0, removed);
				Assert.AreEqual(1, moved);
				Assert.IsFalse(slots.IsValid(a, va));
				Assert.IsTrue(slots.IsValid(b, vb));
				Assert.IsFalse(slots.Release(a, va, out _, out _));
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Clear_KeepsVersions()
		{
			var slots = new NativeSlotAllocator(Allocator.Temp);
			try
			{
				var id = slots.Allocate(out var version);

				slots.Clear();

				Assert.IsTrue(slots.IsEmpty);
				Assert.IsFalse(slots.IsValid(id, version));
				slots.Allocate(out var reusedVersion);
				Assert.AreNotEqual(version, reusedVersion);
			}
			finally
			{
				slots.Dispose();
			}
		}

		[Test]
		public void Equals_ComparesBackingPointer()
		{
			var slots = new NativeSlotAllocator(Allocator.Temp);
			var other = new NativeSlotAllocator(Allocator.Temp);
			try
			{
				var copy = slots;

				Assert.IsTrue(slots.Equals(copy));
				Assert.IsTrue(slots.Equals((object)copy));
				Assert.AreEqual(slots.GetHashCode(), copy.GetHashCode());
				Assert.IsFalse(slots.Equals(other));
			}
			finally
			{
				slots.Dispose();
				other.Dispose();
			}
		}

		[Test]
		public void Dispose_SetsIsCreatedFalse_AndDefaultInstanceDoesNotThrow()
		{
			var slots = new NativeSlotAllocator(Allocator.Temp);
			slots.Dispose();
			Assert.IsFalse(slots.IsCreated);
			Assert.IsTrue(slots.IsEmpty);

			var empty = default(NativeSlotAllocator);
			Assert.DoesNotThrow(() => empty.Dispose());
			Assert.AreEqual(default(JobHandle), empty.Dispose(default));
		}

		[Test]
		public void DisposeJob_CompletesAndReleases()
		{
			var slots = new NativeSlotAllocator(Allocator.TempJob);
			slots.Allocate(out _);

			slots.Dispose(default).Complete();

			Assert.IsFalse(slots.IsCreated);
		}

		[Test]
		public void Job_AllocatesThroughField()
		{
			var slots = new NativeSlotAllocator(Allocator.TempJob);
			try
			{
				new AllocateJob { Slots = slots, Count = 3 }.Schedule().Complete();

				Assert.AreEqual(3, slots.Length);
			}
			finally
			{
				slots.Dispose();
			}
		}

		private struct AllocateJob : IJob
		{
			public NativeSlotAllocator Slots;
			public int Count;

			public void Execute()
			{
				for (var i = 0; i < Count; i++)
					Slots.Allocate(out _);
			}
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		[Test]
		public void DisposedCopy_ThrowsOnAccess()
		{
			var slots = new NativeSlotAllocator(Allocator.Persistent);
			var copy = slots;

			slots.Dispose();

			Assert.Catch<InvalidOperationException>(() => { var _ = copy.Length; });
		}

		[Test]
		public void Constructor_InvalidAllocator_Throws()
		{
			Assert.Throws<ArgumentException>(() => new NativeSlotAllocator(Allocator.None));
		}
#endif
	}
}
