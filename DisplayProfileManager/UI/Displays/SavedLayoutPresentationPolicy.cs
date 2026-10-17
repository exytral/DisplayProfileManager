using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DisplayProfileManager.UI.Displays
{
    public static class SavedLayoutPresentationPolicy
    {
        public static IReadOnlyList<string> GetVisibleNames(SavedLayoutFootprint footprint)
        {
            if (footprint == null)
            {
                return [];
            }

            var names = footprint.IsCloneGroup
                ? [.. footprint.Members.Select(member => member.Name).Where(name => !string.IsNullOrWhiteSpace(name))]
                : new List<string> { footprint.Name };

            if (names.Count == 0 && !string.IsNullOrWhiteSpace(footprint.Name))
                names.Add(footprint.Name);

            return new ReadOnlyCollection<string>(names);
        }

        public static string BuildExceptionalSummary(SavedLayoutGeometry geometry)
        {
            if (geometry == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (geometry.DisabledDisplayCount > 0)
                parts.Add($"{geometry.DisabledDisplayCount} disabled {Plural(geometry.DisabledDisplayCount, "display")} not shown");
            if (geometry.LayoutUnavailableCount > 0)
                parts.Add($"{geometry.LayoutUnavailableCount} {Plural(geometry.LayoutUnavailableCount, "layout")} unavailable");
            if (geometry.OverlapCount > 0)
                parts.Add("saved overlap");
            if (geometry.InconsistentCloneCount > 0)
                parts.Add("clone geometry differs");
            if (geometry.HasPrimaryStateWarning)
                parts.Add($"primary state warning ({geometry.PrimaryDisplayCount} saved)");

            return string.Join(" • ", parts);
        }

        private static string Plural(int count, string singular) => count == 1 ? singular : singular + "s";
    }
}
