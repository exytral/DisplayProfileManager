using System;
using System.Collections.Generic;
using System.Linq;

using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.Hotkeys
{
    internal static class HotkeyConflictPolicy
    {
        internal static bool IsAssigned(HotkeyConfig hotkey) =>
            hotkey != null && hotkey.Key != System.Windows.Input.Key.None;

        internal static bool UsesSameChord(HotkeyConfig first, HotkeyConfig second) =>
            IsAssigned(first) &&
            IsAssigned(second) &&
            first.Key == second.Key &&
            first.ModifierKeys == second.ModifierKeys;

        internal static Profile FindProfileConflict(
            IEnumerable<Profile> profiles,
            HotkeyConfig requested,
            string excludedProfileId = null)
        {
            if (!IsAssigned(requested) || profiles == null)
            {
                return null;
            }

            return profiles.FirstOrDefault(profile =>
                profile != null &&
                !string.Equals(profile.Id, excludedProfileId, StringComparison.Ordinal) &&
                UsesSameChord(profile.HotkeyConfig, requested));
        }
    }
}
