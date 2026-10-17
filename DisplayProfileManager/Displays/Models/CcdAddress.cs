using System;

namespace DisplayProfileManager.Displays.Models
{
    internal readonly struct CcdTargetKey(AdapterLuid adapterId, uint targetId) : IEquatable<CcdTargetKey>
    {
        public AdapterLuid AdapterId { get; } = adapterId;
        public uint TargetId { get; } = targetId & 0xFFFF;

        public bool Equals(CcdTargetKey other) => CcdAddress.LuidEquals(AdapterId, other.AdapterId) && TargetId == other.TargetId;

        public override bool Equals(object obj) => obj is CcdTargetKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(AdapterId.HighPart, AdapterId.LowPart, TargetId);

        public override string ToString() => $"{CcdAddress.FormatLuid(AdapterId)}:{TargetId}";
    }

    internal readonly struct CcdSourceKey(AdapterLuid adapterId, uint sourceId) : IEquatable<CcdSourceKey>
    {
        public AdapterLuid AdapterId { get; } = adapterId;
        public uint SourceId { get; } = sourceId;

        public bool Equals(CcdSourceKey other) => CcdAddress.LuidEquals(AdapterId, other.AdapterId) && SourceId == other.SourceId;

        public override bool Equals(object obj) => obj is CcdSourceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(AdapterId.HighPart, AdapterId.LowPart, SourceId);

        public override string ToString() => $"{CcdAddress.FormatLuid(AdapterId)}:{SourceId}";
    }

    internal static class CcdAddress
    {
        public static CcdTargetKey Target(AdapterLuid adapterId, uint targetId) => new(adapterId, targetId);

        public static CcdTargetKey Target(DisplayConfigInfo display) => Target(display.AdapterId, display.TargetId);

        public static CcdSourceKey Source(AdapterLuid adapterId, uint sourceId) => new(adapterId, sourceId);

        public static CcdSourceKey Source(DisplayConfigInfo display) => Source(display.AdapterId, display.SourceId);

        public static bool LuidEquals(AdapterLuid left, AdapterLuid right) => left.HighPart == right.HighPart && left.LowPart == right.LowPart;

        public static string FormatLuid(AdapterLuid adapterId) => $"{adapterId.HighPart:X8}{adapterId.LowPart:X8}";
    }
}
