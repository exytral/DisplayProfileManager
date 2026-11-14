using System;
using System.Collections.Generic;
using System.Linq;

using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr.Nvidia
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

        internal static bool IsOn(VrrMode mode) => mode != VrrMode.Off;

        internal static VrrMode? Decode(uint applicationMode) => applicationMode switch
        {
            0 => VrrMode.Off,
            1 => VrrMode.On,
            2 => VrrMode.OnIncludingWindowed,
            _ => null
        };

        internal static uint[] MakeTarget(VrrMode target, uint[] current)
        {
            ArgumentNullException.ThrowIfNull(current);
            if (current.Length != SettingCount)
                throw new ArgumentException($"Expected {SettingCount} values", nameof(current));

            uint mode = target switch
            {
                VrrMode.On => 1u,
                VrrMode.OnIncludingWindowed => 2u,
                _ => 0u
            };
            bool on = IsOn(target);
            uint[] plan = [mode, on ? 0u : 4u, on ? 1u : 0u, mode, on ? 1u : 0u];

            // Switching between the two on modes keeps the user's per-feature values
            if (on && Decode(current[ApplicationMode]) is VrrMode currentMode && IsOn(currentMode))
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

        // false only when at least one display answered and every answer was no; unknown or no answers count as capable
        internal static bool HasCapableDisplay(IReadOnlyCollection<bool?> displays)
        {
            ArgumentNullException.ThrowIfNull(displays);
            return displays.Count == 0 || displays.Any(d => d != false);
        }

        internal static bool SameValues(uint[] a, uint[] b) => a.SequenceEqual(b);
    }
}
