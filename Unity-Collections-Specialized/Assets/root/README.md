<div align="center">
    <h1>Unity Collections Specialized</h1>

[![OpenUPM](https://img.shields.io/npm/v/com.saesentsessis.unity-collections-specialized?label=OpenUPM&registry_uri=https://package.openupm.com&labelColor=333A41 'OpenUPM package')](https://openupm.com/packages/com.saesentsessis.unity-collections-specialized/)
[![Unity Editor](https://img.shields.io/badge/Editor-X?style=flat&logo=unity&labelColor=333A41&color=2A2A2A 'Unity Editor supported')](https://unity.com/releases/editor/archive)
[![Unity Runtime](https://img.shields.io/badge/Runtime-X?style=flat&logo=unity&labelColor=333A41&color=2A2A2A 'Unity Runtime supported')](https://unity.com/releases/editor/archive)
[![Tests Passed](https://github.com/Saesentsessis/Unity-Collections-Specialized/actions/workflows/release.yml/badge.svg 'Tests Passed')](https://github.com/Saesentsessis/Unity-Collections-Specialized/actions/workflows/release.yml)<br/>
[![Releases](https://img.shields.io/github/release/Saesentsessis/Unity-Collections-Specialized.svg)](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases)
[![Stars](https://img.shields.io/github/stars/Saesentsessis/Unity-Collections-Specialized 'Stars')](https://github.com/Saesentsessis/Unity-Collections-Specialized/stargazers)
[![License](https://img.shields.io/github/license/Saesentsessis/Unity-Collections-Specialized?label=License&labelColor=333A41)](https://github.com/Saesentsessis/Unity-Collections-Specialized/blob/main/LICENSE)

</div>

_A custom-made, third-party package for High-Performance C# (HPC#). Not affiliated with or endorsed by Unity Technologies._

**Unity Collections Specialized** extends the standard `Unity.Collections` library
with high-performance data structures featuring **stable index handles**. It allows
you to add and remove elements from dense native arrays without invalidating unrelated
handles, preventing memory fragmentation while maintaining contiguous memory for fast
iteration.

## Core Design & Architecture

### Generation-Safe Stable Handles

Instead of standard integer indices, `Add` operations return a `StableIndexHandle`
consisting of a stable ID and a generation version. When an element is removed and its
slot is later reused, the version increments. This guarantees that old, stale handles
cannot accidentally alias newly inserted data.

### Swap-Back Compaction

To maintain contiguous memory for cache-friendly linear iteration, removals utilize
swap-back compaction. When an element is removed, the last element in the collection
is moved into the vacated slot. While the internal dense index order changes, your
external `StableIndexHandle` references remain perfectly intact and accurate.

## Zero-Overhead Access Philosophy

**Architectural Note:** callers are strictly responsible for validating handles prior
to data access using `IsValid(handle)` method.

By deliberately omitting mandatory bounds and version-checking during the actual
`list[handle]` retrieval, the API guarantees **zero-overhead access** in performance
critical sections, where the caller can already assure the handle validity.

Native and managed `Remove` still validate handles when
`ENABLE_UNITY_COLLECTIONS_CHECKS` is enabled. Unsafe `Remove` does not.

### Length and Resize growth

On `UnsafeStableIndexList<T>`, growing via `Length` or `Resize` mints stable-index
metadata and sparse IDs for each new dense slot (same rules as `Add`). Shrinking
truncates the dense length; handles into truncated slots fail `IsValid` via bounds.
Prefer `Add` / `Remove` for normal handle-oriented lifecycle; use `Length` /
`Resize` when you need bulk dense sizing with handle-consistent slots.

## Key types

| Type                       | Description                                                           |
|----------------------------|-----------------------------------------------------------------------|
| `StableIndexList<T>`       | Managed list with stable handles and swap-back removal                |
| `NativeStableIndexList<T>` | Native container with safety handles and job-safe disposal            |
| `UnsafeStableIndexList<T>` | Unmanaged pointer-based list for Burst and low-level code             |
| `StableIndexHandle`        | Generation-safe handle; stale after the referenced element is removed |

Managed and native/unsafe variants expose `Length` (element count) and `Capacity`
(allocated slots). Prefer `Length` for sizing checks.

## Migrating from 0.1.x

| Change | Action |
|--------|--------|
| `StableIndexList<T>.Count` | Rename usages to `.Length` |
| Optional managed parity | `IsCreated`, `IsEmpty`, dense `this[int]`, `Clear()`, `SetCapacity`, `TrimExcess` are available |

## Usage Examples

### Safe Native Usage

Ideal for integration into the Unity Job System with standard safety checks.

```csharp
using UnityEngine;
using Unity.Collections;
using Unity.Collections.Specialized;

// Allocate the safe native container.
var list = new NativeStableIndexList<int>(Allocator.Temp);

// Add an element and store its stable handle.
var handle = list.Add(42);

// Validate and access.
if (list.IsValid(handle))
    Debug.Log(list[handle]); // 42

Debug.Log(list.Length); // 1

// Don't forget to release unmanaged memory.
list.Dispose();
```

### Managed Usage

If you need the stable-index paradigm outside of native memory applications.

```csharp
using Unity.Collections;
using Unity.Collections.Specialized;

var list = new StableIndexList<string>(Allocator.Temp);
var handle = list.Add("42");

list.Remove(handle);

// Will safely evaluate to false because the handle is now stale
if (list.IsValid(handle))
{
    // ...
}

list.Clear(); // resets Length and ID pool
Debug.Log(list.Length); // 0

// Don't forget to release unmanaged memory.
list.Dispose();
```

## Requirements

- Unity **2021.3** or newer
- [`com.unity.collections`](https://docs.unity3d.com/Packages/com.unity.collections@latest) **2.1.4** or newer
- [`com.unity.burst`](https://docs.unity3d.com/Packages/com.unity.burst@latest) **1.8.0** or newer

## Installation

### Method 1: OpenUPM (Recommended)

You can install this package via the [OpenUPM](https://openupm.com/) CLI:

```bash
openupm add com.saesentsessis.unity-collections-specialized
```

Or manually add the scoped registry to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.saesentsessis.unity-collections-specialized": "0.2.0"
  },
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.saesentsessis"
      ]
    }
  ]
}
```

### Method 2: Unity package installer

1. Download the latest `.unitypackage` from [GitHub Releases page](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases).
   - _Direct Link:_ [Unity-Collections-Specialized-Installer.unitypackage](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases/download/0.2.0/Unity-Collections-Specialized-Installer.unitypackage)
2. Import the downloaded package into your Unity project.
3. The installer will automatically configure OpenUPM in your `manifest.json` file and install the package dependencies.

### Method 3: Manual installation

1. Open Unity and navigate to `Window` -> `Package Manager`.
2. Click on the `+` icon in the top left corner and select `Add package from git URL...`.
3. Enter the following URL:
```txt
https://github.com/Saesentsessis/Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root
```
4. Click Add.

You can specify exact release version of this package like this:

```txt
https://github.com/Saesentsessis/Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root#0.2.0
```

## License

Licensed under [MIT License](./LICENSE).
