using System.Collections.Generic;
using System.Linq;

using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr.Amd
{
    // Pure FreeSync policy: AMD has one switch per display and no fullscreen-only variant
    internal static class AmdVrrPlan
    {
        internal readonly record struct DisplayState(bool Supported, bool Enabled);

        internal static bool ShouldEnable(VrrMode mode) => mode != VrrMode.Off;

        // Fullscreen only has no AMD equivalent, so it degrades to plain On
        internal static VrrMode EffectiveMode(VrrMode requested) => requested == VrrMode.OnFullscreenOnly ? VrrMode.On : requested;

        // Indexes of supported displays whose state differs from the target
        internal static List<int> SelectChanges(IReadOnlyList<DisplayState> displays, VrrMode mode)
        {
            bool target = ShouldEnable(mode);
            var changes = new List<int>();
            for (int i = 0; i < displays.Count; i++)
            {
                if (displays[i].Supported && displays[i].Enabled != target)
                    changes.Add(i);
            }

            return changes;
        }

        // On or Off when every supported display agrees, null when they differ or none is supported
        internal static VrrMode? DecodeCurrent(IReadOnlyList<DisplayState> displays)
        {
            var supported = displays.Where(d => d.Supported).ToList();
            if (supported.Count == 0) return null;
            if (supported.All(d => d.Enabled)) return VrrMode.On;
            return supported.All(d => !d.Enabled) ? VrrMode.Off : null;
        }
    }
}
