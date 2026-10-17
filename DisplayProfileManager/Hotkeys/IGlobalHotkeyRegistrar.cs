using System;
using System.Collections.Generic;

using DisplayProfileManager.Hotkeys.Models;

namespace DisplayProfileManager.Hotkeys
{
    public interface IGlobalHotkeyRegistrar : IDisposable
    {
        int RegisterHotkey(uint virtualKey, uint modifiers, Action callback);
        bool UnregisterHotkey(int hotkeyId);
        int RegisterProfileHotkey(string profileId, HotkeyConfig hotkey, Action callback);
        bool UnregisterProfileHotkey(string profileId);
        void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> profileHotkeys, Func<string, Action> callbackFactory);
        void UnregisterAllProfileHotkeys();
    }
}
