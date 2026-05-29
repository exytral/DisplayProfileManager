using DisplayProfileManager.Core;
using NLog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DisplayProfileManager.Helpers
{
    public class GlobalHotkeyHelper : IDisposable
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        #region P/Invoke

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WhKeyboardLl = 13;
        private const int WmKeydown = 0x0100;
        private const int WmSyskeydown = 0x0104;

        private const uint ModNone = 0x0000;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNorepeat = 0x4000;

        #endregion

        private HwndSource _hwndSource;
        private IntPtr _windowHandle;
        private readonly Dictionary<int, Action> _hotkeyActions = new Dictionary<int, Action>();
        private readonly Dictionary<string, int> _profileHotkeyIds = new Dictionary<string, int>();
        private readonly Dictionary<int, string> _hotkeyIdToProfileId = new Dictionary<int, string>();
        private int _currentHotkeyId = 9000;
        private bool _disposed = false;

        public GlobalHotkeyHelper()
        {
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    CreateMessageWindow();
                });
            }
            else
            {
                CreateMessageWindow();
            }
        }

        private void CreateMessageWindow()
        {
            var parameters = new HwndSourceParameters("GlobalHotkeyMessageWindow")
            {
                WindowStyle = 0,
                ExtendedWindowStyle = 0,
                PositionX = -10000,
                PositionY = -10000,
                Width = 1,
                Height = 1
            };

            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);
            _windowHandle = _hwndSource.Handle;

            logger.Debug($"Created message window with handle: 0x{_windowHandle:X}");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmHotkey = 0x0312;
            if (msg == WmHotkey)
            {
                int hotkeyId = wParam.ToInt32();
                logger.Debug($"WmHotkey received for hotkey ID: {hotkeyId}");
                if (_hotkeyActions.TryGetValue(hotkeyId, out Action callback))
                {
                    try
                    {
                        logger.Debug($"Executing callback for hotkey {hotkeyId}");
                        // Execute callback on dispatcher thread to avoid threading issues
                        if (System.Windows.Application.Current?.Dispatcher != null)
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(callback);
                        else
                            callback?.Invoke();

                        handled = true;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Error executing hotkey {hotkeyId} callback");
                    }
                }
                else
                {
                    logger.Warn($"No callback found for hotkey ID: {hotkeyId}");
                }
            }

            return IntPtr.Zero;
        }

        public int RegisterHotkey(uint virtualKey, uint modifiers, Action callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GlobalHotkeyHelper));

            int hotkeyId = _currentHotkeyId++;
            uint finalModifiers = modifiers | ModNorepeat; // Add ModNorepeat to prevent repeated hotkey events
            if (RegisterHotKey(_windowHandle, hotkeyId, finalModifiers, virtualKey))
            {
                _hotkeyActions[hotkeyId] = callback;
                logger.Info($"Successfully registered hotkey {hotkeyId} for key 0x{virtualKey:X2} with modifiers 0x{finalModifiers:X2}");
                return hotkeyId;
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                logger.Error($"Failed to register hotkey {hotkeyId}. Error code: {error}");

                if (error == 1409)
                    logger.Warn("Hotkey is already registered by another application");

                return -1;
            }
        }

        public bool UnregisterHotkey(int hotkeyId)
        {
            if (_disposed || hotkeyId < 0) return false;

            bool result = UnregisterHotKey(_windowHandle, hotkeyId);
            _hotkeyActions.Remove(hotkeyId);

            logger.Debug($"Unregistered hotkey {hotkeyId}: {(result ? "Success" : "Failed")}");
            return result;
        }

        public int RegisterProfileHotkey(string profileId, HotkeyConfig hotkey, Action callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GlobalHotkeyHelper));

            if (hotkey?.Key == Key.None || string.IsNullOrEmpty(profileId))
                return -1;

            UnregisterProfileHotkey(profileId);

            var virtualKey = KeyConverter.ToVirtualKey(hotkey.Key);
            var modifiers = KeyConverter.ConvertModifierKeys(hotkey.ModifierKeys);

            if (virtualKey == 0)
            {
                logger.Error($"Could not convert WPF Key {hotkey.Key} to virtual key");
                return -1;
            }

            int hotkeyId = RegisterHotkey((uint)virtualKey, modifiers, callback);

            if (hotkeyId > 0)
            {
                _profileHotkeyIds[profileId] = hotkeyId;
                _hotkeyIdToProfileId[hotkeyId] = profileId;
                logger.Info($"Registered profile hotkey for '{profileId}': {hotkey} (ID: {hotkeyId})");
            }
            else
            {
                logger.Error($"Failed to register profile hotkey for '{profileId}': {hotkey}");
            }

            return hotkeyId;
        }

        public bool UnregisterProfileHotkey(string profileId)
        {
            if (_disposed || string.IsNullOrEmpty(profileId)) return false;

            if (_profileHotkeyIds.TryGetValue(profileId, out int hotkeyId))
            {
                bool result = UnregisterHotkey(hotkeyId);
                _profileHotkeyIds.Remove(profileId);
                _hotkeyIdToProfileId.Remove(hotkeyId);

                logger.Debug($"Unregistered profile hotkey for '{profileId}' (ID: {hotkeyId}): {(result ? "Success" : "Failed")}");
                return result;
            }

            return true;
        }

        public void RegisterAllProfileHotkeys(Dictionary<string, HotkeyConfig> profileHotkeys, Func<string, Action> callbackFactory)
        {
            if (_disposed || profileHotkeys == null || callbackFactory == null) return;

            UnregisterAllProfileHotkeys();

            foreach (var kvp in profileHotkeys)
            {
                var profileId = kvp.Key;
                var hotkeyConfig = kvp.Value;

                if (hotkeyConfig?.IsEnabled == true && hotkeyConfig.Key != Key.None)
                {
                    var callback = callbackFactory(profileId);
                    if (callback != null)
                    {
                        RegisterProfileHotkey(profileId, hotkeyConfig, callback);
                    }
                }
            }
        }

        public void UnregisterAllProfileHotkeys()
        {
            if (_disposed) return;

            var profileIds = new List<string>(_profileHotkeyIds.Keys);
            foreach (var profileId in profileIds)
            {
                UnregisterProfileHotkey(profileId);
            }

            logger.Info("Unregistered all profile hotkeys");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    foreach (var hotkeyId in _hotkeyActions.Keys)
                    {
                        UnregisterHotKey(_windowHandle, hotkeyId);
                        logger.Debug($"Unregistered hotkey {hotkeyId} during disposal");
                    }
                    _hotkeyActions.Clear();
                    _profileHotkeyIds.Clear();
                    _hotkeyIdToProfileId.Clear();

                    if (System.Windows.Application.Current?.Dispatcher != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            _hwndSource?.RemoveHook(WndProc);
                            _hwndSource?.Dispose();
                        });
                    }
                    else
                    {
                        _hwndSource?.RemoveHook(WndProc);
                        _hwndSource?.Dispose();
                    }

                    _hwndSource = null;
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~GlobalHotkeyHelper() => Dispose(false);
    }
}