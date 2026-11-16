using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Collections.Specialized
{
	[NativeContainer]
	public unsafe struct NativeSlotAllocatorDispose
	{
		[NativeDisableUnsafePtrRestriction]
		public UnsafeSlotAllocator* Ptr;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		public AtomicSafetyHandle m_Safety;
#endif

		public void Dispose()
		{
			UnsafeSlotAllocator.Destroy(Ptr);
		}
	}

	[BurstCompile]
	public struct NativeSlotAllocatorDisposeJob : IJob
	{
		public NativeSlotAllocatorDispose Data;

		public void Execute()
		{
			Data.Dispose();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	[NativeContainer]
	[DebuggerDisplay("Length = {_ptr == null ? default : _ptr->Length}")]
	public struct NativeSlotAllocator : INativeDisposable, IEquatable<NativeSlotAllocator>
	{
		// Safety is provided by m_Safety; the raw pointer must be allowed inside job structs.
		[NativeDisableUnsafePtrRestriction]
		private unsafe UnsafeSlotAllocator* _ptr;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		// ReSharper disable once InconsistentNaming
		// The AtomicSafetyHandle field must be named exactly 'm_Safety'.
		internal AtomicSafetyHandle m_Safety;

		// ReSharper disable once InconsistentNaming
		// DisposeSentinel is a class; it must be nulled in job copies or the struct cannot be scheduled.
		[NativeSetClassTypeToNullOnSchedule]
		internal DisposeSentinel m_DisposeSentinel;

		// ReSharper disable once InconsistentNaming
		// Statically register this type with the safety system, using a name derived from the type itself
		internal static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeSlotAllocator>();
#endif

		public NativeSlotAllocator(Allocator allocator) : this(8, allocator) { }

		public unsafe NativeSlotAllocator(int capacity, Allocator allocator)
		{
			CollectionUtils.CheckAllocator(allocator);

			_ptr = AllocatorManager.Allocate<UnsafeSlotAllocator>(allocator);
			*_ptr = new UnsafeSlotAllocator(capacity, allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeContainerSafety.Create(out m_Safety, out m_DisposeSentinel, allocator, s_staticSafetyId);
#endif
		}

		public unsafe int Length
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				CheckRead();
				return _ptr->Length;
			}
		}

		public unsafe int Capacity
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				CheckRead();
				return _ptr->Capacity;
			}
		}

		public unsafe bool IsEmpty
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => !IsCreated || _ptr->IsEmpty;
		}

		public unsafe bool IsCreated
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _ptr != null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe int Allocate(out int version)
		{
			CheckWriteAndBumpSecondaryVersion();
			return _ptr->Allocate(out version);
		}

		public unsafe bool Release(int id, int version, out int removedDense, out int movedFromDense)
		{
			CheckWriteAndBumpSecondaryVersion();
			return _ptr->Release(id, version, out removedDense, out movedFromDense);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe bool IsValid(int id, int version)
		{
			CheckRead();
			return _ptr->IsValid(id, version);
		}

		/// <summary>
		/// Resets length to 0 without zeroing allocated memory. Versions are kept, so released IDs stay invalid.
		/// </summary>
		public unsafe void Clear()
		{
			CheckWriteAndBumpSecondaryVersion();
			_ptr->Clear();
		}

		public unsafe bool Equals(NativeSlotAllocator other)
		{
			return _ptr == other._ptr;
		}

		[ExcludeFromBurstCompatTesting("Managed obj comparison is not allowed inside Burst context")]
		public override bool Equals(object obj)
		{
			return obj is NativeSlotAllocator other && Equals(other);
		}

		public override unsafe int GetHashCode() => ((IntPtr)_ptr).GetHashCode();

		/// <summary>
		/// Releases all resources (memory and safety handles).
		/// </summary>
		public unsafe void Dispose()
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeContainerSafety.CheckExists(m_Safety);
#endif
			if (IsCreated == false)
				return;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeContainerSafety.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
			UnsafeSlotAllocator.Destroy(_ptr);
			_ptr = null;
		}

		/// <summary>
		/// Creates and schedules a job that releases all resources (memory and safety handles) of this allocator.
		/// </summary>
		/// <param name="inputDeps">The dependency for the new job.</param>
		/// <returns>The handle of the new job. The job depends upon `inputDeps` and releases all resources (memory and safety handles) of this allocator.</returns>
		public unsafe JobHandle Dispose(JobHandle inputDeps)
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			NativeContainerSafety.CheckExists(m_Safety);
#endif
			if (IsCreated == false)
				return inputDeps;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			// DisposeSentinel must be cleared on the main thread; memory free can run in the job.
			DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
			var jobHandle = new NativeSlotAllocatorDisposeJob
			{
				Data = new NativeSlotAllocatorDispose
				{
					Ptr = _ptr,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
					m_Safety = m_Safety
#endif
				}
			}.Schedule(inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.Release(m_Safety);
#endif
			_ptr = null;

			return jobHandle;
		}

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void CheckRead()
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
		}

		[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private readonly void CheckWriteAndBumpSecondaryVersion()
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
		}
	}
}
