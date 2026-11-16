# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-11-16

### Added

- `GenerationalStableIndexList<T>`, `UnsafeGenerationalStableIndexList<T>` and `NativeGenerationalStableIndexList<T>` — stable-index lists with a `uint Generation` counter (unchecked wrap).
  - Implemented as thin wrappers over the plain lists of the same tier, so all storage / handle logic is shared.
  - Generation bumps on `Add`, a successful `Remove` / `TryRemove`, `Clear` (bumps instead of resetting), `Resize` / `Length` set, and on value writes (`Set`, `SetAt`, setters) only when the value changes. Growth, `SetCapacity` and `TrimExcess` never bump.
  - `UnsafeGenerationalStableIndexList<T>` keeps untracked `ref` accessors plus `BumpGeneration()`; `NativeGenerationalStableIndexList<T>` is read-only (by-value indexers, `AsReadOnly()`, `AsReadOnlySpan()`), with writes through `Set` / `SetAt`.
- `NativeSlotAllocator` and `UnsafeSlotAllocator` - a standalone stable index slot allocator. `NativeSlotAllocator` exposes `IsValid` and `IsEmpty`.
- `TryRemove` on `UnsafeStableIndexList<T>` and `StableIndexList<T>` — removes by handle and returns whether anything was removed.
- `NativeStableIndexList<T>.IsEmpty`; the dense indexer now range-checks under `ENABLE_UNITY_COLLECTIONS_CHECKS`.

### Changed

- **Breaking:** minimum supported Unity version raised from 2021.3 to **2022.3** (the lowest version the package is tested on, with `com.unity.collections` 2.1.4).
- **Breaking:** `UnsafeStableIndexList<T>` and `StableIndexList<T>` now delegate handle / ID bookkeeping to `UnsafeSlotAllocator`.
  - `Clear()` keeps slot versions, so handles taken before `Clear` can no longer become valid again.
  - `SetCapacity` is grow-only; `TrimExcess` shrinks to the smallest ID space that still covers every live ID.
  - Unsafe `Remove` now validates the handle and ignores invalid or stale handles (Native / managed `Remove` still throw under `ENABLE_UNITY_COLLECTIONS_CHECKS`).
  - IDs handed out by `Length` / `Resize` growth are re-used pre-minted IDs and are no longer equal to dense indices.
  - `UnsafeStableIndexList<T>` public fields `m_length`, `m_capacity`, `m_idCapacity` and `Allocator` are replaced by the `Length`, `Capacity` and `Allocator` properties.
  - **Migration:** read `Length` / `Capacity` instead of `m_length` / `m_capacity`; don't rely on handle versions restarting at 1 after `Clear`.

### Fixed

- `UnsafeStableIndexList<T>`, `NativeStableIndexList<T>` and `StableIndexList<T>` now clears it's internal variables on deferred `Destroy(JobHandle)` call.
- `NativeStableIndexList<T>` now inherits from `INativeDisposable` instead of `IDisposable`.
- `StableIndexList<T>.TrimExcess` / `SetCapacity` no longer overflow the metadata buffers when more IDs exist than the new capacity.
- `UnsafeStableIndexList<T>.TrimExcess` / `SetCapacity` no longer lose live elements whose ID is at or above the new capacity.
- `NativeStableIndexList<T>` and `NativeSlotAllocator` can now be used in jobs: the list pointer is marked `[NativeDisableUnsafePtrRestriction]` and the `DisposeSentinel` `[NativeSetClassTypeToNullOnSchedule]` (scheduling previously threw).
- `NativeSlotAllocator.GetHashCode` no longer dereferences the allocator header.

## [0.2.0] - 2026-07-15

### Added

- `StableIndexList<T>.Clear()` — resets length and ID pool; clears managed `T[]` for GC without zeroing unmanaged buffers.
- Managed API parity: `IsCreated`, `IsEmpty`, dense `this[int]`, `SetCapacity`, `TrimExcess`.
- `CollectionUtils.CheckHandleValid` — debug-only handle validation under `ENABLE_UNITY_COLLECTIONS_CHECKS`.
- Growing `UnsafeStableIndexList` via `Length` / `Resize` now mints stable IDs and metadata for each new dense slot (same recycle/mint rules as `Add`).

### Changed

- **Breaking:** `StableIndexList<T>.Count` renamed to `Length` to match Native/Unsafe and `INativeList<T>`.
  - **Migration:** replace `list.Count` with `list.Length`.
- `Clear()` on Unsafe/Native resets both `m_length` and `m_idCapacity` (invalidates all prior handles) without MemClear.
- Native handle indexer uses `CheckWriteAndThrow` without bumping the secondary safety version (keeps `AsArray()` secondary views valid across handle `ref` access).
- Native/managed `Remove` validates handles in development builds (`ENABLE_UNITY_COLLECTIONS_CHECKS`); Unsafe `Remove` stays unchecked (zero-overhead).
- `NativeStableIndexList` now creates a `DisposeSentinel` and aligns `UntypedNativeStableList` layout with `UnsafeStableIndexList`.
- Dispose job helpers renamed to `NativeStableIndexListDispose` / `NativeStableIndexListDisposeJob` and `UnsafeStableIndexListDisposeJob`.

### Fixed

- SoA grow path in `UnsafeStableIndexList.ResizeExact` — meta and indices regions are copied separately (capacity-dependent indices offset).
- Managed `StableIndexList.Reallocate` now allocates/frees with the instance `_allocator` (no longer hardcodes `Allocator.Persistent`).
- `Resize(..., ClearMemory)` grows correctly; clears newly added `DataPtr` only (does not wipe recycled metadata).

### Removed

- ASCII provenance banners from Runtime sources (covered by LICENSE / README).

## [0.1.1] - 2026-07-02

### Added

- `CollectionUtils.AssumePositive(long)` overload — tells Burst that a `long` value is always non-negative (`[AssumeRange(0L, long.MaxValue)]`), matching the existing `int` overload.

### Changed

- **Installer:** replaced `Application.isBatchMode` auto-run guard with `IVAN_MURZAK_INSTALLER_PROJECT` preprocessor directive, so the installer now runs correctly in CI batch mode while still being suppressed when building the Installer project itself.
- **Installer:** flattened `SimpleJSON` into the parent `Unity.Collections.Specialized.Installer` namespace, removing unused `using` directives (`System.Linq`, `SimpleJSON` namespace).
- **Installer:** fixed test file paths in `ManifestInstallerTests` (was pointing at `Unity SoA Generator Installer`, now correctly references `Unity Collections Specialized Installer`).
- **Installer:** removed redundant test fixture JSON files and simplified `correct_manifest.json` to only validate dependency injection (scoped registries section removed from fixtures).
- **Installer:** removed unused `com.unity.multiplayer.center` dependency from the Installer project manifest.
- **Installer:** bumped installer version constant from `0.1.0` to `0.1.1`.

## [0.1.0] - 2026-07-01

### Added

- Initial preview release of Unity Collections Specialized.
- `StableIndexList<T>` managed collection with stable index handles and swap-back removal.
- `NativeStableIndexList<T>` and `UnsafeStableIndexList<T>` native/Burst-compatible variants.
- `StableIndexHandle` for generation-safe references into collections.
- Dependencies on `com.unity.collections` 2.0.0 and `com.unity.burst` 1.8.0.

[0.3.0]: https://github.com/Saesentsessis/Unity-Collections-Specialized/compare/0.2.0...0.3.0
[0.2.0]: https://github.com/Saesentsessis/Unity-Collections-Specialized/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/Saesentsessis/Unity-Collections-Specialized/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Saesentsessis/Unity-Collections-Specialized/releases/tag/0.1.0
