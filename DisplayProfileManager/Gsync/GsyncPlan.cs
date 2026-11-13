using System;
using System.Linq;

using DisplayProfileManager.Gsync.Models;

namespace DisplayProfileManager.Gsync
{
    // The NVIDIA Control Panel G-SYNC switch spans five global driver settings that must change together
    // Adapted from GsyncStatusTrayToggle (MIT), see THIRD-PARTY-LICENSES.md
    internal static class GsyncPlan
    {
        internal const int ApplicationMode = 0;
        internal const int ApplicationState = 1;
        internal const int GlobalFeature = 2;
        internal const int GlobalMode = 3;
        internal const int PreferredRefresh = 4;
        internal const int SettingCount = 5;

        // VRR_MODE, VRR_APP_OVERRIDE, VRRFEATUREINDICATOR, VRRREQUESTSTATE, REFRESH_RATE_OVERRIDE
        internal static readonly uint[] SettingIds = [0x1194F158, 0x10A879CF, 0x1094F157, 0x1094F1F7, 0x0064B541];
        internal static readonly uint[] SettingDefaults = [1, 0, 1, 1, 0];

        // Save order: mode first, then the feature indicator after the display returns, then the request state
        internal static readonly int[][] SaveGroups = [[ApplicationMode, ApplicationState], [GlobalFeature], [PreferredRefresh, GlobalMode]];

        internal static bool IsOn(GsyncMode mode) => mode != GsyncMode.Off;

        internal static GsyncMode? Decode(uint applicationMode) => applicationMode switch
        {
            0 => GsyncMode.Off,
            1 => GsyncMode.Fullscreen,
            2 => GsyncMode.FullscreenAndWindowed,
            _ => null
        };

        internal static uint[] MakeTarget(GsyncMode target, uint[] current)
        {
            ArgumentNullException.ThrowIfNull(current);
            if (current.Length != SettingCount)
                throw new ArgumentException($"Expected {SettingCount} values", nameof(current));

            uint mode = target switch
            {
                GsyncMode.Fullscreen => 1u,
                GsyncMode.FullscreenAndWindowed => 2u,
                _ => 0u
            };
            bool on = IsOn(target);
            uint[] plan = [mode, on ? 0u : 4u, on ? 1u : 0u, mode, on ? 1u : 0u];

            // Switching between the two on modes keeps the user's per-feature values
            if (on && Decode(current[ApplicationMode]) is GsyncMode currentMode && IsOn(currentMode))
            {
                plan[ApplicationState] = current[ApplicationState];
                plan[GlobalFeature] = current[GlobalFeature];
                plan[PreferredRefresh] = current[PreferredRefresh];
            }

            return plan;
        }

        internal static bool IsOnOffChange(uint[] current, uint[] target)
        {
            var currentMode = Decode(current[ApplicationMode]);
            var targetMode = Decode(target[ApplicationMode]);
            return currentMode.HasValue && targetMode.HasValue && IsOn(currentMode.Value) != IsOn(targetMode.Value);
        }

        internal static bool SameValues(uint[] a, uint[] b) => a.SequenceEqual(b);
    }
}
