using System;

using DisplayProfileManager.Hotkeys.Interop;
using DisplayProfileManager.Hotkeys.Models;

namespace DisplayProfileManager.Hotkeys
{
    internal enum QuickSwitchRegistrationResult
    {
        Disabled,
        Unchanged,
        Registered,
        Failed
    }

    internal sealed class QuickSwitchHotkeyRegistration : IDisposable
    {
        private readonly IGlobalHotkeyRegistrar _registrar;
        private readonly Action _callback;
        private int _hotkeyId = -1;

        internal bool IsRegistered => _hotkeyId >= 0;
        internal HotkeyConfig RegisteredHotkey { get; private set; }

        internal QuickSwitchHotkeyRegistration(IGlobalHotkeyRegistrar registrar, Action callback)
        {
            _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        internal QuickSwitchRegistrationResult Reconcile(bool enabled, HotkeyConfig requested)
        {
            if (!enabled)
            {
                Disable();
                return QuickSwitchRegistrationResult.Disabled;
            }

            if (requested?.IsValid() != true)
            {
                return QuickSwitchRegistrationResult.Failed;
            }

            if (IsRegistered && RegisteredHotkey?.Equals(requested) == true)
            {
                return QuickSwitchRegistrationResult.Unchanged;
            }

            int virtualKey = KeyConverter.ToVirtualKey(requested.Key);
            uint modifiers = KeyConverter.ConvertModifierKeys(requested.ModifierKeys);
            int replacementId = virtualKey == 0 ? -1 : _registrar.RegisterHotkey((uint)virtualKey, modifiers, _callback);
            if (replacementId < 0)
            {
                return QuickSwitchRegistrationResult.Failed;
            }

            int previousId = _hotkeyId;
            _hotkeyId = replacementId;
            RegisteredHotkey = requested.Clone();

            if (previousId >= 0)
                _registrar.UnregisterHotkey(previousId);

            return QuickSwitchRegistrationResult.Registered;
        }

        internal void Disable()
        {
            if (_hotkeyId >= 0)
                _registrar.UnregisterHotkey(_hotkeyId);

            _hotkeyId = -1;
            RegisteredHotkey = null;
        }

        public void Dispose() => Disable();
    }
}
