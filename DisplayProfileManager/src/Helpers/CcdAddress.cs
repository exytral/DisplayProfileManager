using System;

namespace DisplayProfileManager.Helpers
{
    internal readonly struct CcdTargetKey : IEquatable<CcdTargetKey>
    {
        public DisplayConfigHelper.LUID AdapterId { get; }
        public uint TargetId { get; }

        public CcdTargetKey(DisplayConfigHelper.LUID adapterId, uint targetId)
        {
            AdapterId = adapterId;
            TargetId = targetId & 0xFFFF;
        }

        public bool Equals(CcdTargetKey other) => CcdAddress.LuidEquals(AdapterId, other.AdapterId) && TargetId == other.TargetId;

        public override bool Equals(object obj) => obj is CcdTargetKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(AdapterId.HighPart, AdapterId.LowPart, TargetId);

        public override string ToString() => $"{CcdAddress.FormatLuid(AdapterId)}:{TargetId}";
    }

    internal readonly struct CcdSourceKey : IEquatable<CcdSourceKey>
    {
        public DisplayConfigHelper.LUID AdapterId { get; }
        public uint SourceId { get; }

        public CcdSourceKey(DisplayConfigHelper.LUID adapterId, uint sourceId)
        {
            AdapterId = adapterId;
            SourceId = sourceId;
        }

        public bool Equals(CcdSourceKey other) => CcdAddress.LuidEquals(AdapterId, other.AdapterId) && SourceId == other.SourceId;

        public override bool Equals(object obj) => obj is CcdSourceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(AdapterId.HighPart, AdapterId.LowPart, SourceId);

        public override string ToString() => $"{CcdAddress.FormatLuid(AdapterId)}:{SourceId}";
    }

    internal static class CcdAddress
    {
        public static CcdTargetKey Target(DisplayConfigHelper.LUID adapterId, uint targetId) => new CcdTargetKey(adapterId, targetId);

        public static CcdTargetKey Target(DisplayConfigHelper.DisplayConfigInfo display) => Target(display.AdapterId, display.TargetId);

        public static CcdSourceKey Source(DisplayConfigHelper.LUID adapterId, uint sourceId) => new CcdSourceKey(adapterId, sourceId);

        public static CcdSourceKey Source(DisplayConfigHelper.DisplayConfigInfo display) => Source(display.AdapterId, display.SourceId);

        public static bool LuidEquals(DisplayConfigHelper.LUID left, DisplayConfigHelper.LUID right) => left.HighPart == right.HighPart && left.LowPart == right.LowPart;

        public static string FormatLuid(DisplayConfigHelper.LUID adapterId) => $"{adapterId.HighPart:X8}{adapterId.LowPart:X8}";
    }
}
