using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

using DisplayProfileManager.Hotkeys.Models;

namespace DisplayProfileManager.UI.Windows
{
    internal sealed record QuickSwitchKeycap(string Label, bool IsWindowsModifier);

    internal static class QuickSwitchHotkeyPresentation
    {
        internal static IReadOnlyList<QuickSwitchKeycap> Build(HotkeyConfig hotkey)
        {
            if (hotkey == null || hotkey.Key == Key.None)
            {
                return [];
            }

            var keycaps = new List<QuickSwitchKeycap>();
            if ((hotkey.ModifierKeys & ModifierKeys.Control) == ModifierKeys.Control)
                keycaps.Add(new QuickSwitchKeycap("Ctrl", false));
            if ((hotkey.ModifierKeys & ModifierKeys.Alt) == ModifierKeys.Alt)
                keycaps.Add(new QuickSwitchKeycap("Alt", false));
            if ((hotkey.ModifierKeys & ModifierKeys.Shift) == ModifierKeys.Shift)
                keycaps.Add(new QuickSwitchKeycap("Shift", false));
            if ((hotkey.ModifierKeys & ModifierKeys.Windows) == ModifierKeys.Windows)
                keycaps.Add(new QuickSwitchKeycap("Windows", true));

            keycaps.Add(new QuickSwitchKeycap(GetKeyLabel(hotkey.Key), false));
            return new ReadOnlyCollection<QuickSwitchKeycap>(keycaps);
        }

        private static string GetKeyLabel(Key key)
        {
            string label = key.ToString();
            if (label.StartsWith("D", StringComparison.Ordinal) && label.Length == 2 && char.IsDigit(label[1]))
            {
                return label[1].ToString();
            }

            return key switch
            {
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemPeriod => ".",
                Key.OemComma => ",",
                _ => label
            };
        }
    }
}
