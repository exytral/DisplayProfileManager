using DisplayProfileManager.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DisplayProfileManager.Helpers
{
    public static class WallpaperHelper
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly uint[] _standardIntervalSeconds = { 60, 600, 1800, 3600, 21600, 86400 };
        private static readonly HashSet<string> _imageExtensions = new HashSet<string>([".jpg", ".jpeg", ".png", ".bmp", ".gif"], StringComparer.OrdinalIgnoreCase);
        internal const int SlideshowSettlementMaxAttempts = 5;
        internal const int SlideshowSettlementDelayMs = 50;

        #region Interop — Desktop Wallpaper COM

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
            void GetPropertyStore(uint flags, ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
            void GetAttributes(uint dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IntPtr ppenumShellItems);
        }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            [PreserveSig]
            int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(DesktopWallpaperPosition position);
            DesktopWallpaperPosition GetPosition();
            void SetSlideshow(IShellItemArray items);
            IShellItemArray GetSlideshow();
            void SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);
            void GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
            DesktopSlideshowState GetStatus();
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }

        private enum DesktopWallpaperPosition
        {
            Center = 0,
            Tile = 1,
            Stretch = 2,
            Fit = 3,
            Fill = 4,
            Span = 5,
        }

        [Flags]
        private enum DesktopSlideshowState : uint
        {
            Enabled = 0x01,
            Slideshow = 0x02,
            DisabledByRemoteSession = 0x04,
        }

        [Flags]
        private enum DesktopSlideshowOptions : uint
        {
            None = 0x00,
            ShuffleImages = 0x01,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        internal readonly struct DesktopMonitorRectResult
        {
            public int HResult { get; }
            public int Left { get; }
            public int Top { get; }
            public int Right { get; }
            public int Bottom { get; }
            public bool HasPositiveArea => Right > Left && Bottom > Top;

            public DesktopMonitorRectResult(int hResult, int left, int top, int right, int bottom)
            {
                HResult = hResult;
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        internal const int HResultSOk = 0;
        internal const int HResultSFalse = 1;
        private const string ClsidDesktopWallpaper = "C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD";
        private const uint SigdnFileSysPath = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHCreateShellItemArrayFromShellItem(
            IShellItem psi, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

        private static IDesktopWallpaper CreateDesktopWallpaper()
        {
            var type = Type.GetTypeFromCLSID(new Guid(ClsidDesktopWallpaper));
            return (IDesktopWallpaper)Activator.CreateInstance(type);
        }

        #endregion

        #region Interop — User32

        private const uint WmSettingChange = 0x001A;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint SpiSetDeskWallpaper = 0x0014;
        private const uint SpifUpdateIniFile = 0x0001;
        private const uint SpifSendChange = 0x0002;
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            UIntPtr wParam,
            [MarshalAs(UnmanagedType.LPWStr)] string lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            [MarshalAs(UnmanagedType.LPWStr)] string pvParam,
            uint fWinIni);

        #endregion

        #region Monitor Mapping

        private static string NormalizeMonitorDevicePath(string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().TrimEnd('\0');

        internal static Dictionary<string, string> BuildMonitorMapFromCcd(
            IEnumerable<DisplayConfigHelper.DisplayConfigInfo> activeDisplays,
            IEnumerable<string> attachedDesktopMonitorIds)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (activeDisplays == null || attachedDesktopMonitorIds == null)
            {
                return map;
            }

            var desktopByPath = attachedDesktopMonitorIds
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new { Original = path, Normalized = NormalizeMonitorDevicePath(path) })
                .Where(item => item.Normalized.Length > 0)
                .GroupBy(item => item.Normalized, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Original, StringComparer.OrdinalIgnoreCase);

            var usedMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in activeDisplays
                .Where(display => display != null && display.IsEnabled && !string.IsNullOrWhiteSpace(display.DeviceName))
                .GroupBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                var targetPaths = group
                    .Select(display => NormalizeMonitorDevicePath(display.MonitorDevicePath))
                    .Where(path => path.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (targetPaths.Count != 1) continue;

                if (!desktopByPath.TryGetValue(targetPaths[0], out string monitorId)) continue;

                if (!usedMonitorIds.Add(monitorId)) continue;

                map[group.Key] = monitorId;
            }

            return map;
        }

        private static DesktopMonitorRectResult ReadDesktopMonitorRect(IDesktopWallpaper dw, string monitorId)
        {
            int hResult = dw.GetMonitorRECT(monitorId, out RECT rect);
            return new DesktopMonitorRectResult(hResult, rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        internal static bool TryGetAttachedDesktopMonitorIdsStrict(
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            out List<string> attached)
        {
            attached = new List<string>();
            if (getMonitorCount == null || getMonitorDevicePathAt == null || getMonitorRect == null)
            {
                return false;
            }

            uint count;
            try
            {
                count = getMonitorCount();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not enumerate the Spotlight wallpaper monitor domain");
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                string monitorId;
                try
                {
                    monitorId = getMonitorDevicePathAt(i);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not resolve Spotlight wallpaper monitor index {i}");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(monitorId) || !seen.Add(monitorId))
                {
                    _logger.Warn($"Spotlight wallpaper monitor index {i} did not resolve to a unique monitor ID");
                    return false;
                }

                DesktopMonitorRectResult rect;
                try
                {
                    rect = getMonitorRect(monitorId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not classify Spotlight wallpaper monitor {monitorId}");
                    return false;
                }

                if (rect.HResult == HResultSFalse)
                {
                    _logger.Debug($"IDesktopWallpaper monitor is definitively detached: {monitorId}");
                    continue;
                }

                if (rect.HResult != HResultSOk)
                {
                    _logger.Warn($"Spotlight wallpaper monitor {monitorId} attachment state is unknown (HRESULT 0x{rect.HResult:X8})");
                    return false;
                }

                if (!rect.HasPositiveArea)
                {
                    _logger.Warn($"Spotlight wallpaper monitor {monitorId} returned S_OK without a valid display rectangle");
                    return false;
                }

                attached.Add(monitorId);
            }

            return true;
        }

        private static bool TryGetAttachedDesktopMonitorIdsStrict(IDesktopWallpaper dw, out List<string> attached) =>
            TryGetAttachedDesktopMonitorIdsStrict(
                () => dw.GetMonitorDevicePathCount(),
                index => dw.GetMonitorDevicePathAt(index),
                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                out attached);

        internal static bool TryResolveCurrentActiveSpotlightMonitorIds(
            IEnumerable<DisplayConfigHelper.DisplayConfigInfo> currentDisplays,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            out List<string> monitorIds)
        {
            monitorIds = new List<string>();
            if (currentDisplays == null || getMonitorCount == null || getMonitorDevicePathAt == null || getMonitorRect == null)
            {
                return false;
            }

            var expectedOrder = new List<string>();
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var display in currentDisplays.Where(display => display != null && display.IsEnabled))
            {
                string expectedPath = NormalizeMonitorDevicePath(display.MonitorDevicePath);
                if (expectedPath.Length == 0)
                {
                    _logger.Warn($"Spotlight current-active domain is incomplete for {display.DeviceName}: CCD monitor path is unavailable");
                    return false;
                }

                if (!expected.Add(expectedPath))
                {
                    _logger.Warn($"Spotlight current-active domain is ambiguous: CCD monitor path '{expectedPath}' is not unique");
                    return false;
                }

                expectedOrder.Add(expectedPath);
            }

            if (expectedOrder.Count == 0)
            {
                _logger.Warn("Spotlight current-active domain is empty: no enabled CCD targets are available");
                return false;
            }

            uint count;
            try
            {
                count = getMonitorCount();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not enumerate wallpaper monitor identities for the current-active Spotlight domain");
                return false;
            }

            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                string rawMonitorId;
                try
                {
                    rawMonitorId = getMonitorDevicePathAt(i);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Ignoring unaddressable historical wallpaper monitor index {i}");
                    continue;
                }

                string monitorId = NormalizeMonitorDevicePath(rawMonitorId);
                if (monitorId.Length == 0)
                {
                    _logger.Debug($"Ignoring blank historical wallpaper monitor index {i}");
                    continue;
                }

                if (expected.Contains(monitorId))
                {
                    if (resolved.ContainsKey(monitorId)) continue;

                    DesktopMonitorRectResult liveRect;
                    try
                    {
                        liveRect = getMonitorRect(monitorId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Could not classify expected live Spotlight monitor {monitorId}");
                        return false;
                    }

                    if (liveRect.HResult != HResultSOk || !liveRect.HasPositiveArea)
                    {
                        string classification = liveRect.HResult == HResultSFalse
                            ? "detached"
                            : $"unavailable (HRESULT 0x{liveRect.HResult:X8})";
                        _logger.Warn($"Expected live Spotlight monitor {monitorId} is {classification} or has an invalid rectangle");
                        return false;
                    }

                    resolved[monitorId] = monitorId;
                    continue;
                }

                DesktopMonitorRectResult extraRect;
                try
                {
                    extraRect = getMonitorRect(monitorId);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Ignoring unclassifiable historical wallpaper monitor {monitorId}");
                    continue;
                }

                if (extraRect.HResult == HResultSOk && extraRect.HasPositiveArea)
                {
                    _logger.Warn($"Spotlight monitor authority mismatch: attached wallpaper monitor {monitorId} is absent from the enabled CCD target set");
                    return false;
                }

                if (extraRect.HResult == HResultSFalse)
                    _logger.Debug($"Ignoring detached historical wallpaper monitor {monitorId}");
                else
                    _logger.Debug($"Ignoring historical wallpaper monitor {monitorId} with non-authoritative attachment state 0x{extraRect.HResult:X8}");
            }

            if (resolved.Count != expected.Count)
            {
                var missing = expectedOrder.Where(path => !resolved.ContainsKey(path));
                _logger.Warn($"Spotlight current-active domain is incomplete: unresolved CCD monitor paths {string.Join(", ", missing)}");
                return false;
            }

            foreach (string expectedPath in expectedOrder)
                monitorIds.Add(resolved[expectedPath]);

            return monitorIds.Count == expectedOrder.Count && monitorIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == monitorIds.Count;
        }

        private static List<string> GetAttachedDesktopMonitorIds(IDesktopWallpaper dw)
        {
            var attached = new List<string>();
            uint count = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    string monitorId = dw.GetMonitorDevicePathAt(i);
                    var rect = ReadDesktopMonitorRect(dw, monitorId);
                    if (rect.HResult == HResultSOk && rect.HasPositiveArea)
                        attached.Add(monitorId);
                    else if (rect.HResult == HResultSFalse)
                        _logger.Debug($"IDesktopWallpaper monitor is detached: {monitorId}");
                    else
                        _logger.Debug($"IDesktopWallpaper monitor {i} attachment state is unavailable (HRESULT 0x{rect.HResult:X8})");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"IDesktopWallpaper monitor {i} is not currently attached");
                }
            }

            return attached;
        }

        private static Dictionary<string, string> BuildMonitorMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dw = CreateDesktopWallpaper();
                var desktopMonitorIds = GetAttachedDesktopMonitorIds(dw);
                var displayConfigs = DisplayConfigHelper.GetDisplayConfigs();
                map = BuildMonitorMapFromCcd(displayConfigs, desktopMonitorIds);

                foreach (var display in displayConfigs.Where(display => display.IsEnabled))
                {
                    if (string.IsNullOrWhiteSpace(display.DeviceName)) continue;

                    if (map.TryGetValue(display.DeviceName, out string monitorId))
                    {
                        _logger.Debug($"Wallpaper CCD identity: {display.DeviceName} target {display.TargetId} -> {display.MonitorDevicePath} -> {monitorId}");
                    }
                    else
                    {
                        _logger.Warn($"Wallpaper CCD identity unresolved for {display.DeviceName} target {display.TargetId} path '{display.MonitorDevicePath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "BuildMonitorMap failed");
            }

            if (map.Count == 0)
                _logger.Warn("Wallpaper monitor map is empty -> every picture assignment will be unavailable");

            return map;
        }

        #endregion

        #region Windows Wallpaper State

        private const string WallpapersSubkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
        private const string BackgroundTypeValue = "BackgroundType";
        private const int BackgroundTypePicture = 0;
        private const int BackgroundTypeSolid = 1;
        private const int BackgroundTypeSlideshow = 2;
        private const int BackgroundTypeSpotlight = 3;

        private const string DesktopSpotlightSubkey = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";
        private const string DesktopSpotlightValue = "EnabledState";
        private const string ControlPanelDesktopSubkey = @"Control Panel\Desktop";
        private const string WallpaperValue = "Wallpaper";
        private const string BackgroundAppsSubkey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        private const string BackgroundAppsDisabled = "GlobalUserDisabled";

        private const string SpotlightBuiltInMarker = @"MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images";
        private const string SpotlightIrisMarker = @"MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService";
        private const string SpotlightPackageName = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy";

        private static string SpotlightIrisRoot => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages",
            SpotlightPackageName,
            "LocalCache",
            "Microsoft",
            "IrisService");

        private static string SpotlightBuiltInRoot => System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty,
            "SystemApps",
            SpotlightPackageName,
            "DesktopSpotlight",
            "Assets",
            "Images");

        private static int? HkcuDword(string subkey, string value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey))
                {
                    if (key?.GetValue(value) is int dword)
                    {
                        return dword;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
            }

            return null;
        }

        private static string HkcuString(string subkey, string value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey))
                    return key?.GetValue(value) as string;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
                return null;
            }
        }

        private static bool SetHkcuDword(string subkey, string value, int data, bool create)
        {
            try
            {
                using (var key = create
                    ? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subkey)
                    : Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey, true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    key.SetValue(value, data, Microsoft.Win32.RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry write failed for {subkey}\\{value}");
                return false;
            }
        }

        private static bool SetHkcuString(string subkey, string value, string data)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subkey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    key.SetValue(value, data ?? string.Empty, Microsoft.Win32.RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry write failed for {subkey}\\{value}");
                return false;
            }
        }

        private static bool TryGetHkcuString(string subkey, string value, out bool exists, out string data)
        {
            exists = false;
            data = null;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    object raw = key.GetValue(value, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw == null)
                    {
                        return true;
                    }
                    if (!(raw is string text))
                    {
                        return false;
                    }

                    exists = true;
                    data = text;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
                return false;
            }
        }

        private static bool DeleteHkcuValue(string subkey, string value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey, true))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    key.DeleteValue(value, throwOnMissingValue: false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry delete failed for {subkey}\\{value}");
                return false;
            }
        }
        internal static bool IsSpotlightProviderPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.IndexOf(SpotlightBuiltInMarker, StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf(SpotlightIrisMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                string fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsVerifiedSpotlightProviderPath(string path) =>
            IsPathUnderRoot(path, SpotlightIrisRoot) || IsPathUnderRoot(path, SpotlightBuiltInRoot);

        internal static string SelectSpotlightRepaintPath(
            IEnumerable<string> currentPaths,
            IEnumerable<string> irisCandidates,
            IEnumerable<string> builtInCandidates,
            Func<string, bool> isUsableImage) =>
            SelectSpotlightRepaintPath(currentPaths, irisCandidates, builtInCandidates, isUsableImage, isUsableImage);

        internal static string SelectSpotlightRepaintPath(
            IEnumerable<string> currentPaths,
            IEnumerable<string> irisCandidates,
            IEnumerable<string> builtInCandidates,
            Func<string, bool> isUsableImage,
            Func<string, bool> isLandscapeImage)
        {
            if (isUsableImage == null || isLandscapeImage == null)
            {
                return null;
            }

            foreach (string path in currentPaths ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(path) && IsVerifiedSpotlightProviderPath(path) && isUsableImage(path))
                {
                    return path;
                }
            }

            foreach (var source in new[] { irisCandidates, builtInCandidates })
            {
                foreach (string path in source ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(path) && IsVerifiedSpotlightProviderPath(path) && isLandscapeImage(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static bool IsUsableSpotlightProviderImage(string path)
        {
            if (!IsVerifiedSpotlightProviderPath(path) || !System.IO.File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                {
                    var frame = BitmapFrame.Create(
                        stream,
                        BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);
                    return frame.PixelWidth > 0 && frame.PixelHeight > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Spotlight provider image is not usable: {path}");
                return false;
            }
        }

        internal static bool IsLandscapeImageDimensions(int pixelWidth, int pixelHeight) => pixelWidth > 0 && pixelHeight > 0 && pixelWidth >= pixelHeight;

        private static bool IsLandscapeSpotlightProviderImage(string path)
        {
            if (!IsVerifiedSpotlightProviderPath(path) || !System.IO.File.Exists(path))
            {
                return false;
            }

            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                {
                    var frame = BitmapFrame.Create(
                        stream,
                        BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);
                    return IsLandscapeImageDimensions(frame.PixelWidth, frame.PixelHeight);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Spotlight provider image is not a usable landscape image: {path}");
                return false;
            }
        }

        private static List<string> EnumerateSpotlightProviderImages(string root, bool newestFirst)
        {
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Directory.Exists(root))
            {
                return new List<string>();
            }

            try
            {
                var files = new System.IO.DirectoryInfo(root)
                    .EnumerateFiles("*", System.IO.SearchOption.AllDirectories)
                    .Where(file => IsSpotlightProviderPath(file.FullName));

                files = newestFirst
                    ? files.OrderByDescending(file => file.LastWriteTimeUtc).ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    : files.OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase);

                return files.Select(file => file.FullName).ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Could not enumerate Spotlight provider images under {root}");
                return new List<string>();
            }
        }

        internal static WallpaperMode ClassifyWallpaperMode(
            int? backgroundType,
            bool slideshowActive,
            bool hasPicture,
            bool hasSpotlightProviderPath)
        {
            switch (backgroundType)
            {
                case BackgroundTypePicture: return WallpaperMode.Picture;
                case BackgroundTypeSolid: return WallpaperMode.Solid;
                case BackgroundTypeSlideshow: return WallpaperMode.Slideshow;
                case BackgroundTypeSpotlight: return WallpaperMode.Spotlight;
            }

            if (hasSpotlightProviderPath)
            {
                return WallpaperMode.Spotlight;
            }
            if (slideshowActive)
            {
                return WallpaperMode.Slideshow;
            }
            return hasPicture ? WallpaperMode.Picture : WallpaperMode.Solid;
        }

        private static WallpaperMode DetectCurrentMode(IDesktopWallpaper dw)
        {
            int? backgroundType = HkcuDword(WallpapersSubkey, BackgroundTypeValue);
            bool slideshowActive = SafeGetStatus(dw).HasFlag(DesktopSlideshowState.Slideshow);
            string liveWallpaper = HkcuString(ControlPanelDesktopSubkey, WallpaperValue);
            return ClassifyWallpaperMode(
                backgroundType,
                slideshowActive,
                !string.IsNullOrEmpty(liveWallpaper),
                IsSpotlightProviderPath(liveWallpaper));
        }

        internal static bool TryDetectCurrentModeForApply(
            int? backgroundType,
            string liveWallpaper,
            Func<uint> readStatus,
            out WallpaperMode mode)
        {
            mode = WallpaperMode.Unknown;
            if (!TryGetSlideshowActive(readStatus, out bool slideshowActive))
            {
                return false;
            }

            mode = ClassifyWallpaperMode(
                backgroundType,
                slideshowActive,
                !string.IsNullOrEmpty(liveWallpaper),
                IsSpotlightProviderPath(liveWallpaper));
            return true;
        }

        private static bool TryDetectCurrentModeForApply(IDesktopWallpaper dw, out WallpaperMode mode)
        {
            return TryDetectCurrentModeForApply(
                HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                HkcuString(ControlPanelDesktopSubkey, WallpaperValue),
                () => (uint)dw.GetStatus(),
                out mode);
        }

        private static string GetCurrentSpotlightWallpaperPath()
        {
            var path = HkcuString(ControlPanelDesktopSubkey, WallpaperValue);
            return IsSpotlightProviderPath(path) && System.IO.File.Exists(path) ? path : null;
        }

        #endregion

        #region Capture

        public static WallpaperSettings Capture()
        {
            var snapshot = new WallpaperSettings();

            try
            {
                var dw = CreateDesktopWallpaper();
                uint backgroundColor = SafeGetBackgroundColor(dw);
                snapshot.Position = PositionToString(SafeGetPosition(dw));

                int? backgroundType = HkcuDword(WallpapersSubkey, BackgroundTypeValue);
                bool slideshowActive = SafeGetStatus(dw).HasFlag(DesktopSlideshowState.Slideshow);
                string liveWallpaper = HkcuString(ControlPanelDesktopSubkey, WallpaperValue);
                var monitorMap = BuildMonitorMap();
                var capturedPictures = new Dictionary<string, MonitorWallpaper>(StringComparer.OrdinalIgnoreCase);
                bool providerPathSeen = IsSpotlightProviderPath(liveWallpaper);

                foreach (var kvp in monitorMap)
                {
                    try
                    {
                        string path = dw.GetWallpaper(kvp.Value);
                        if (string.IsNullOrEmpty(path)) continue;

                        providerPathSeen |= IsSpotlightProviderPath(path);
                        capturedPictures[kvp.Key] = new MonitorWallpaper
                        {
                            Path = path,
                            MonitorId = kvp.Value
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"GetWallpaper failed for {kvp.Key}");
                    }
                }

                snapshot.Mode = ClassifyWallpaperMode(
                    backgroundType,
                    slideshowActive,
                    capturedPictures.Count > 0,
                    providerPathSeen);
                if (ModeOwnsBackgroundColor(snapshot.Mode)) snapshot.SolidColorArgb = backgroundColor;

                switch (snapshot.Mode)
                {
                    case WallpaperMode.Picture:
                        foreach (var kvp in capturedPictures)
                            snapshot.PerMonitor[kvp.Key] = kvp.Value;
                        _logger.Info($"Wallpaper capture: Picture mode, {TextHelper.Plural(snapshot.PerMonitor.Count, "monitor")}");
                        break;
                    case WallpaperMode.Slideshow:
                        CaptureSlideshow(dw, snapshot);
                        _logger.Info("Wallpaper capture: Slideshow mode");
                        break;
                    case WallpaperMode.Spotlight:
                        _logger.Info("Wallpaper capture: Spotlight mode");
                        break;
                    default:
                        snapshot.Mode = WallpaperMode.Solid;
                        _logger.Info("Wallpaper capture: Solid Color mode");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Wallpaper capture failed");
                snapshot.Mode = WallpaperMode.Unknown;
            }

            return snapshot;
        }

        private static void CaptureSlideshow(IDesktopWallpaper dw, WallpaperSettings snapshot)
        {
            try
            {
                dw.GetSlideshowOptions(out var options, out uint tick);
                uint seconds = tick / 1000;
                if (!_standardIntervalSeconds.Contains(seconds))
                    _logger.Warn($"Slideshow interval reads {seconds}s, which Windows does not offer -> capturing it as-is");

                snapshot.SlideshowConfig = new SlideshowConfig
                {
                    IntervalSeconds = seconds,
                    Shuffle = options.HasFlag(DesktopSlideshowOptions.ShuffleImages),
                    SourcePaths = ReadSlideshowSourcePaths(dw),
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GetSlideshowOptions failed -> capturing without interval or shuffle");
                snapshot.SlideshowConfig = new SlideshowConfig();
            }
        }

        private static List<string> ReadSlideshowSourcePaths(IDesktopWallpaper dw)
        {
            var paths = new List<string>();

            try
            {
                var items = dw.GetSlideshow();
                if (items == null)
                {
                    return paths;
                }

                items.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    items.GetItemAt(i, out IShellItem item);
                    if (item == null) continue;

                    item.GetDisplayName(SigdnFileSysPath, out string path);
                    if (!string.IsNullOrEmpty(path))
                        paths.Add(path);

                    Marshal.ReleaseComObject(item);
                }

                Marshal.ReleaseComObject(items);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GetSlideshow failed -> capturing without source folder");
            }

            return paths;
        }

        private static DesktopWallpaperPosition SafeGetPosition(IDesktopWallpaper dw)
        {
            try
            {
                return dw.GetPosition();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GetPosition failed -> defaulting to Fill");
                return DesktopWallpaperPosition.Fill;
            }
        }

        private static uint SafeGetBackgroundColor(IDesktopWallpaper dw)
        {
            try
            {
                return dw.GetBackgroundColor();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GetBackgroundColor failed -> defaulting to black");
                return 0;
            }
        }

        private static DesktopSlideshowState SafeGetStatus(IDesktopWallpaper dw)
        {
            try
            {
                return dw.GetStatus();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GetStatus failed");
                return 0;
            }
        }

        internal static bool TryGetSlideshowActive(Func<uint> readStatus, out bool slideshowActive)
        {
            slideshowActive = false;
            if (readStatus == null)
            {
                return false;
            }

            try
            {
                slideshowActive = (readStatus() & (uint)DesktopSlideshowState.Slideshow) != 0;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "GetStatus failed");
                return false;
            }
        }

        private static bool? GetSlideshowActiveForOwnership(IDesktopWallpaper dw)
        {
            return TryGetSlideshowActive(() => (uint)dw.GetStatus(), out bool slideshowActive)
                ? slideshowActive
                : (bool?)null;
        }

        #endregion

        #region Apply State Machine

        internal enum WallpaperTransitionStep
        {
            DeactivateSpotlight,
            SetBackgroundColor,
            SupersedeSlideshowOwnership,
            SetPosition,
            DisableDesktopBackground,
            SetPictureWallpapers,
            SetSlideshowSource,
            SetSlideshowOptions,
            EstablishDestinationMode,
            NotifySettings,
            VerifyDestinationMode,
            ActivateSpotlight,
        }

        internal enum WallpaperApplyOutcome
        {
            Success,
            Partial,
            Failed,
        }

        internal sealed class WallpaperTransitionPlan
        {
            public WallpaperMode SourceMode { get; }
            public WallpaperMode DestinationMode { get; }
            public IReadOnlyList<WallpaperTransitionStep> Steps { get; }

            public WallpaperTransitionPlan(WallpaperMode sourceMode, WallpaperMode destinationMode, IReadOnlyList<WallpaperTransitionStep> steps)
            {
                SourceMode = sourceMode;
                DestinationMode = destinationMode;
                Steps = steps;
            }
        }

        internal sealed class PictureAssignment
        {
            public string DeviceName { get; }
            public string MonitorId { get; }
            public string Path { get; }

            public PictureAssignment(string deviceName, string monitorId, string path)
            {
                DeviceName = deviceName;
                MonitorId = monitorId;
                Path = path;
            }
        }

        internal sealed class PictureRollbackAssignment
        {
            public string MonitorId { get; }
            public string Path { get; }

            public PictureRollbackAssignment(string monitorId, string path)
            {
                MonitorId = monitorId;
                Path = path;
            }
        }

        internal sealed class PictureRollbackSnapshot
        {
            public List<PictureRollbackAssignment> Assignments { get; } = new List<PictureRollbackAssignment>();
        }

        internal sealed class PicturePreflightResult
        {
            public List<PictureAssignment> Assignments { get; } = new List<PictureAssignment>();
            public int RequestedCount { get; internal set; }
            public int DisconnectedCount { get; internal set; }
            public int MissingCount { get; internal set; }
            public bool HasViableAssignments => Assignments.Count > 0;
            public bool HasUnavailableLiveAssignments => DisconnectedCount > 0 || MissingCount > 0;
        }

        private sealed class SlideshowSourceHandle : IDisposable
        {
            public string Folder { get; }
            public IShellItem Item { get; }
            public IShellItemArray Array { get; }

            public SlideshowSourceHandle(string folder, IShellItem item, IShellItemArray array)
            {
                Folder = folder;
                Item = item;
                Array = array;
            }

            public void Dispose()
            {
                if (Array != null) Marshal.ReleaseComObject(Array);
                if (Item != null) Marshal.ReleaseComObject(Item);
            }
        }

        internal static bool ModeOwnsBackgroundColor(WallpaperMode mode) => mode == WallpaperMode.Solid || mode == WallpaperMode.Picture || mode == WallpaperMode.Slideshow;

        internal static WallpaperTransitionPlan BuildTransitionPlan(WallpaperMode sourceMode, WallpaperMode destinationMode)
        {
            if (destinationMode == WallpaperMode.Unknown)
            {
                return new WallpaperTransitionPlan(sourceMode, destinationMode, Array.Empty<WallpaperTransitionStep>());
            }

            var steps = new List<WallpaperTransitionStep>();
            if (destinationMode == WallpaperMode.Spotlight)
            {
                steps.Add(WallpaperTransitionStep.ActivateSpotlight);
                return new WallpaperTransitionPlan(sourceMode, destinationMode, steps);
            }

            steps.Add(WallpaperTransitionStep.DeactivateSpotlight);
            steps.Add(WallpaperTransitionStep.SetBackgroundColor);

            if (destinationMode == WallpaperMode.Solid)
            {
                if (sourceMode == WallpaperMode.Slideshow)
                    steps.Add(WallpaperTransitionStep.SupersedeSlideshowOwnership);
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
            }
            else if (destinationMode == WallpaperMode.Picture)
            {
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
                steps.Add(WallpaperTransitionStep.SetPictureWallpapers);
                steps.Add(WallpaperTransitionStep.SetPosition);
            }
            else if (destinationMode == WallpaperMode.Slideshow)
            {
                steps.Add(WallpaperTransitionStep.SetPosition);
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
                steps.Add(WallpaperTransitionStep.SetSlideshowSource);
                steps.Add(WallpaperTransitionStep.SetSlideshowOptions);
            }

            steps.Add(WallpaperTransitionStep.EstablishDestinationMode);
            steps.Add(WallpaperTransitionStep.NotifySettings);
            steps.Add(WallpaperTransitionStep.VerifyDestinationMode);
            return new WallpaperTransitionPlan(sourceMode, destinationMode, steps);
        }

        internal static PicturePreflightResult PreflightPictureAssignments(WallpaperSettings snapshot, IReadOnlyDictionary<string, string> monitorMap, Func<string, bool> fileExists)
        {
            var result = new PicturePreflightResult();
            if (snapshot?.PerMonitor == null || monitorMap == null || fileExists == null)
            {
                return result;
            }

            foreach (var kvp in snapshot.PerMonitor)
            {
                result.RequestedCount++;
                if (!monitorMap.TryGetValue(kvp.Key, out string monitorId) || string.IsNullOrEmpty(monitorId))
                {
                    result.DisconnectedCount++;
                    continue;
                }

                string path = kvp.Value?.Path;
                if (string.IsNullOrEmpty(path) || !fileExists(path))
                {
                    result.MissingCount++;
                    continue;
                }

                result.Assignments.Add(new PictureAssignment(kvp.Key, monitorId, path));
            }

            return result;
        }

        internal static WallpaperApplyOutcome ClassifyPictureApplyOutcome(PicturePreflightResult preflight, int applied, int setterFailures)
        {
            if (preflight == null || applied == 0)
            {
                return WallpaperApplyOutcome.Failed;
            }

            return setterFailures > 0 || preflight.HasUnavailableLiveAssignments
                ? WallpaperApplyOutcome.Partial
                : WallpaperApplyOutcome.Success;
        }

        internal static bool TryCapturePictureRollbackSnapshot(IEnumerable<string> monitorIds, Func<string, string> getWallpaper, out PictureRollbackSnapshot snapshot)
        {
            snapshot = null;
            if (monitorIds == null || getWallpaper == null)
            {
                return false;
            }

            var ids = monitorIds.ToList();
            if (ids.Count == 0)
            {
                return false;
            }

            var captured = new PictureRollbackSnapshot();
            foreach (string monitorId in ids)
            {
                if (string.IsNullOrWhiteSpace(monitorId))
                {
                    return false;
                }

                try
                {
                    string path = getWallpaper(monitorId);
                    if (string.IsNullOrEmpty(path))
                    {
                        _logger.Warn($"Could not capture prior Picture wallpaper for {monitorId}: rendered path is unavailable");
                        return false;
                    }

                    captured.Assignments.Add(new PictureRollbackAssignment(monitorId, path));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not capture prior Picture wallpaper for {monitorId}");
                    return false;
                }
            }

            snapshot = captured;
            return true;
        }

        internal static bool RestorePictureRollbackSnapshot(PictureRollbackSnapshot snapshot, Action<string, string> setWallpaper)
        {
            if (snapshot?.Assignments == null || snapshot.Assignments.Count == 0 || setWallpaper == null)
            {
                return false;
            }

            bool complete = true;
            foreach (var assignment in snapshot.Assignments)
            {
                try
                {
                    setWallpaper(assignment.MonitorId, assignment.Path);
                }
                catch (Exception ex)
                {
                    complete = false;
                    _logger.Warn(ex, $"Could not restore prior Picture wallpaper for {assignment.MonitorId}");
                }
            }

            return complete;
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            IEnumerable<DisplayConfigHelper.DisplayConfigInfo> currentDisplays,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<IReadOnlyList<string>, WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null ||
                !TryResolveCurrentActiveSpotlightMonitorIds(
                    currentDisplays,
                    getMonitorCount,
                    getMonitorDevicePathAt,
                    getMonitorRect,
                    out List<string> currentActiveMonitorIds))
            {
                _logger.Warn("Spotlight apply aborted before mutation: the current-active wallpaper monitor domain could not be established authoritatively.");
                return WallpaperApplyOutcome.Failed;
            }

            return PrepareSpotlightDestinationApply(
                sourceMode,
                currentActiveMonitorIds,
                getWallpaper,
                rememberPictureRollback,
                () => activateSpotlight(currentActiveMonitorIds));
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<IReadOnlyList<string>, WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null ||
                !TryGetAttachedDesktopMonitorIdsStrict(
                    getMonitorCount,
                    getMonitorDevicePathAt,
                    getMonitorRect,
                    out List<string> attachedMonitorIds) ||
                attachedMonitorIds.Count == 0)
            {
                _logger.Warn("Spotlight apply aborted before mutation: the attached wallpaper monitor domain could not be established authoritatively.");
                return WallpaperApplyOutcome.Failed;
            }

            return PrepareSpotlightDestinationApply(
                sourceMode,
                attachedMonitorIds,
                getWallpaper,
                rememberPictureRollback,
                () => activateSpotlight(attachedMonitorIds));
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            IEnumerable<string> attachedMonitorIds,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            if (sourceMode != WallpaperMode.Picture)
            {
                return activateSpotlight();
            }

            if (rememberPictureRollback == null || !TryCapturePictureRollbackSnapshot(attachedMonitorIds, getWallpaper, out PictureRollbackSnapshot snapshot))
            {
                _logger.Warn("Picture -> Spotlight aborted before mutation: exact per-monitor Picture rollback state could not be captured");
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                rememberPictureRollback(snapshot);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Picture -> Spotlight aborted before mutation: Picture rollback state could not be retained.");
                return WallpaperApplyOutcome.Failed;
            }

            return activateSpotlight();
        }

        internal static string SelectSlideshowSource(IEnumerable<string> paths, Func<string, bool> directoryExists)
        {
            if (paths == null || directoryExists == null)
            {
                return null;
            }

            return paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && directoryExists(path));
        }

        internal static WallpaperApplyOutcome ExecuteTransitionSteps(
            WallpaperTransitionPlan plan,
            Func<WallpaperTransitionStep, WallpaperApplyOutcome> executeStep)
        {
            if (plan == null || executeStep == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            var finalOutcome = WallpaperApplyOutcome.Success;
            foreach (var step in plan.Steps)
            {
                var stepOutcome = executeStep(step);
                if (stepOutcome == WallpaperApplyOutcome.Failed)
                {
                    return WallpaperApplyOutcome.Failed;
                }
                if (stepOutcome == WallpaperApplyOutcome.Partial)
                    finalOutcome = WallpaperApplyOutcome.Partial;
            }

            return finalOutcome;
        }

        public static bool Apply(WallpaperSettings snapshot)
        {
            if (snapshot == null || snapshot.Mode == WallpaperMode.Unknown)
            {
                return false;
            }

            IDesktopWallpaper dw = null;
            SlideshowSourceHandle slideshowSource = null;
            WallpaperMode sourceMode = WallpaperMode.Unknown;
            bool mutationStarted = false;
            uint priorColor = 0;
            DesktopWallpaperPosition priorPosition = DesktopWallpaperPosition.Fill;
            IShellItemArray priorSlideshowSource = null;
            DesktopSlideshowOptions priorSlideshowOptions = DesktopSlideshowOptions.None;
            uint priorSlideshowTick = 0;
            bool legacyWallpaperCaptured = false;
            bool priorLegacyWallpaperExists = false;
            string priorLegacyWallpaper = null;
            PictureRollbackSnapshot priorPictureRollback = null;

            try
            {
                dw = CreateDesktopWallpaper();
                if (!TryDetectCurrentModeForApply(dw, out sourceMode))
                {
                    _logger.Warn("Wallpaper apply aborted before mutation: Slideshow ownership status could not be established");
                    return false;
                }
                priorColor = SafeGetBackgroundColor(dw);
                priorPosition = SafeGetPosition(dw);

                if ((snapshot.Mode == WallpaperMode.Spotlight && sourceMode != WallpaperMode.Spotlight) ||
                    (sourceMode == WallpaperMode.Slideshow && snapshot.Mode == WallpaperMode.Solid))
                {
                    if (!TryGetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, out priorLegacyWallpaperExists, out priorLegacyWallpaper))
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} aborted before mutation: prior legacy wallpaper registration could not be captured for rollback.");
                        return false;
                    }
                    legacyWallpaperCaptured = true;
                }

                if (sourceMode == WallpaperMode.Slideshow && snapshot.Mode != WallpaperMode.Slideshow)
                {
                    try
                    {
                        priorSlideshowSource = dw.GetSlideshow();
                        if (priorSlideshowSource != null)
                            dw.GetSlideshowOptions(out priorSlideshowOptions, out priorSlideshowTick);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Could not capture prior Slideshow ownership for rollback");
                    }

                    if ((snapshot.Mode == WallpaperMode.Solid || snapshot.Mode == WallpaperMode.Spotlight) && priorSlideshowSource == null)
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} aborted before mutation: prior Slideshow ownership could not be captured for rollback.");
                        return false;
                    }
                }

                PicturePreflightResult picturePreflight = null;
                if (snapshot.Mode == WallpaperMode.Picture)
                {
                    var monitorMap = BuildMonitorMap();
                    picturePreflight = PreflightPictureAssignments(snapshot, monitorMap, System.IO.File.Exists);
                    if (!picturePreflight.HasViableAssignments)
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> Picture aborted before mutation: no viable live picture assignments ({picturePreflight.DisconnectedCount} disconnected, {picturePreflight.MissingCount} missing).");
                        return false;
                    }
                }
                else if (snapshot.Mode == WallpaperMode.Slideshow)
                {
                    if (!TryPrepareSlideshowSource(snapshot.SlideshowConfig?.SourcePaths, out slideshowSource))
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> Slideshow aborted before mutation: no resolvable slideshow source.");
                        return false;
                    }
                }

                var plan = BuildTransitionPlan(sourceMode, snapshot.Mode);
                _logger.Info($"Wallpaper transition: {sourceMode} -> {snapshot.Mode}");

                var outcome = ExecuteTransitionSteps(plan, step =>
                {
                    if (step != WallpaperTransitionStep.NotifySettings &&
                        step != WallpaperTransitionStep.VerifyDestinationMode &&
                        step != WallpaperTransitionStep.ActivateSpotlight)
                    {
                        mutationStarted = true;
                    }

                    switch (step)
                    {
                        case WallpaperTransitionStep.DeactivateSpotlight:
                            return DeactivateSpotlight();
                        case WallpaperTransitionStep.SetBackgroundColor:
                            return ApplyBackgroundColor(dw, snapshot.SolidColorArgb);
                        case WallpaperTransitionStep.SupersedeSlideshowOwnership:
                            return SupersedeSlideshowOwnership(dw);
                        case WallpaperTransitionStep.SetPosition:
                            return ApplyPosition(dw, snapshot.Position);
                        case WallpaperTransitionStep.DisableDesktopBackground:
                            dw.Enable(false);
                            return WallpaperApplyOutcome.Success;
                        case WallpaperTransitionStep.SetPictureWallpapers:
                            return ApplyPictureWallpapers(dw, picturePreflight);
                        case WallpaperTransitionStep.SetSlideshowSource:
                            return ApplySlideshowSource(dw, slideshowSource)
                                ? WallpaperApplyOutcome.Success
                                : WallpaperApplyOutcome.Failed;
                        case WallpaperTransitionStep.SetSlideshowOptions:
                            return ApplySlideshowOptions(dw, snapshot.SlideshowConfig);
                        case WallpaperTransitionStep.EstablishDestinationMode:
                            return EstablishDestinationMode(snapshot.Mode);
                        case WallpaperTransitionStep.NotifySettings:
                            NotifyWallpaperSettingsChanged();
                            return WallpaperApplyOutcome.Success;
                        case WallpaperTransitionStep.VerifyDestinationMode:
                            if (snapshot.Mode == WallpaperMode.Slideshow)
                            {
                                return VerifySlideshowOwnershipSettled(
                                    () => HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                                    () => GetSlideshowActiveForOwnership(dw),
                                    milliseconds => System.Threading.Thread.Sleep(milliseconds));
                            }

                            return VerifyDestinationOwnership(
                                snapshot.Mode,
                                () => DetectCurrentMode(dw),
                                () => GetSlideshowActiveForOwnership(dw));
                        case WallpaperTransitionStep.ActivateSpotlight:
                            return PrepareSpotlightDestinationApply(
                                sourceMode,
                                DisplayConfigHelper.GetDisplayConfigs(),
                                () => dw.GetMonitorDevicePathCount(),
                                index => dw.GetMonitorDevicePathAt(index),
                                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                                monitorId => dw.GetWallpaper(monitorId),
                                captured => priorPictureRollback = captured,
                                monitorIds =>
                                {
                                    mutationStarted = true;
                                    return ActivateSpotlightDestination(dw, monitorIds);
                                });
                        default:
                            return WallpaperApplyOutcome.Failed;
                    }
                });

                if (outcome == WallpaperApplyOutcome.Failed)
                {
                    if (mutationStarted)
                        RecoverPriorOwnerAfterFailure(() => RestorePriorOwner(dw, sourceMode, priorColor, priorPosition, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick, legacyWallpaperCaptured, priorLegacyWallpaperExists, priorLegacyWallpaper, priorPictureRollback));
                    _logger.Error($"Wallpaper transition {sourceMode} -> {snapshot.Mode} failed before the destination was fully established.");
                    return false;
                }

                if (outcome == WallpaperApplyOutcome.Partial)
                {
                    _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} partially applied; one or more represented destination properties could not be established.");
                    return false;
                }

                _logger.Info($"Wallpaper applied: {WallpaperModeNames.Display(snapshot.Mode)}");
                return true;
            }
            catch (Exception ex)
            {
                if (mutationStarted && dw != null)
                    RecoverPriorOwnerAfterFailure(() => RestorePriorOwner(dw, sourceMode, priorColor, priorPosition, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick, legacyWallpaperCaptured, priorLegacyWallpaperExists, priorLegacyWallpaper, priorPictureRollback));
                _logger.Error(ex, "Wallpaper apply failed");
                return false;
            }
            finally
            {
                slideshowSource?.Dispose();
                if (priorSlideshowSource != null)
                    Marshal.ReleaseComObject(priorSlideshowSource);
            }
        }

        internal static WallpaperApplyOutcome ApplyBackgroundColor(Action setBackgroundColor)
        {
            if (setBackgroundColor == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                setBackgroundColor();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetBackgroundColor failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        private static WallpaperApplyOutcome ApplyBackgroundColor(IDesktopWallpaper dw, uint color)
        {
            return ApplyBackgroundColor(() => dw.SetBackgroundColor(color));
        }

        internal static WallpaperApplyOutcome ApplyPosition(Action setPosition)
        {
            if (setPosition == null)
            {
                return WallpaperApplyOutcome.Partial;
            }

            try
            {
                setPosition();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetPosition failed");
                return WallpaperApplyOutcome.Partial;
            }
        }

        private static WallpaperApplyOutcome ApplyPosition(IDesktopWallpaper dw, string position)
        {
            var outcome = ApplyPosition(() => dw.SetPosition(StringToPosition(position)));
            if (outcome == WallpaperApplyOutcome.Success)
                _logger.Debug($"Wallpaper position set to '{position}'");
            return outcome;
        }

        private static WallpaperApplyOutcome ApplyPictureWallpapers(IDesktopWallpaper dw, PicturePreflightResult preflight)
        {
            if (preflight == null || !preflight.HasViableAssignments)
            {
                return WallpaperApplyOutcome.Failed;
            }

            int applied = 0;
            int setterFailures = 0;
            foreach (var assignment in preflight.Assignments)
            {
                try
                {
                    dw.SetWallpaper(assignment.MonitorId, assignment.Path);
                    applied++;
                }
                catch (Exception ex)
                {
                    setterFailures++;
                    _logger.Warn(ex, $"SetWallpaper failed for {assignment.DeviceName}");
                }
            }

            _logger.Info($"Picture wallpaper assignment: {applied} applied, {setterFailures} setter failures, {preflight.DisconnectedCount} disconnected, {preflight.MissingCount} missing");
            return ClassifyPictureApplyOutcome(preflight, applied, setterFailures);
        }

        private static bool TryPrepareSlideshowSource(List<string> paths, out SlideshowSourceHandle handle)
        {
            handle = null;
            string folder = SelectSlideshowSource(paths, System.IO.Directory.Exists);
            if (folder == null)
            {
                return false;
            }

            IShellItem item = null;
            IShellItemArray array = null;
            try
            {
                var itemGuid = typeof(IShellItem).GUID;
                var arrayGuid = typeof(IShellItemArray).GUID;
                SHCreateItemFromParsingName(folder, IntPtr.Zero, ref itemGuid, out item);
                SHCreateShellItemArrayFromShellItem(item, ref arrayGuid, out array);
                handle = new SlideshowSourceHandle(folder, item, array);
                return true;
            }
            catch (Exception ex)
            {
                if (array != null) Marshal.ReleaseComObject(array);
                if (item != null) Marshal.ReleaseComObject(item);
                _logger.Warn(ex, $"Could not resolve slideshow source {folder}");
                return false;
            }
        }

        private static bool ApplySlideshowSource(IDesktopWallpaper dw, SlideshowSourceHandle source)
        {
            if (source?.Array == null)
            {
                return false;
            }

            try
            {
                dw.SetSlideshow(source.Array);
                _logger.Debug($"Slideshow source set to {source.Folder}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"SetSlideshow failed for {source.Folder}");
                return false;
            }
        }

        internal static WallpaperApplyOutcome ApplySlideshowOptions(Action setOptions)
        {
            if (setOptions == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                setOptions();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetSlideshowOptions failed");
                return WallpaperApplyOutcome.Partial;
            }
        }

        private static WallpaperApplyOutcome ApplySlideshowOptions(IDesktopWallpaper dw, SlideshowConfig config)
        {
            if (config == null)
            {
                _logger.Warn("Wallpaper apply: Slideshow mode has no config");
                return WallpaperApplyOutcome.Failed;
            }

            var options = config.Shuffle
                ? DesktopSlideshowOptions.ShuffleImages
                : DesktopSlideshowOptions.None;
            var outcome = ApplySlideshowOptions(() =>
                dw.SetSlideshowOptions(options, config.IntervalSeconds * 1000));
            if (outcome == WallpaperApplyOutcome.Success)
                _logger.Debug($"Slideshow options: {config.IntervalSeconds}s, shuffle {config.Shuffle}");
            return outcome;
        }

        internal static WallpaperApplyOutcome SupersedeSlideshowOwnership(
            Func<bool?> readSlideshowActive,
            Func<bool> establishSolidOwnership)
        {
            if (readSlideshowActive == null || establishSolidOwnership == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool? slideshowActive = readSlideshowActive();
                if (!slideshowActive.HasValue)
                {
                    _logger.Warn("Slideshow ownership status is unavailable while leaving Slideshow");
                    return WallpaperApplyOutcome.Failed;
                }

                if (!slideshowActive.Value)
                {
                    return WallpaperApplyOutcome.Success;
                }

                if (!establishSolidOwnership())
                {
                    _logger.Warn("Could not invoke the classic Solid ownership bridge while leaving Slideshow");
                    return WallpaperApplyOutcome.Failed;
                }

                bool? slideshowStillActive = readSlideshowActive();
                if (!slideshowStillActive.HasValue || slideshowStillActive.Value)
                {
                    _logger.Warn("Slideshow ownership was not authoritatively cleared by the classic Solid ownership bridge");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not supersede Slideshow ownership");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static bool ApplyClassicSolidOwnershipBridge(
            Func<bool> writeEmptyLegacyWallpaper,
            Func<bool> applyEmptyLegacyWallpaper)
        {
            if (writeEmptyLegacyWallpaper == null || applyEmptyLegacyWallpaper == null)
            {
                return false;
            }

            try
            {
                return writeEmptyLegacyWallpaper() && applyEmptyLegacyWallpaper();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Classic Solid ownership bridge failed");
                return false;
            }
        }

        private static bool ApplyClassicSolidOwnershipBridge() =>
            ApplyClassicSolidOwnershipBridge(
                () => SetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, string.Empty),
                () => SystemParametersInfo(
                    SpiSetDeskWallpaper,
                    0,
                    string.Empty,
                    SpifUpdateIniFile | SpifSendChange));

        private static WallpaperApplyOutcome SupersedeSlideshowOwnership(IDesktopWallpaper dw) =>
            SupersedeSlideshowOwnership(
                () => GetSlideshowActiveForOwnership(dw),
                ApplyClassicSolidOwnershipBridge);

        internal static bool RecoverPriorOwnerAfterFailure(Func<bool> restorePriorOwner)
        {
            if (restorePriorOwner == null)
            {
                return false;
            }

            try
            {
                return restorePriorOwner();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Prior wallpaper owner recovery threw");
                return false;
            }
        }

        internal static bool RestorePriorOwner(
            WallpaperMode sourceMode,
            bool legacyWallpaperCaptured,
            bool priorLegacyWallpaperExists,
            string priorLegacyWallpaper,
            Func<bool> restoreSharedState,
            Func<bool> restoreSpotlightOwner,
            Func<bool> deactivateSpotlight,
            Func<string, bool> setLegacyWallpaper,
            Func<bool> deleteLegacyWallpaper,
            Func<bool> restoreSourceOwnership,
            Func<bool> establishSourceMode,
            Action notifySettings,
            Action<string> logComplete,
            Action<string> logIncomplete,
            Func<bool> restorePictureAssignments = null)
        {
            bool complete = true;

            try
            {
                complete &= restoreSharedState != null && restoreSharedState();

                if (sourceMode == WallpaperMode.Spotlight)
                {
                    complete &= restoreSpotlightOwner != null && restoreSpotlightOwner();
                }
                else
                {
                    complete &= deactivateSpotlight != null && deactivateSpotlight();

                    if (legacyWallpaperCaptured)
                    {
                        complete &= RestoreLegacyWallpaperRegistration(
                            priorLegacyWallpaperExists,
                            priorLegacyWallpaper,
                            setLegacyWallpaper,
                            deleteLegacyWallpaper);
                    }

                    complete &= restoreSourceOwnership != null && restoreSourceOwnership();

                    if (sourceMode == WallpaperMode.Picture && restorePictureAssignments != null)
                        complete &= restorePictureAssignments();

                    if (sourceMode == WallpaperMode.Solid || sourceMode == WallpaperMode.Picture || sourceMode == WallpaperMode.Slideshow)
                        complete &= establishSourceMode != null && establishSourceMode();

                    notifySettings?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Prior wallpaper owner restore threw for {sourceMode}");
                complete = false;
            }

            string message = $"prior wallpaper owner after failed destination establishment: {sourceMode}";
            if (complete)
                logComplete?.Invoke($"Restored {message}");
            else
                logIncomplete?.Invoke($"Incomplete restore of {message}");

            return complete;
        }

        private static bool RestoreSharedWallpaperState(IDesktopWallpaper dw, uint priorColor, DesktopWallpaperPosition priorPosition)
        {
            try
            {
                dw.SetBackgroundColor(priorColor);
                dw.SetPosition(priorPosition);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not restore prior wallpaper color or position");
                return false;
            }
        }

        private static bool RestoreSourceWallpaperOwnership(
            IDesktopWallpaper dw,
            WallpaperMode sourceMode,
            IShellItemArray priorSlideshowSource,
            DesktopSlideshowOptions priorSlideshowOptions,
            uint priorSlideshowTick)
        {
            try
            {
                if (sourceMode == WallpaperMode.Slideshow)
                {
                    if (priorSlideshowSource == null)
                    {
                        return false;
                    }

                    dw.Enable(true);
                    dw.SetSlideshow(priorSlideshowSource);
                    dw.SetSlideshowOptions(priorSlideshowOptions, priorSlideshowTick);
                }
                else if (sourceMode == WallpaperMode.Solid)
                {
                    dw.Enable(false);
                }
                else
                {
                    dw.Enable(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Could not restore prior wallpaper rendering owner {sourceMode}");
                return false;
            }
        }

        private static bool RestorePriorOwner(
            IDesktopWallpaper dw,
            WallpaperMode sourceMode,
            uint priorColor,
            DesktopWallpaperPosition priorPosition,
            IShellItemArray priorSlideshowSource,
            DesktopSlideshowOptions priorSlideshowOptions,
            uint priorSlideshowTick,
            bool legacyWallpaperCaptured,
            bool priorLegacyWallpaperExists,
            string priorLegacyWallpaper,
            PictureRollbackSnapshot priorPictureRollback)
        {
            return RestorePriorOwner(
                sourceMode,
                legacyWallpaperCaptured,
                priorLegacyWallpaperExists,
                priorLegacyWallpaper,
                () => RestoreSharedWallpaperState(dw, priorColor, priorPosition),
                () => ActivateSpotlightDestination(dw, normalizePresentation: false) == WallpaperApplyOutcome.Success,
                () => DeactivateSpotlight() == WallpaperApplyOutcome.Success,
                value => SetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, value),
                () => DeleteHkcuValue(ControlPanelDesktopSubkey, WallpaperValue),
                () => RestoreSourceWallpaperOwnership(dw, sourceMode, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick),
                () => EstablishDestinationMode(sourceMode) == WallpaperApplyOutcome.Success,
                NotifyWallpaperSettingsChanged,
                message => _logger.Warn(message),
                message => _logger.Error(message),
                priorPictureRollback == null
                    ? null
                    : () => RestorePictureRollbackSnapshot(
                        priorPictureRollback,
                        (monitorId, path) => dw.SetWallpaper(monitorId, path)));
        }
        internal static bool RestoreLegacyWallpaperRegistration(
            bool priorValueExisted,
            string priorValue,
            Func<string, bool> setValue,
            Func<bool> deleteValue)
        {
            if (priorValueExisted)
            {
                return setValue != null && setValue(priorValue ?? string.Empty);
            }

            return deleteValue != null && deleteValue();
        }

        internal static WallpaperApplyOutcome DeactivateSpotlight(
            bool modeIsSpotlight,
            bool providerEnabled,
            Func<bool> resetMode,
            Func<bool> disableProvider)
        {
            try
            {
                bool modeReset = true;
                bool providerDisabled = true;

                if (modeIsSpotlight)
                {
                    if (resetMode == null)
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                    modeReset = resetMode();
                }

                if (providerEnabled)
                {
                    if (disableProvider == null)
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                    providerDisabled = disableProvider();
                }

                if (!modeReset || !providerDisabled)
                {
                    _logger.Warn("Desktop Spotlight mode or provider state could not be deactivated completely");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight deactivation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        private static WallpaperApplyOutcome DeactivateSpotlight()
        {
            bool modeIsSpotlight = HkcuDword(WallpapersSubkey, BackgroundTypeValue) == BackgroundTypeSpotlight;
            bool providerEnabled = HkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue) == 1;
            return DeactivateSpotlight(
                modeIsSpotlight,
                providerEnabled,
                () => SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, BackgroundTypePicture, create: true),
                () => SetHkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue, 0, create: false));
        }

        internal static WallpaperApplyOutcome ActivateSpotlight(
            Func<bool> enableProvider,
            Func<bool> selectMode)
        {
            if (enableProvider == null || selectMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool providerEnabled = enableProvider();
                bool modeSelected = selectMode();
                if (!providerEnabled || !modeSelected)
                {
                    _logger.Warn("Desktop Spotlight provider or mode state could not be established completely");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight activation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static int? BackgroundTypeForMode(WallpaperMode mode)
        {
            switch (mode)
            {
                case WallpaperMode.Picture: return BackgroundTypePicture;
                case WallpaperMode.Solid: return BackgroundTypeSolid;
                case WallpaperMode.Slideshow: return BackgroundTypeSlideshow;
                case WallpaperMode.Spotlight: return BackgroundTypeSpotlight;
                default: return null;
            }
        }

        internal static WallpaperApplyOutcome EstablishDestinationMode(
            WallpaperMode mode,
            Func<int, bool> writeMode,
            Func<int?> readMode)
        {
            int? backgroundType = BackgroundTypeForMode(mode);
            if (!backgroundType.HasValue || writeMode == null || readMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                if (!writeMode(backgroundType.Value) || readMode() != backgroundType.Value)
                {
                    _logger.Warn($"Wallpaper destination mode {mode} could not be established or verified");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper destination mode {mode} could not be established");
                return WallpaperApplyOutcome.Failed;
            }
        }

        private static WallpaperApplyOutcome EstablishDestinationMode(WallpaperMode mode)
        {
            return EstablishDestinationMode(
                mode,
                value => SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, value, create: true),
                () => HkcuDword(WallpapersSubkey, BackgroundTypeValue));
        }

        internal static WallpaperApplyOutcome VerifyDestinationMode(WallpaperMode expectedMode, Func<WallpaperMode> detectMode)
        {
            if (detectMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                var actual = detectMode();
                if (actual != expectedMode)
                {
                    _logger.Warn($"Wallpaper destination mode verification failed: requested {expectedMode}, detected {actual}");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper destination mode verification failed for {expectedMode}");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome VerifyDestinationOwnership(
            WallpaperMode expectedMode,
            Func<WallpaperMode> detectMode,
            Func<bool?> slideshowActive)
        {
            var modeOutcome = VerifyDestinationMode(expectedMode, detectMode);
            if (modeOutcome != WallpaperApplyOutcome.Success || expectedMode == WallpaperMode.Spotlight)
            {
                return modeOutcome;
            }
            if (slideshowActive == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool? active = slideshowActive();
                if (!active.HasValue)
                {
                    _logger.Warn($"Wallpaper ownership verification failed for {expectedMode}: slideshow status is unavailable");
                    return WallpaperApplyOutcome.Failed;
                }

                bool expectedSlideshow = expectedMode == WallpaperMode.Slideshow;
                if (active.Value != expectedSlideshow)
                {
                    _logger.Warn($"Wallpaper ownership verification failed: requested {expectedMode}, slideshow active={active.Value}");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper ownership verification failed for {expectedMode}");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome VerifySlideshowOwnershipSettled(
            Func<int?> readBackgroundType,
            Func<bool?> readSlideshowActive,
            Action<int> delay,
            int maxAttempts = SlideshowSettlementMaxAttempts,
            int delayMs = SlideshowSettlementDelayMs)
        {
            if (readBackgroundType == null || readSlideshowActive == null || delay == null || maxAttempts <= 0 || delayMs < 0)
            {
                return WallpaperApplyOutcome.Failed;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                int? backgroundType;
                try
                {
                    backgroundType = readBackgroundType();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Slideshow settlement could not read destination mode");
                    return WallpaperApplyOutcome.Failed;
                }

                if (backgroundType != BackgroundTypeSlideshow)
                {
                    _logger.Warn($"Slideshow settlement contradicted by destination mode {backgroundType?.ToString() ?? "unavailable"}");
                    return WallpaperApplyOutcome.Failed;
                }

                bool? slideshowActive = null;
                try
                {
                    slideshowActive = readSlideshowActive();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Slideshow ownership status is unavailable during settlement attempt {attempt}/{maxAttempts}");
                }

                if (slideshowActive == true)
                {
                    return WallpaperApplyOutcome.Success;
                }

                if (attempt < maxAttempts)
                {
                    try
                    {
                        delay(delayMs);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Slideshow settlement delay failed");
                        return WallpaperApplyOutcome.Failed;
                    }
                }
            }

            _logger.Warn($"Slideshow ownership did not settle within {(maxAttempts - 1) * delayMs} ms");
            return WallpaperApplyOutcome.Failed;
        }
        internal static bool IsSpotlightDestinationEstablished(
            int? backgroundType,
            int? providerEnabled,
            bool? slideshowActive,
            IEnumerable<string> renderedWallpaperPaths,
            Func<string, bool> fileExists = null) =>
            IsSpotlightDestinationEstablished(
                backgroundType,
                providerEnabled,
                slideshowActive,
                positionIsFill: true,
                renderedWallpaperPaths,
                fileExists);

        internal static bool IsSpotlightDestinationEstablished(
            int? backgroundType,
            int? providerEnabled,
            bool? slideshowActive,
            bool positionIsFill,
            IEnumerable<string> renderedWallpaperPaths,
            Func<string, bool> fileExists = null)
        {
            if (backgroundType != BackgroundTypeSpotlight || providerEnabled != 1 ||
                !slideshowActive.HasValue || slideshowActive.Value || !positionIsFill)
            {
                return false;
            }

            var paths = renderedWallpaperPaths?.ToList() ?? new List<string>();
            return paths.Count > 0 && paths.All(path =>
                !string.IsNullOrWhiteSpace(path) &&
                IsSpotlightProviderPath(path) &&
                (fileExists == null || fileExists(path)));
        }
        private static List<string> GetRenderedWallpaperPaths(IDesktopWallpaper dw, IEnumerable<string> monitorIds)
        {
            var paths = new List<string>();
            foreach (string monitorId in monitorIds ?? Array.Empty<string>())
            {
                try
                {
                    paths.Add(dw.GetWallpaper(monitorId));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not read rendered wallpaper for {monitorId}");
                    paths.Add(string.Empty);
                }
            }

            return paths;
        }

        private static bool TryEnableDesktopBackground(IDesktopWallpaper dw)
        {
            try
            {
                dw.Enable(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not hand desktop rendering ownership back before Spotlight activation");
                return false;
            }
        }

        internal static bool RefreshSpotlightSelection(Func<bool> refreshDesktop, Action notifySettings)
        {
            if (refreshDesktop == null || notifySettings == null)
            {
                return false;
            }

            try
            {
                bool refreshed = refreshDesktop();
                notifySettings();
                if (!refreshed)
                    _logger.Warn("Desktop Spotlight shell refresh failed");
                return refreshed;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight shell refresh failed");
                return false;
            }
        }

        internal static bool PaintSpotlightProviderImage(
            string path,
            Func<string, bool> isUsableImage,
            Action<string> paint)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsVerifiedSpotlightProviderPath(path) || isUsableImage == null || paint == null)
            {
                return false;
            }

            try
            {
                if (!isUsableImage(path))
                {
                    return false;
                }

                paint(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight provider-owned repaint failed");
                return false;
            }
        }

        internal static bool PaintSpotlightProviderImageForMonitors(
            string path,
            IEnumerable<string> monitorIds,
            Func<string, bool> isUsableImage,
            Action<string, string> paint)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsVerifiedSpotlightProviderPath(path) ||
                monitorIds == null || isUsableImage == null || paint == null)
            {
                return false;
            }

            List<string> ids = monitorIds.ToList();
            if (ids.Count == 0 || ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            {
                return false;
            }

            try
            {
                if (!isUsableImage(path))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight provider-owned repaint validation failed");
                return false;
            }

            bool complete = true;
            foreach (string monitorId in ids)
            {
                try
                {
                    paint(monitorId, path);
                }
                catch (Exception ex)
                {
                    complete = false;
                    _logger.Warn(ex, $"Desktop Spotlight provider-owned repaint failed for {monitorId}");
                }
            }

            return complete;
        }

        internal static bool NormalizeSpotlightPositionToFill(Action setFillPosition)
        {
            if (setFillPosition == null)
            {
                return false;
            }

            try
            {
                setFillPosition();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight position could not be normalized to Fill");
                return false;
            }
        }

        internal static WallpaperApplyOutcome ActivateSpotlightDestination(
            Func<bool> enableDesktopBackground,
            Func<bool> relinquishPreviousOwner,
            Func<bool> enableProvider,
            Func<bool> selectMode,
            Func<bool> refreshSelection,
            Func<bool> verifyDestination,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage) =>
            ActivateSpotlightDestination(
                enableDesktopBackground,
                relinquishPreviousOwner,
                enableProvider,
                selectMode,
                refreshSelection,
                normalizePresentation: null,
                verifyDestination,
                selectRepaintPath,
                paintProviderImage);

        internal static WallpaperApplyOutcome ActivateSpotlightDestination(
            Func<bool> enableDesktopBackground,
            Func<bool> relinquishPreviousOwner,
            Func<bool> enableProvider,
            Func<bool> selectMode,
            Func<bool> refreshSelection,
            Func<bool> normalizePresentation,
            Func<bool> verifyDestination,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage)
        {
            if (enableDesktopBackground == null || relinquishPreviousOwner == null || enableProvider == null || selectMode == null ||
                refreshSelection == null || verifyDestination == null || selectRepaintPath == null || paintProviderImage == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                if (!enableDesktopBackground() || !relinquishPreviousOwner())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                var providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                if (normalizePresentation != null)
                {
                    if (!normalizePresentation())
                    {
                        return WallpaperApplyOutcome.Failed;
                    }

                    providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                    if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                }

                if (verifyDestination())
                {
                    return WallpaperApplyOutcome.Success;
                }

                string repaintPath = selectRepaintPath();
                if (string.IsNullOrWhiteSpace(repaintPath) || !IsVerifiedSpotlightProviderPath(repaintPath))
                {
                    _logger.Warn("Desktop Spotlight mode/provider was selected, but no verified provider-owned repaint image is available");
                    return WallpaperApplyOutcome.Failed;
                }

                if (!paintProviderImage(repaintPath))
                {
                    return WallpaperApplyOutcome.Failed;
                }

                providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                if (!verifyDestination())
                {
                    _logger.Warn("Desktop Spotlight presentation/repaint completed, but final provider/mode/rendered ownership verification failed");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight destination activation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static bool RelinquishWallpaperForSpotlight(
            Func<bool?> readSlideshowActive,
            Func<WallpaperApplyOutcome> supersedeSlideshow,
            Func<bool> deleteLegacyWallpaper)
        {
            if (readSlideshowActive == null || supersedeSlideshow == null || deleteLegacyWallpaper == null)
            {
                return false;
            }

            try
            {
                bool? slideshowActive = readSlideshowActive();
                if (!slideshowActive.HasValue)
                {
                    _logger.Warn("Could not establish Slideshow ownership state before Spotlight acquisition");
                    return false;
                }

                if (slideshowActive.Value && supersedeSlideshow() != WallpaperApplyOutcome.Success)
                {
                    return false;
                }

                return deleteLegacyWallpaper();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not relinquish prior wallpaper owner before Spotlight acquisition");
                return false;
            }
        }

        private static bool RelinquishWallpaperForSpotlight(IDesktopWallpaper dw)
        {
            // Current Settings clears legacy wallpaper ownership before Spotlight provider selection; keep that OS-coupled handoff isolated here
            return RelinquishWallpaperForSpotlight(
                () => GetSlideshowActiveForOwnership(dw),
                () => SupersedeSlideshowOwnership(dw),
                () => DeleteHkcuValue(ControlPanelDesktopSubkey, WallpaperValue));
        }
        private static WallpaperApplyOutcome ActivateSpotlightDestination(
            IDesktopWallpaper dw,
            IReadOnlyList<string> attachedMonitorIds = null,
            bool normalizePresentation = true)
        {
            if (HkcuDword(BackgroundAppsSubkey, BackgroundAppsDisabled) == 1)
            {
                _logger.Warn("Desktop Spotlight destination cannot be established while background apps are disabled globally");
                return WallpaperApplyOutcome.Failed;
            }

            List<string> monitorIds;
            if (attachedMonitorIds != null)
            {
                monitorIds = attachedMonitorIds.ToList();
            }
            else if (!TryResolveCurrentActiveSpotlightMonitorIds(
                DisplayConfigHelper.GetDisplayConfigs(),
                () => dw.GetMonitorDevicePathCount(),
                index => dw.GetMonitorDevicePathAt(index),
                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                out monitorIds))
            {
                _logger.Warn("Desktop Spotlight destination cannot be established because the current-active wallpaper monitor domain is incomplete");
                return WallpaperApplyOutcome.Failed;
            }

            if (monitorIds.Count == 0)
            {
                _logger.Warn("Desktop Spotlight destination cannot be verified because no attached wallpaper monitors are available");
                return WallpaperApplyOutcome.Failed;
            }

            bool VerifyDestination() => IsSpotlightDestinationEstablished(
                HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                HkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue),
                GetSlideshowActiveForOwnership(dw),
                !normalizePresentation || dw.GetPosition() == DesktopWallpaperPosition.Fill,
                GetRenderedWallpaperPaths(dw, monitorIds),
                IsUsableSpotlightProviderImage);

            string SelectRepaintPath() => SelectSpotlightRepaintPath(
                GetRenderedWallpaperPaths(dw, monitorIds),
                EnumerateSpotlightProviderImages(SpotlightIrisRoot, newestFirst: true),
                EnumerateSpotlightProviderImages(SpotlightBuiltInRoot, newestFirst: false),
                IsUsableSpotlightProviderImage,
                IsLandscapeSpotlightProviderImage);

            Func<bool> normalizeSpotlightPresentation = normalizePresentation
                ? () => NormalizeSpotlightPositionToFill(() => dw.SetPosition(DesktopWallpaperPosition.Fill))
                : null;

            return ActivateSpotlightDestination(
                () => TryEnableDesktopBackground(dw),
                () => RelinquishWallpaperForSpotlight(dw),
                () => SetHkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue, 1, create: true),
                () => SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, BackgroundTypeSpotlight, create: true),
                () => RefreshSpotlightSelection(
                    () => SystemParametersInfo(
                        SpiSetDeskWallpaper,
                        0,
                        null,
                        SpifUpdateIniFile | SpifSendChange),
                    NotifyWallpaperSettingsChanged),
                normalizeSpotlightPresentation,
                VerifyDestination,
                SelectRepaintPath,
                path => PaintSpotlightProviderImageForMonitors(
                    path,
                    monitorIds,
                    IsUsableSpotlightProviderImage,
                    (monitorId, providerPath) => dw.SetWallpaper(monitorId, providerPath)));
        }

        private static void NotifyWallpaperSettingsChanged()
        {
            try
            {
                var result = SendMessageTimeout(
                    HwndBroadcast,
                    WmSettingChange,
                    UIntPtr.Zero,
                    @"Control Panel\Desktop",
                    SmtoAbortIfHung,
                    250,
                    out _);

                if (result == IntPtr.Zero)
                    _logger.Debug("Wallpaper settings notification timed out or was not delivered");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Wallpaper settings notification failed");
            }
        }

        #endregion
        #region Preview

        public static string GetSnapshotPreviewPath(WallpaperSettings snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            if (snapshot.Mode == WallpaperMode.Picture)
            {
                return snapshot.PerMonitor.Values.Select(m => m.Path).FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));
            }

            if (snapshot.Mode == WallpaperMode.Slideshow)
            {
                return FirstImageInSourceFolders(snapshot.SlideshowConfig?.SourcePaths);
            }

            if (snapshot.Mode == WallpaperMode.Spotlight)
            {
                return GetCurrentSpotlightWallpaperPath();
            }

            return null;
        }

        private static string FirstImageInSourceFolders(List<string> paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (var folder in paths.Where(p => !string.IsNullOrEmpty(p) && System.IO.Directory.Exists(p)))
            {
                try
                {
                    var image = new System.IO.DirectoryInfo(folder)
                        .GetFiles()
                        .Where(f => _imageExtensions.Contains(f.Extension))
                        .OrderBy(f => f.Name)
                        .Select(f => f.FullName)
                        .FirstOrDefault();

                    if (image != null)
                    {
                        return image;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not read slideshow folder {folder}");
                }
            }

            return null;
        }

        #endregion

        #region Enum <-> String Mapping

        public static readonly string[] AllPositions = { "fill", "fit", "stretch", "tile", "center", "span" };

        public static string NormalizePosition(string pos) => PositionToString(StringToPosition(pos));

        private static string PositionToString(DesktopWallpaperPosition pos)
        {
            switch (pos)
            {
                case DesktopWallpaperPosition.Center: return "center";
                case DesktopWallpaperPosition.Tile: return "tile";
                case DesktopWallpaperPosition.Stretch: return "stretch";
                case DesktopWallpaperPosition.Fit: return "fit";
                case DesktopWallpaperPosition.Span: return "span";
                default: return "fill";
            }
        }

        private static DesktopWallpaperPosition StringToPosition(string pos)
        {
            switch (pos)
            {
                case "center": return DesktopWallpaperPosition.Center;
                case "tile": return DesktopWallpaperPosition.Tile;
                case "stretch": return DesktopWallpaperPosition.Stretch;
                case "fit": return DesktopWallpaperPosition.Fit;
                case "span": return DesktopWallpaperPosition.Span;
                default: return DesktopWallpaperPosition.Fill;
            }
        }

        #endregion
    }

    #region Data Model

    public static class WallpaperModeNames
    {
        public static string Display(WallpaperMode mode) => mode == WallpaperMode.Solid ? "Solid Color" : mode.ToString();
    }

    public enum WallpaperMode
    {
        Unknown,
        Solid,
        Picture,
        Slideshow,
        Spotlight
    }

    public class WallpaperSettings
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WallpaperMode Mode { get; set; } = WallpaperMode.Unknown;

        [JsonProperty("solidColorArgb")]
        public uint SolidColorArgb { get; set; } = 0;

        [JsonProperty("position")]
        public string Position { get; set; } = "fill";

        [JsonProperty("perMonitor")]
        public Dictionary<string, MonitorWallpaper> PerMonitor { get; set; } = new Dictionary<string, MonitorWallpaper>();

        [JsonProperty("slideshowConfig")]
        public SlideshowConfig SlideshowConfig { get; set; } = null;
    }

    public class MonitorWallpaper
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("monitorId", NullValueHandling = NullValueHandling.Ignore)]
        public string MonitorId { get; set; }
    }

    public class SlideshowConfig
    {
        [JsonProperty("intervalSeconds")]
        public uint IntervalSeconds { get; set; } = 1800;

        [JsonProperty("shuffle")]
        public bool Shuffle { get; set; } = false;

        [JsonProperty("sourcePaths")]
        public List<string> SourcePaths { get; set; } = new List<string>();
    }

    #endregion
}
