using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.UI.Displays
{
    public sealed class SavedLayoutMember
    {
        internal SavedLayoutMember(string name, bool isEnabled, bool isPrimary, bool isCloneSource)
        {
            Name = name;
            IsEnabled = isEnabled;
            IsPrimary = isPrimary;
            IsCloneSource = isCloneSource;
        }

        public string Name { get; }
        public bool IsEnabled { get; }
        public bool IsPrimary { get; }
        public bool IsCloneSource { get; }
    }

    public sealed class SavedLayoutFootprint
    {
        internal SavedLayoutFootprint(
            int groupIndex, string name, long x, long y, long width, long height, int rotation,
            bool isPrimary, bool isCloneGroup, int cloneMemberCount, bool hasInconsistentCloneGeometry,
            IReadOnlyList<SavedLayoutMember> members)
        {
            GroupIndex = groupIndex;
            Name = name;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Rotation = rotation;
            IsPrimary = isPrimary;
            IsCloneGroup = isCloneGroup;
            CloneMemberCount = cloneMemberCount;
            HasInconsistentCloneGeometry = hasInconsistentCloneGeometry;
            Members = members;
        }

        public int GroupIndex { get; }
        public string Name { get; }
        public long X { get; }
        public long Y { get; }
        public long Width { get; }
        public long Height { get; }
        public int Rotation { get; }
        public bool IsPrimary { get; }
        public bool IsCloneGroup { get; }
        public int CloneMemberCount { get; }
        public bool HasInconsistentCloneGeometry { get; }
        public IReadOnlyList<SavedLayoutMember> Members { get; }
    }

    public sealed class SavedLayoutBounds
    {
        internal SavedLayoutBounds(long x, long y, long width, long height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public long X { get; }
        public long Y { get; }
        public long Width { get; }
        public long Height { get; }
    }

    public sealed class SavedLayoutGeometry
    {
        internal SavedLayoutGeometry(
            IReadOnlyList<SavedLayoutFootprint> footprints, SavedLayoutBounds logicalBounds,
            int enabledDisplayCount, int enabledLayoutCount, int disabledDisplayCount,
            int layoutUnavailableCount, int cloneGroupCount, int inconsistentCloneCount,
            int overlapCount, int primaryDisplayCount)
        {
            Footprints = footprints;
            LogicalBounds = logicalBounds;
            EnabledDisplayCount = enabledDisplayCount;
            EnabledLayoutCount = enabledLayoutCount;
            DisabledDisplayCount = disabledDisplayCount;
            LayoutUnavailableCount = layoutUnavailableCount;
            CloneGroupCount = cloneGroupCount;
            InconsistentCloneCount = inconsistentCloneCount;
            OverlapCount = overlapCount;
            PrimaryDisplayCount = primaryDisplayCount;
        }

        public IReadOnlyList<SavedLayoutFootprint> Footprints { get; }
        public SavedLayoutBounds LogicalBounds { get; }
        public bool HasLogicalBounds => Footprints.Count > 0 && LogicalBounds.Width > 0 && LogicalBounds.Height > 0;
        public int EnabledDisplayCount { get; }
        public int EnabledLayoutCount { get; }
        public int DisabledDisplayCount { get; }
        public int LayoutUnavailableCount { get; }
        public int CloneGroupCount { get; }
        public int InconsistentCloneCount { get; }
        public int OverlapCount { get; }
        public int PrimaryDisplayCount { get; }
        public bool HasPrimaryStateWarning => EnabledDisplayCount > 0 && PrimaryDisplayCount != 1;
    }

    public sealed class ProjectedLayoutFootprint
    {
        internal ProjectedLayoutFootprint(SavedLayoutFootprint source, double x, double y, double width, double height)
        {
            Source = source;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public SavedLayoutFootprint Source { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
    }

    public sealed class SavedLayoutProjection
    {
        internal SavedLayoutProjection(
            SavedLayoutGeometry geometry, IReadOnlyList<ProjectedLayoutFootprint> footprints,
            double scale, double originX, double originY)
        {
            Geometry = geometry;
            Footprints = footprints;
            Scale = scale;
            OriginX = originX;
            OriginY = originY;
        }

        public SavedLayoutGeometry Geometry { get; }
        public IReadOnlyList<ProjectedLayoutFootprint> Footprints { get; }
        public double Scale { get; }
        public double OriginX { get; }
        public double OriginY { get; }
    }

    public static class SavedLayoutProjector
    {
        public static SavedLayoutGeometry Build(IReadOnlyList<DisplaySetting> displaySettings)
        {
            var source = displaySettings ?? [];
            var groups = DisplayGroupBuilder.GroupDisplaysForUI([.. source]);
            var footprints = new List<SavedLayoutFootprint>();

            int enabledDisplayCount = source.Count(setting => setting.IsEnabled);
            int disabledDisplayCount = source.Count - enabledDisplayCount;
            int enabledLayoutCount = 0;
            int layoutUnavailableCount = 0;
            int cloneGroupCount = 0;
            int inconsistentCloneCount = 0;

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var members = group.AllMembers;
                var enabledMembers = members.Where(member => member.IsEnabled).ToList();
                if (group.IsCloneGroup) cloneGroupCount++;
                if (enabledMembers.Count == 0) continue;

                enabledLayoutCount++;
                var geometrySource = IsDrawable(group.RepresentativeSetting)
                    ? group.RepresentativeSetting
                    : enabledMembers.FirstOrDefault(IsDrawable);

                bool inconsistent = group.IsCloneGroup && HasInconsistentEnabledCloneGeometry(enabledMembers);
                if (inconsistent) inconsistentCloneCount++;

                if (geometrySource == null)
                {
                    layoutUnavailableCount++;
                    continue;
                }

                var orderedMembers = new[] { group.RepresentativeSetting }
                    .Concat(members.Where(member => !ReferenceEquals(member, group.RepresentativeSetting)));
                var memberMetadata = new ReadOnlyCollection<SavedLayoutMember>(
                    [.. orderedMembers.Select(member => new SavedLayoutMember(
                        GetDisplayName(member), member.IsEnabled, member.IsPrimary, member.IsCloneSource))]);

                footprints.Add(new SavedLayoutFootprint(
                    groupIndex,
                    GetDisplayName(group.RepresentativeSetting),
                    geometrySource.DisplayPositionX,
                    geometrySource.DisplayPositionY,
                    geometrySource.Width,
                    geometrySource.Height,
                    geometrySource.Rotation,
                    enabledMembers.Any(member => member.IsPrimary),
                    group.IsCloneGroup,
                    members.Count,
                    inconsistent,
                    memberMetadata));
            }

            var bounds = CalculateBounds(footprints);
            int overlapCount = CountOverlaps(footprints);
            int primaryDisplayCount = source.Count(setting => setting.IsEnabled && setting.IsPrimary);

            return new SavedLayoutGeometry(
                new ReadOnlyCollection<SavedLayoutFootprint>(footprints),
                bounds,
                enabledDisplayCount,
                enabledLayoutCount,
                disabledDisplayCount,
                layoutUnavailableCount,
                cloneGroupCount,
                inconsistentCloneCount,
                overlapCount,
                primaryDisplayCount);
        }

        public static SavedLayoutProjection Fit(
            SavedLayoutGeometry geometry, double viewportWidth, double viewportHeight, double inset)
        {
            geometry ??= Build([]);
            double safeInset = double.IsFinite(inset) && inset > 0 ? inset : 0;
            if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight)
                || viewportWidth <= 0 || viewportHeight <= 0 || !geometry.HasLogicalBounds)
            {
                return EmptyProjection(geometry);
            }

            double usableWidth = Math.Max(0, viewportWidth - (2 * safeInset));
            double usableHeight = Math.Max(0, viewportHeight - (2 * safeInset));
            if (usableWidth <= 0 || usableHeight <= 0)
            {
                return EmptyProjection(geometry);
            }

            double logicalWidth = geometry.LogicalBounds.Width;
            double logicalHeight = geometry.LogicalBounds.Height;
            double scale = Math.Min(usableWidth / logicalWidth, usableHeight / logicalHeight);
            if (!double.IsFinite(scale) || scale <= 0)
            {
                return EmptyProjection(geometry);
            }

            double originX = safeInset + ((usableWidth - (logicalWidth * scale)) / 2);
            double originY = safeInset + ((usableHeight - (logicalHeight * scale)) / 2);
            if (!double.IsFinite(originX) || !double.IsFinite(originY))
            {
                return EmptyProjection(geometry);
            }

            var projected = geometry.Footprints.Select(footprint =>
                new ProjectedLayoutFootprint(
                    footprint,
                    originX + ((footprint.X - geometry.LogicalBounds.X) * scale),
                    originY + ((footprint.Y - geometry.LogicalBounds.Y) * scale),
                    footprint.Width * scale,
                    footprint.Height * scale)).ToList();

            if (projected.Any(item =>
                !double.IsFinite(item.X) || !double.IsFinite(item.Y)
                || !double.IsFinite(item.Width) || !double.IsFinite(item.Height)))
            {
                return EmptyProjection(geometry);
            }

            return new SavedLayoutProjection(
                geometry,
                new ReadOnlyCollection<ProjectedLayoutFootprint>(projected),
                scale,
                originX,
                originY);
        }

        private static bool IsDrawable(DisplaySetting setting) => setting != null && setting.IsEnabled && setting.Width > 0 && setting.Height > 0;

        private static bool HasInconsistentEnabledCloneGeometry(IReadOnlyList<DisplaySetting> enabledMembers)
        {
            if (enabledMembers.Count < 2)
            {
                return false;
            }

            var first = enabledMembers[0];
            return enabledMembers.Skip(1).Any(member =>
                member.DisplayPositionX != first.DisplayPositionX
                || member.DisplayPositionY != first.DisplayPositionY
                || member.Width != first.Width
                || member.Height != first.Height
                || member.Rotation != first.Rotation);
        }

        private static SavedLayoutBounds CalculateBounds(IReadOnlyList<SavedLayoutFootprint> footprints)
        {
            if (footprints.Count == 0)
            {
                return new SavedLayoutBounds(0, 0, 0, 0);
            }

            long minX = footprints.Min(footprint => footprint.X);
            long minY = footprints.Min(footprint => footprint.Y);
            long maxX = footprints.Max(footprint => footprint.X + footprint.Width);
            long maxY = footprints.Max(footprint => footprint.Y + footprint.Height);
            return new SavedLayoutBounds(minX, minY, maxX - minX, maxY - minY);
        }

        private static int CountOverlaps(IReadOnlyList<SavedLayoutFootprint> footprints)
        {
            int overlaps = 0;
            for (int i = 0; i < footprints.Count; i++)
            {
                var first = footprints[i];
                long firstRight = first.X + first.Width;
                long firstBottom = first.Y + first.Height;

                for (int j = i + 1; j < footprints.Count; j++)
                {
                    var second = footprints[j];
                    long secondRight = second.X + second.Width;
                    long secondBottom = second.Y + second.Height;
                    if (first.X < secondRight && second.X < firstRight
                        && first.Y < secondBottom && second.Y < firstBottom)
                    {
                        overlaps++;
                    }
                }
            }

            return overlaps;
        }

        private static string GetDisplayName(DisplaySetting setting)
        {
            if (!string.IsNullOrWhiteSpace(setting?.ReadableDeviceName))
            {
                return setting.ReadableDeviceName.Trim();
            }
            if (!string.IsNullOrWhiteSpace(setting?.DeviceString))
            {
                return setting.DeviceString.Trim();
            }
            if (!string.IsNullOrWhiteSpace(setting?.DeviceName))
            {
                return setting.DeviceName.Trim();
            }
            return "Display";
        }

        private static SavedLayoutProjection EmptyProjection(SavedLayoutGeometry geometry) => new SavedLayoutProjection(geometry, [], 0, 0, 0);
    }
}
