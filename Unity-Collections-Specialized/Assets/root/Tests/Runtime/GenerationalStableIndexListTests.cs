using System;
using System.Reflection;
using NUnit.Framework;

namespace Unity.Collections.Specialized.Tests
{
	/// <summary>
	/// Storage / handle behaviour is the wrapped <see cref="StableIndexList{T}"/>'s (see <see cref="StableIndexListTests"/>).
	/// These tests cover the forwarding surface and the generation rules.
	/// </summary>
	public class GenerationalStableIndexListTests
	{
		[Test]
		public void EmptyList_IsEmptyCreatedAndAtGenerationZero()
		{
			using var list = CreateList();

			Assert.IsTrue(list.IsCreated);
			Assert.IsTrue(list.IsEmpty);
			Assert.AreEqual(0, list.Length);
			Assert.GreaterOrEqual(list.Capacity, 1);
			Assert.AreEqual(0U, list.Generation);
		}

		[Test]
		public void Add_ValueReadableEverywhere_AndBumpsGeneration()
		{
			using var list = CreateList();

			var a = list.Add(10);
			var b = list.Add(20);

			Assert.AreEqual(2U, list.Generation);
			Assert.AreEqual(2, list.Length);
			Assert.IsTrue(list.IsValid(a));
			Assert.AreEqual(10, list[a]);
			Assert.AreEqual(20, list[b]);
			Assert.AreEqual(10, list[0]);
			Assert.AreEqual(new[] { 10, 20 }, list.Data.ToArray());
		}

		[Test]
		public void Add_BeyondCapacity_BumpsOncePerAdd()
		{
			using var list = new GenerationalStableIndexList<int>(1, Allocator.Temp);
			var count = list.Capacity * 3;

			for (var i = 0; i < count; i++)
				list.Add(i);

			Assert.AreEqual((uint)count, list.Generation);
		}

		[Test]
		public void Remove_SwapBackPreservesSurvivors_AndBumpsGeneration()
		{
			using var list = CreateList();

			var a = list.Add(10);
			var b = list.Add(20);
			var before = list.Generation;

			list.Remove(a);

			Assert.AreEqual(before + 1U, list.Generation);
			Assert.IsFalse(list.IsValid(a));
			Assert.AreEqual(20, list[b]);
		}

		[Test]
		public void Remove_InvalidHandle_ThrowsAndDoesNotBump()
		{
			using var list = CreateList();

			var handle = list.Add(1);
			list.Remove(handle);
			var before = list.Generation;

			Assert.Throws<ArgumentException>(() => list.Remove(handle));
			Assert.AreEqual(before, list.Generation);
		}

		[Test]
		public void TryRemove_StaleHandle_ReturnsFalseAndDoesNotBump()
		{
			using var list = CreateList();

			var handle = list.Add(1);
			Assert.IsTrue(list.TryRemove(handle));
			var before = list.Generation;

			Assert.IsFalse(list.TryRemove(handle));
			Assert.IsFalse(list.TryRemove(StableIndexHandle.Null));
			Assert.AreEqual(before, list.Generation);
		}

		[Test]
		public void Clear_BumpsGeneration_InsteadOfResetting_AndKeepsHandlesInvalid()
		{
			using var list = CreateList();

			var first = list.Add(1);
			var before = list.Generation;

			list.Clear();

			Assert.AreEqual(before + 1U, list.Generation);
			Assert.IsTrue(list.IsEmpty);

			var reused = list.Add(2);
			Assert.IsFalse(list.IsValid(first));
			Assert.IsTrue(list.IsValid(reused));
		}

		[Test]
		public void CapacityChanges_DoNotBumpGeneration()
		{
			using var list = CreateList();

			var handle = list.Add(7);
			var before = list.Generation;

			list.SetCapacity(64);
			Assert.GreaterOrEqual(list.Capacity, 64);
			list.TrimExcess();

			Assert.AreEqual(before, list.Generation);
			Assert.IsTrue(list.IsValid(handle));
			Assert.AreEqual(7, list[handle]);
		}

		[Test]
		public void HandleSetter_BumpsOnlyWhenValueChanges()
		{
			using var list = CreateList();

			var handle = list.Add(5);
			var before = list.Generation;

			list[handle] = 5;
			Assert.AreEqual(before, list.Generation);

			list[handle] = 6;
			Assert.AreEqual(before + 1U, list.Generation);
			Assert.AreEqual(6, list[handle]);
		}

		[Test]
		public void DenseSetter_BumpsOnlyWhenValueChanges()
		{
			using var list = CreateList();

			list.Add(5);
			var before = list.Generation;

			list[0] = 5;
			Assert.AreEqual(before, list.Generation);

			list[0] = 9;
			Assert.AreEqual(before + 1U, list.Generation);
		}

		[Test]
		public void Setter_ReferenceType_UsesEqualityComparer()
		{
			using var list = new GenerationalStableIndexList<string>(Allocator.Temp);

			var handle = list.Add("a");
			var before = list.Generation;

			// Equal but not the same instance: no change.
			list[handle] = new string('a', 1);
			Assert.AreEqual(before, list.Generation);

			list[handle] = null;
			Assert.AreEqual(before + 1U, list.Generation);
			Assert.IsNull(list[handle]);
		}

		[Test]
		public void Generation_WrapsAroundUnchecked()
		{
			using var list = CreateList();

			typeof(GenerationalStableIndexList<int>)
				.GetField("_generation", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(list, uint.MaxValue);

			list.Add(1);

			Assert.AreEqual(0U, list.Generation);
		}

		[Test]
		public void Dispose_ClearsIsCreatedResetsGenerationAndAllowsSecondCall()
		{
			var list = CreateList();
			list.Add(1);

			list.Dispose();

			Assert.IsFalse(list.IsCreated);
			Assert.AreEqual(0U, list.Generation);
			Assert.DoesNotThrow(list.Dispose);
		}

		private static GenerationalStableIndexList<int> CreateList()
		{
			return new GenerationalStableIndexList<int>(Allocator.Temp);
		}
	}
}
