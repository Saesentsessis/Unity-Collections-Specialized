/*
┌────────────────────────────────────────────────────────────────────────────┐
│  Unity Collections Specialized                                             │
│  Custom-made third-party package — not affiliated with or endorsed by      │
│  Unity Technologies.                                                       │
│  Repository: https://github.com/Saesentsessis/Unity-Collections-Specialized│
└────────────────────────────────────────────────────────────────────────────┘
*/

using System.Runtime.InteropServices;

namespace Unity.Collections.Specialized
{
    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
    public struct StableIndexMetadata
    {
        public int ReverseId;   // Reverse ID: Which ID owns the data at this dense index?
        public int Version;     // Generation count to detect stale handles
    }
}