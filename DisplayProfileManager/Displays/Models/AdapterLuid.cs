using System;

namespace DisplayProfileManager.Displays.Models
{
    public struct AdapterLuid(uint lowPart, int highPart) : IEquatable<AdapterLuid>
    {
        public uint LowPart = lowPart;
        public int HighPart = highPart;

        public readonly bool Equals(AdapterLuid other) => LowPart == other.LowPart && HighPart == other.HighPart;
        public override readonly bool Equals(object obj) => obj is AdapterLuid other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);
        public static bool operator ==(AdapterLuid left, AdapterLuid right) => left.Equals(right);
        public static bool operator !=(AdapterLuid left, AdapterLuid right) => !left.Equals(right);
    }
}
