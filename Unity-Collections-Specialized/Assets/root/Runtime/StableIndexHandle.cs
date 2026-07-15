using System;
using Unity.Burst;

namespace Unity.Collections.Specialized
{
    public readonly struct StableIndexHandle : IEquatable<StableIndexHandle>
    {
        public readonly int Index;
        public readonly int Version;

        public StableIndexHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public bool IsValid => Index != -1;
        public static StableIndexHandle Null => new StableIndexHandle(-1, 0);

        public bool Equals(StableIndexHandle other) => Index == other.Index && Version == other.Version;
        [BurstDiscard]
        public override bool Equals(object obj) => obj is StableIndexHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Index, Version);
        public static bool operator ==(StableIndexHandle left, StableIndexHandle right) => left.Equals(right);
        public static bool operator !=(StableIndexHandle left, StableIndexHandle right) => !left.Equals(right);
    }
}