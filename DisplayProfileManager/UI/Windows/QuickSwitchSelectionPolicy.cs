using System;
using System.Collections.Generic;
using System.Windows.Input;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.Windows
{
    internal enum QuickSwitchInputAction
    {
        None,
        Dismiss,
        MoveUp,
        MoveDown,
        Apply
    }

    internal static class QuickSwitchSelectionPolicy
    {
        internal static QuickSwitchInputAction GetInputAction(Key key, int itemCount)
        {
            if (key == Key.Escape)
            {
                return QuickSwitchInputAction.Dismiss;
            }
            if (itemCount <= 0)
            {
                return QuickSwitchInputAction.None;
            }
            if (key == Key.Up)
            {
                return QuickSwitchInputAction.MoveUp;
            }
            if (key == Key.Down)
            {
                return QuickSwitchInputAction.MoveDown;
            }
            if (key == Key.Enter)
            {
                return QuickSwitchInputAction.Apply;
            }

            return QuickSwitchInputAction.None;
        }

        internal static int GetInitialIndex(IReadOnlyList<Profile> profiles, string currentProfileId, string previousSelectionId = null)
        {
            if (profiles == null || profiles.Count == 0)
            {
                return -1;
            }

            int current = FindProfileIndex(profiles, currentProfileId);
            if (current >= 0)
            {
                return current;
            }

            int previous = FindProfileIndex(profiles, previousSelectionId);
            return previous >= 0 ? previous : 0;
        }

        internal static int Move(int currentIndex, int count, int delta)
        {
            if (count <= 0)
            {
                return -1;
            }

            int normalized = currentIndex >= 0 && currentIndex < count ? currentIndex : 0;
            int moved = (normalized + delta) % count;
            return moved < 0 ? moved + count : moved;
        }

        private static int FindProfileIndex(IReadOnlyList<Profile> profiles, string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return -1;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i]?.Id, profileId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
