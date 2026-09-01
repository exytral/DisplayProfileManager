using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DisplayProfileManager.UI
{
    public enum TrayNotificationIcon
    {
        None,
        Info,
        Error
    }

    public sealed class TrayIcon : IDisposable
    {
        #region Core
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private readonly ProfileManager _profileManager;
        private readonly HwndSource _hwndSource;
        private readonly uint _taskbarCreatedMessage;
        private readonly Guid _iconGuid = new Guid("5F0F5E65-47B6-4C28-9A4B-9F2A52E4D2E2");

        private Icon _defaultIcon;
        private Icon _currentIcon;
        private bool _iconAdded;
        private bool _visible = true;
        private bool _disposed;
        private string _notificationLink;

        public event EventHandler ShowMainWindow;
        public event EventHandler ShowSettingsWindow;
        public event EventHandler ExitApplication;

        public TrayIcon()
        {
            _profileManager = ProfileManager.Instance;
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

            var sourceParameters = new HwndSourceParameters("DisplayProfileManager.TrayIcon")
            {
                Width = 0,
                Height = 0,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x00000080
            };

            _hwndSource = new HwndSource(sourceParameters);
            _hwndSource.AddHook(WndProc);
            ShowWindow(_hwndSource.Handle, SwHide);

            _defaultIcon = ApplicationIconHelper.LoadIcon();
            _currentIcon = _defaultIcon;

            SetupEventHandlers();
            UpdateTrayIcon(_profileManager.GetCurrentProfile());
            UpdateTrayIconTooltip();
            AddNotifyIcon();
        }

        #endregion

        #region P/Invoke

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CheckMenuItem(IntPtr hMenu, uint uIDCheckItem, uint uCheck);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetMenuItemInfo(
            IntPtr hMenu,
            uint uItem,
            bool fByPosition,
            ref MENUITEMINFO lpmii);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint TrackPopupMenuEx(
            IntPtr hMenu,
            uint uFlags,
            int x,
            int y,
            IntPtr hWnd,
            IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc,
            ref BITMAPINFOHEADER pbmi,
            uint usage,
            out IntPtr ppvBits,
            IntPtr hSection,
            uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DrawIconEx(
            IntPtr hdc,
            int xLeft,
            int yTop,
            IntPtr hIcon,
            int cxWidth,
            int cyWidth,
            uint istepIfAniCur,
            IntPtr hbrFlickerFreeDraw,
            uint diFlags);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MENUITEMINFO
        {
            public uint cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public IntPtr dwItemData;
            public IntPtr dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        #endregion

        #region Constants

        private const int WmLButtonUp = 0x0202;
        private const int WmLButtonDblClk = 0x0203;
        private const int WmRButtonUp = 0x0205;
        private const int WmAppTray = 0x0400 + 0x41;
        private const int SwHide = 0;
        private const int SmCxSmIcon = 49;

        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint NifInfo = 0x00000010;
        private const uint NifGuid = 0x00000020;

        private const uint NimAdd = 0x00000000;
        private const uint NimModify = 0x00000001;
        private const uint NimDelete = 0x00000002;
        private const uint NimSetFocus = 0x00000003;
        private const uint NimSetVersion = 0x00000004;
        private const uint NotifyIconVersion4 = 4;

        private const uint NiifNone = 0x00000000;
        private const uint NiifInfo = 0x00000001;
        private const uint NiifError = 0x00000003;

        private const uint MfString = 0x00000000;
        private const uint MfSeparator = 0x00000800;
        private const uint MfByCommand = 0x00000000;
        private const uint MfChecked = 0x00000008;
        private const uint MiimBitmap = 0x00000080;

        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;

        private const uint MenuProfileBase = 0x1000;
        private const uint MenuOpen = 0x2001;
        private const uint MenuSettings = 0x2002;
        private const uint MenuExit = 0x2003;

        private const uint DiNormal = 0x0003;

        #endregion

        #region Private Methods

        private void SetupEventHandlers()
        {
            _profileManager.ProfileAdded += OnProfileChanged;
            _profileManager.ProfileUpdated += OnProfileChanged;
            _profileManager.ProfileDeleted += OnProfileDeleted;
            _profileManager.ProfilesLoaded += OnProfilesLoaded;
            _profileManager.ProfileApplied += OnProfileApplied;
        }

        private void UpdateTrayIcon(Profile profile)
        {
            Icon icon = null;

            try
            {
                icon = IconHelper.LoadIcon(profile?.Icon) ?? _defaultIcon;

                if (ReferenceEquals(icon, _currentIcon)) return;

                var oldIcon = _currentIcon;
                _currentIcon = icon;
                UpdateNativeIcon();

                if (oldIcon != null && !ReferenceEquals(oldIcon, _defaultIcon))
                    oldIcon.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to update tray icon");

                if (icon != null &&!ReferenceEquals(icon, _defaultIcon) && !ReferenceEquals(icon, _currentIcon))
                    icon.Dispose();

                if (!ReferenceEquals(_currentIcon, _defaultIcon))
                {
                    var oldIcon = _currentIcon;
                    _currentIcon = _defaultIcon;
                    UpdateNativeIcon();
                    oldIcon?.Dispose();
                }
            }
        }

        private void UpdateNativeIcon()
        {
            if (!_iconAdded || _currentIcon == null) return;

            var data = CreateNotifyIconData(NifIcon);
            data.hIcon = _currentIcon.Handle;
            if (!Shell_NotifyIcon(NimModify, ref data))
                logger.Warn("Failed to update tray icon");
        }

        private void UpdateTrayIconTooltip()
        {
            string tooltip = "Display Profile Manager";
            var currentProfile = _profileManager.GetCurrentProfile();

            if (currentProfile != null)
                tooltip = BuildTooltip(currentProfile.Name);

            var data = CreateNotifyIconData(NifTip);
            data.szTip = tooltip;

            if (_iconAdded && !Shell_NotifyIcon(NimModify, ref data))
                logger.Warn("Failed to update tray tooltip");
        }

        private static string BuildTooltip(string profileName)
        {
            const string prefix = "Display Profile Manager - ";
            string fullTooltip = string.IsNullOrEmpty(profileName)
                ? "Display Profile Manager"
                : $"{prefix}{profileName}";

            if (fullTooltip.Length < 64)
            {
                return fullTooltip;
            }

            int availableSpace = 63 - prefix.Length - 3;
            return availableSpace > 0
                ? $"{prefix}{profileName.Substring(0, availableSpace)}..."
                : fullTooltip.Substring(0, 60) + "...";
        }

        private void AddNotifyIcon()
        {
            if (_disposed || !_visible || _iconAdded || _currentIcon == null) return;

            var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip | NifGuid);
            data.hIcon = _currentIcon.Handle;
            data.szTip = BuildTooltip(_profileManager.GetCurrentProfile()?.Name);
            data.uVersion = NotifyIconVersion4;

            if (!Shell_NotifyIcon(NimAdd, ref data))
            {
                logger.Warn("Failed to add tray icon");
                return;
            }

            if (!Shell_NotifyIcon(NimSetVersion, ref data))
                logger.Warn("Failed to set tray icon notification version");

            _iconAdded = true;
        }

        private void RemoveNotifyIcon()
        {
            if (!_iconAdded) return;

            var data = CreateNotifyIconData(0);

            if (!Shell_NotifyIcon(NimDelete, ref data))
                logger.Warn("Failed to remove tray icon");

            _iconAdded = false;
        }

        private IntPtr CreateContextMenu(out List<IntPtr> bitmapHandles)
        {
            bitmapHandles = new List<IntPtr>();
            IntPtr menu = CreatePopupMenu();

            if (menu == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
                
            int iconSize = GetSystemMetrics(SmCxSmIcon);
            var profiles = _profileManager.GetAllProfiles().OrderBy(p => p.Name, NaturalStringComparer.Instance).ToList();

            uint command = MenuProfileBase;
            foreach (var profile in profiles)
            {
                string displayName = profile.Name;

                if (profile.HotkeyConfig?.IsEnabled == true && profile.HotkeyConfig.Key != Key.None)
                    displayName += $" ({profile.HotkeyConfig})";

                AppendMenu(menu, MfString, command, displayName);

                if (profile.Id == _profileManager.CurrentProfileId)
                    CheckMenuItem(menu, command, MfByCommand | MfChecked);
                else if (!string.IsNullOrEmpty(profile.Icon))
                {
                    using (var icon = IconHelper.LoadIcon(profile.Icon))
                    {
                        if (icon != null)
                        {
                            IntPtr hBitmap = CreateMenuIconBitmap(icon.Handle, iconSize);
                            if (hBitmap != IntPtr.Zero)
                            {
                                bitmapHandles.Add(hBitmap);

                                var itemInfo = new MENUITEMINFO
                                {
                                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                                    fMask = MiimBitmap,
                                    hbmpItem = hBitmap
                                };

                                SetMenuItemInfo(menu, command, false, ref itemInfo);
                            }
                        }
                    }
                }

                command++;
            }

            if (profiles.Count > 0)
                AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, MenuOpen, "Open");
            AppendMenu(menu, MfString, MenuSettings, "Settings");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, MenuExit, "Exit");

            return menu;
        }

        private static IntPtr CreateMenuIconBitmap(IntPtr hIcon, int size)
        {
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);

            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            IntPtr hBitmap = CreateDIBSection(
                hdcScreen,
                ref bmi,
                0,
                out IntPtr pvBits,
                IntPtr.Zero,
                0);

            if (hBitmap != IntPtr.Zero)
            {
                IntPtr hOld = SelectObject(hdcMem, hBitmap);
                DrawIconEx(
                    hdcMem,
                    0,
                    0,
                    hIcon,
                    size,
                    size,
                    0,
                    IntPtr.Zero,
                    DiNormal);

                int count = size * size;
                int[] pixels = new int[count];
                Marshal.Copy(pvBits, pixels, 0, count);

                for (int i = 0; i < count; i++)
                {
                    uint px = unchecked((uint)pixels[i]);
                    byte a = (byte)(px >> 24);
                    byte r = (byte)(((px >> 16) & 0xFF) * a / 255);
                    byte g = (byte)(((px >> 8) & 0xFF) * a / 255);
                    byte b = (byte)((px & 0xFF) * a / 255);

                    pixels[i] = unchecked(
                        (int)(((uint)a << 24) |
                              ((uint)r << 16) |
                              ((uint)g << 8) |
                              b));
                }

                Marshal.Copy(pixels, 0, pvBits, count);
                SelectObject(hdcMem, hOld);
            }

            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            return hBitmap;
        }

        private void ShowContextMenu()
        {
            if (_disposed) return;

            IntPtr menu = CreateContextMenu(out var bitmapHandles);

            if (menu == IntPtr.Zero) return;

            try
            {
                SetForegroundWindow(_hwndSource.Handle);
                GetCursorPos(out var point);

                uint command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, point.X, point.Y, _hwndSource.Handle, IntPtr.Zero);
                if (command >= MenuProfileBase && command < MenuOpen)
                {
                    int index = checked((int)(command - MenuProfileBase));
                    var profile = _profileManager.GetAllProfiles().OrderBy(p => p.Name, NaturalStringComparer.Instance).ElementAtOrDefault(index);
                    if (profile != null)
                        _ = ApplyProfileFromTrayAsync(profile);
                }
                else if (command == MenuOpen)
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                else if (command == MenuSettings)
                    ShowSettingsWindow?.Invoke(this, EventArgs.Empty);
                else if (command == MenuExit)
                    ExitApplication?.Invoke(this, EventArgs.Empty);

                var focusData = CreateNotifyIconData(0);
                Shell_NotifyIcon(NimSetFocus, ref focusData);
            }
            finally
            {
                foreach (var handle in bitmapHandles)
                    DeleteObject(handle);

                DestroyMenu(menu);
            }
        }

        private async System.Threading.Tasks.Task ApplyProfileFromTrayAsync(Profile profile)
        {
            try
            {
                logger.Info($"Applying profile '{profile.Name}' from TrayIcon");

                var applyResult = await _profileManager.ApplyProfileAsync(profile, ProfileManager.ApplySource.Tray);
                if (!applyResult.Success)
                {
                    string errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, applyResult);

                    logger.Warn(errorDetails);
                    ShowNotification("Apply failed", errorDetails, TrayNotificationIcon.Error);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying profile from tray");
                try
                {
                    ShowNotification("Apply failed","Error applying profile", TrayNotificationIcon.Error);
                }
                catch { }
            }
        }

        private void ShowBalloonTip(string title, string message, TrayNotificationIcon icon)
        {
            if (!_iconAdded) return;

            var data = CreateNotifyIconData(NifInfo);
            data.szInfoTitle = title;
            data.szInfo = message;
            data.dwInfoFlags = icon switch
            {
                TrayNotificationIcon.Info => NiifInfo,
                TrayNotificationIcon.Error => NiifError,
                _ => NiifNone
            };

            if (!Shell_NotifyIcon(NimModify, ref data))
                logger.Warn("Failed to display tray notification");
        }

        private void OnProfilesLoaded(object sender, EventArgs e) => UpdateTrayIconTooltip();

        private void OnProfileApplied(object sender, ProfileManager.ProfileAppliedEventArgs e)
        {
            UpdateTrayIconTooltip();
            UpdateTrayIcon(e.Profile);
        }

        private void OnProfileChanged(object sender, Profile profile)
        {
            UpdateTrayIcon(_profileManager.GetCurrentProfile());
            UpdateTrayIconTooltip();
        }

        private void OnProfileDeleted(object sender, string profileId)
        {
            UpdateTrayIcon(_profileManager.GetCurrentProfile());
            UpdateTrayIconTooltip();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (unchecked((uint)msg) == _taskbarCreatedMessage)
            {
                if (_visible)
                    AddNotifyIcon();

                handled = true;

                return IntPtr.Zero;
            }

            if (msg == unchecked((int)WmAppTray))
            {
                int mouseMessage = unchecked((int)(lParam.ToInt64() & 0xFFFF));

                if (mouseMessage == WmLButtonUp)
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                else if (mouseMessage == WmLButtonDblClk)
                    ShowMainWindow?.Invoke(this, EventArgs.Empty);
                else if (mouseMessage == WmRButtonUp)
                    ShowContextMenu();
                else if (mouseMessage == 0x0405)
                    OpenNotificationLink();

                handled = true;
            }

            return IntPtr.Zero;
        }

        private void OpenNotificationLink()
        {
            if (string.IsNullOrEmpty(_notificationLink)) return;

            try
            {
                Process.Start(new ProcessStartInfo(_notificationLink)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error opening notification link");
            }
            finally
            {
                _notificationLink = null;
            }
        }

        private NOTIFYICONDATA CreateNotifyIconData(uint flags)
        {
            return new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwndSource.Handle,
                uID = 1,
                uFlags = flags,
                uCallbackMessage = WmAppTray,
                guidItem = _iconGuid
            };
        }

        #endregion

        #region Public Methods

        public void ShowNotification(string title, string message, TrayNotificationIcon icon = TrayNotificationIcon.None)
        {
            _notificationLink = null;
            ShowBalloonTip(title, message, icon);
        }

        public void ShowUpdateNotification(string title, string message, string url)
        {
            _notificationLink = url;
            ShowBalloonTip(title, message, TrayNotificationIcon.Info);
        }

        public void UpdateTooltip(string text)
        {
            var data = CreateNotifyIconData(NifTip);
            data.szTip = text;

            if (_iconAdded)
                Shell_NotifyIcon(NimModify, ref data);
        }

        public void Show()
        {
            _visible = true;
            AddNotifyIcon();
        }

        public void Hide()
        {
            _visible = false;
            RemoveNotifyIcon();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _profileManager.ProfileAdded -= OnProfileChanged;
            _profileManager.ProfileUpdated -= OnProfileChanged;
            _profileManager.ProfileDeleted -= OnProfileDeleted;
            _profileManager.ProfilesLoaded -= OnProfilesLoaded;
            _profileManager.ProfileApplied -= OnProfileApplied;

            RemoveNotifyIcon();
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();

            if (_currentIcon != null && !ReferenceEquals(_currentIcon, _defaultIcon))
                _currentIcon.Dispose();

            _defaultIcon?.Dispose();
            _defaultIcon = null;
            _currentIcon = null;
        }

        #endregion
    }
}