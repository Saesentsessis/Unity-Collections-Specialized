# Unity Collections Specialized

_A custom-made, third-party package for High-Performance C# (HPC#). Not affiliated with or endorsed by Unity Technologies._

**Unity Collections Specialized** extends the standard `Unity.Collections` library
with high-performance data structures featuring **stable index handles**. It allows
you to add and remove elements from dense native arrays without invalidating unrelated
handles, preventing memory fragmentation while maintaining contiguous memory for fast
iteration.

## Core Design & Architecture

### Generation-Safe Stable Handles

Instead of standard integer indices, `Add` operations return a `StableIndexHandle`
consisting of stable ID and generation version. When an element is removed and it's
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

## Key types

| Type                       | Description                                                           |
|----------------------------|-----------------------------------------------------------------------|
| `StableIndexList<T>`       | Managed list with stable handles and swap-back removal                |
| `NativeStableIndexList<T>` | Native container with safety handles and job-safe disposal            |
| `UnsafeStableIndexList<T>` | Unmanaged pointer-based list for Burst and low-level code             |
| `StableIndexHandle`        | Generation-safe handle; stale after the referenced element is removed |

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
    "com.saesentsessis.unity-collections-specialized": "0.1.0"
  },
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.saesentsessis.unity-collections-specialized"
      ]
    }
  ]
}
```

### Method 2: Unity package installer

1. Download the latest `.unitypackage` from [GitHub Releases page](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases).
   - _Direct Link:_ [Unity-Collections-Specialized-Installer.unitypackage](https://github.com/Saesentsessis/Unity-Collections-Specialized/releases/download/0.1.0/Unity-Collections-Specialized-Installer.unitypackage)
2. Import the downloaded package into your Unity project.
3. The installer will automatically configure OpenUPM in your `manifest.json` file and install the package dependencies.

### Method 3: Manual installation

1. Open `Package Manager` window.
2. Click on the `+` icon.
3. Select **Install from git URL...** and paste this link:

```txt
https://github.com/Saesentsessis/Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root
```

You can specify exact release version of this package like this:

```txt
https://github.com/Saesentsessis/Unity-Collections-Specialized.git?path=Unity-Collections-Specialized/Assets/root#0.1.0
```

## License

Licensed under [MIT License](./LICENSE).
