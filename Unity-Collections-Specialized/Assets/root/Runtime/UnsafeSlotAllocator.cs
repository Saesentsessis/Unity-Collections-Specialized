using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Collections.Specialized
{
	[BurstCompile(DisableSafetyChecks = true)]
	public unsafe struct UnsafeSlotAllocatorDisposeJob : IJob
	{
		[NativeDisableUnsafePtrRestriction]
		public void* MetaAndIndicesPtr;
		
		public AllocatorManager.AllocatorHandle Allocator;
		
		public void Execute()
		{
			AllocatorManager.Free(Allocator, MetaAndIndicesPtr);
		}
	}
	
	[StructLayout(LayoutKind.Sequential)]
	public unsafe struct UnsafeSlotAllocator : INativeDisposable
	{
		public const int MaxCapacity = int.MaxValue;
        
		public static readonly int PackedElementSize = sizeof(StableIndexMetadata) + sizeof(int);
        
		public static void Destroy(UnsafeSlotAllocator* ptr)
		{
			CheckNull(ptr);
			var allocator = ptr->Allocator;
			ptr->Dispose();
			AllocatorManager.Free(allocator, ptr);
		}
		
		/// <summary>
		/// Container for an array of per-index metadata, followed by an array of reverse indices.
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private void* _metaAndIndicesPtr;
        
		/// <summary>
		/// Dense storage for Metadata.
		/// Maps: Dense Index -> Metadata { RID, Validity }.
		/// </summary>
		public StableIndexMetadata* MetaPtr => (StableIndexMetadata*)_metaAndIndicesPtr;
        
		/// <summary>
		/// Sparse storage for Indices.
		/// Maps: ID -> Dense Index.
		/// Offset is capacity * (sizeof(StableIndexMetadata) / sizeof(int)) because
		/// meta and indices share one SoA allocation: [Meta × capacity][Indices × capacity].
		/// </summary>
		public int* IndicesPtr => (int*)_metaAndIndicesPtr + (long)m_capacity * (sizeof(StableIndexMetadata) / sizeof(int));

		/// <summary>
		/// The number of active IDs.
		/// </summary>
		internal int m_length;

		/// <summary>
		/// The number of IDs ever allocated (Active + Free)
		/// </summary>
		internal int m_capacity;

		internal AllocatorManager.AllocatorHandle m_allocator;

		public UnsafeSlotAllocator(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
		{
			_metaAndIndicesPtr = null;
			m_length = 0;
			m_capacity = 0;
			m_allocator = allocator;
            
			SetCapacity(math.max(initialCapacity, 1));
		}
        
		/// <summary>
		/// The number of elements.
		/// </summary>
		/// <value>The number of elements.</value>
		public int Length
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			readonly get => CollectionUtils.AssumePositive(m_length);

			set
			{
				if (value < 0)
					throw new ArgumentOutOfRangeException(nameof(value), "Length must be non-negative.");

				// Shrink: truncate dense length. Handles past the new length fail IsValid via bounds.
				if (value <= m_length)
				{
					m_length = value;
					return;
				}

				// Grow: ensure capacity and mint stable IDs for every new dense slot.
				Resize(value);
			}
		}
        
		/// <summary>
		/// The number of elements that can fit in the internal buffer.
		/// </summary>
		/// <value>The number of elements that can fit in the internal buffer.</value>
		public int Capacity
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			readonly get => CollectionUtils.AssumePositive(m_capacity);
			set => SetCapacity(value);
		}
        
		/// <summary>
		/// Whether the list is empty.
		/// </summary>
		/// <value>True if the list is empty or the list has not been constructed.</value>
		public readonly bool IsEmpty
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => !IsCreated || m_length == 0;
		}

		/// <summary>
		/// Whether this list has been allocated (and not yet deallocated).
		/// </summary>
		/// <value>True if this list has been allocated (and not yet deallocated).</value>
		public readonly bool IsCreated
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _metaAndIndicesPtr != null;
		}

		public readonly AllocatorManager.AllocatorHandle Allocator
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_allocator;
		}
        
		/// <summary>
		/// Resets the length to 0 without zeroing allocated memory.
		/// </summary>
		/// <remarks>
		/// Does not change the capacity. All existing handles become invalid.
		/// </remarks>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Clear()
		{
			m_length = 0;
		}
        
		/// <summary>
		/// Sets the capacity to match the maxLiveId.
		/// </summary>
		public void TrimExcess()
		{
			// The ID space must still cover every live ID.
			var maxLiveId = -1;
			for (var i = 0; i < m_length; i++)
				maxLiveId = math.max(maxLiveId, MetaPtr[i].ReverseId);

			var newCapacity = maxLiveId + 1;
			if (newCapacity >= m_capacity)
				return;

			// Keep only free IDs that fit the smaller ID space, compacted right after the live prefix.
			var metaPtr = MetaPtr;
			var indicesPtr = IndicesPtr;
			var writeIndex = m_length;
			for (var readIndex = m_length; readIndex < m_capacity; readIndex++)
			{
				var m = metaPtr[readIndex];
				
				if (m.ReverseId >= newCapacity)
					continue;

				metaPtr[writeIndex] = m;
				indicesPtr[m.ReverseId] = writeIndex;
				writeIndex++;
			}

			ResizeExact(newCapacity); // write == newCapacity here
		}

		/// <summary>
		/// Sets the length, expanding the capacity if necessary.
		/// Growing mints stable-index metadata and sparse IDs for each new dense slot
		/// so handles remain consistent with <see cref="Allocate"/>.
		/// </summary>
		/// <param name="length">The new length.</param>
		public void Resize(int length)
		{
			if (length < 0)
				throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

			var oldLength = m_length;

			if (length > m_capacity)
				SetCapacity(length);

			if (length > oldLength)
				MintIdsForRange(oldLength, length);

			m_length = length;
		}
        
		/// <summary>
		/// Bumps the version of dense slots [fromInclusive, toExclusive), which were pre-minted by InitFreeIds.
		/// </summary>
		private void MintIdsForRange(int fromInclusive, int toExclusive)
		{
			var metaPtr = MetaPtr;
			for (var denseIndex = fromInclusive; denseIndex < toExclusive; denseIndex++)
				metaPtr[denseIndex].Version++;
		}
        
		/// <summary>
		/// Sets the capacity.
		/// </summary>
		/// <param name="capacity">The new capacity.</param>
		public void SetCapacity(int capacity)
		{
			SetCapacity(ref m_allocator, capacity);
		}
        
		private void SetCapacity<U>(ref U allocator, int capacity) where U : unmanaged, AllocatorManager.IAllocator
		{
			CollectionUtils.CheckCapacityInRange(capacity, MaxCapacity);
            
			var newCapacity = math.max(capacity, CollectionHelper.CacheLineSize / PackedElementSize);
			var ceilPow2 = math.ceilpow2((long)newCapacity);
			newCapacity = (int)math.min(MaxCapacity, ceilPow2);

			// grow-only; use TrimExcess to shrink
			if (newCapacity <= m_capacity)
				return;

			ResizeExact(ref allocator, newCapacity);
		}
        
		private void ResizeExact(int capacity)
		{
			ResizeExact(ref m_allocator, capacity);
		}
        
		private void ResizeExact<U>(ref U allocator, int newCapacity) where U : unmanaged, AllocatorManager.IAllocator
		{
			newCapacity = math.max(0, newCapacity);

			CollectionUtils.CheckAllocator(m_allocator);
			void* newMetaAndIndicesPtr = null;

			if (newCapacity > 0)
			{
				newMetaAndIndicesPtr = AllocatorManager.Allocate(
					allocator.Handle,
					PackedElementSize,
					UnsafeUtility.AlignOf<StableIndexMetadata>(),
					newCapacity
				);

				if (m_capacity > 0)
				{
					var oldCapacity = m_capacity;
					var itemsToCopy = math.min(newCapacity, oldCapacity);

					// SoA layout: [Meta × capacity][Indices × capacity]. Copy each region
					// separately — the indices offset depends on capacity and moves on grow/shrink.
					if (_metaAndIndicesPtr != null)
					{
						var metaIntsPerElement = sizeof(StableIndexMetadata) / sizeof(int);
						var metaBytes = (long)itemsToCopy * sizeof(StableIndexMetadata);
						var indexBytes = (long)itemsToCopy * sizeof(int);

						UnsafeUtility.MemCpy(newMetaAndIndicesPtr, _metaAndIndicesPtr, metaBytes);

						var oldIndices = (int*)_metaAndIndicesPtr + (long)oldCapacity * metaIntsPerElement;
						var newIndices = (int*)newMetaAndIndicesPtr + (long)newCapacity * metaIntsPerElement;
						UnsafeUtility.MemCpy(newIndices, oldIndices, indexBytes);
					}
				}
			}

			if (CollectionUtils.ShouldDeallocate(allocator.Handle))
				AllocatorManager.Free(allocator.Handle, _metaAndIndicesPtr);
            
			var previousCapacity = m_capacity;
			_metaAndIndicesPtr = newMetaAndIndicesPtr;
			m_capacity = newCapacity;
			m_length = math.min(m_length, newCapacity);
			InitFreeIds(previousCapacity, newCapacity);
		}
		
		/// <summary>
		/// Pre-mints every new ID in [from, to) as a free entry: ReverseId == i, IndicesPtr[i] == i, Version == 0.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void InitFreeIds(int from, int to)
		{
			var meta = MetaPtr;
			var indices = IndicesPtr;
			for (var i = from; i < to; i++)
			{
				meta[i] = new StableIndexMetadata { ReverseId = i, Version = 0 };
				indices[i] = i;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsValid(int id, int version)
		{
			if ((uint)id >= (uint)m_capacity)
				return false;
			
			var denseIndex = IndicesPtr[id];
			
			if ((uint)denseIndex >= (uint)m_length)
				return false;
			
			return MetaPtr[denseIndex].Version == version;
		}
        
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Allocate(out int version)
		{
			if (m_length == m_capacity)
			{
				if (m_capacity >= MaxCapacity)
					throw new InvalidOperationException("SlotAllocator is at maximum capacity.");

				SetCapacity(m_capacity == 0 ? 8 : (int)math.min((long)m_capacity * 2, MaxCapacity));
			}

			ref var meta = ref MetaPtr[m_length++];
			version = ++meta.Version;
			return meta.ReverseId;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Release(int id, int version, out int removedDense, out int movedFromDense)
		{
			removedDense = movedFromDense = -1;

			if ((uint)id >= (uint)m_capacity)
				return false;

			var denseIndex = IndicesPtr[id];
			if (denseIndex >= m_length || MetaPtr[denseIndex].Version != version)
				return false;

			var lastDenseIndex = m_length - 1;
			var lastId = MetaPtr[lastDenseIndex].ReverseId;

			MetaPtr[denseIndex].Version++;

			(MetaPtr[denseIndex], MetaPtr[lastDenseIndex]) = (MetaPtr[lastDenseIndex], MetaPtr[denseIndex]);

			IndicesPtr[lastId] = denseIndex;
			IndicesPtr[id] = lastDenseIndex;

			m_length--;
			removedDense = denseIndex;
			movedFromDense = lastDenseIndex; // equals removedDense when the last element was released: nothing moved
			return true;
		}

		/// <summary>
		/// Releases all resources (memory).
		/// </summary>
		public void Dispose()
		{
			if (IsCreated == false)
				return;
            
			if (CollectionUtils.ShouldDeallocate(m_allocator))
			{
				AllocatorManager.Free(m_allocator, _metaAndIndicesPtr);
				m_allocator = AllocatorManager.Invalid;
			}

			_metaAndIndicesPtr = null;
			m_length = 0;
			m_capacity = 0;
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			if (IsCreated == false)
				return inputDeps;

			if (CollectionUtils.ShouldDeallocate(m_allocator))
			{
				var jobHandle = new UnsafeSlotAllocatorDisposeJob
				{
					MetaAndIndicesPtr = _metaAndIndicesPtr,
					Allocator = m_allocator
				}.Schedule(inputDeps);
				
				_metaAndIndicesPtr = null;
				m_length = 0;
				m_capacity = 0;
				m_allocator = AllocatorManager.Invalid;

				return jobHandle;
			}
			
			_metaAndIndicesPtr = null;
			m_length = 0;
			m_capacity = 0;

			return inputDeps;
		}
		
		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		private static void CheckNull(void* listData)
		{
			if (listData == null)
				throw new InvalidOperationException("UnsafeList has yet to be created or has been destroyed!");
		}
	}
}