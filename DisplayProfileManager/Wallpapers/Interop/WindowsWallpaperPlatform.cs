using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Wallpapers.Models;
using static DisplayProfileManager.Wallpapers.Interop.DesktopWallpaperNative;

namespace DisplayProfileManager.Wallpapers.Interop
{
    internal readonly struct DesktopMonitorRectResult(int hResult, int left, int top, int right, int bottom)
    {
        public int HResult { get; } = hResult;
        public int Left { get; } = left;
        public int Top { get; } = top;
        public int Right { get; } = right;
        public int Bottom { get; } = bottom;
        public bool HasPositiveArea => Right > Left && Bottom > Top;
    }

    internal static class WindowsWallpaperPlatform
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        internal const int HResultSOk = 0;
        internal const int HResultSFalse = 1;
        internal const string WallpapersSubkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
        internal const string BackgroundTypeValue = "BackgroundType";
        internal const int BackgroundTypePicture = 0;
        internal const int BackgroundTypeSolid = 1;
        internal const int BackgroundTypeSlideshow = 2;
        internal const int BackgroundTypeSpotlight = 3;
        internal const string DesktopSpotlightSubkey = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";
        internal const string DesktopSpotlightValue = "EnabledState";
        internal const string ControlPanelDesktopSubkey = @"Control Panel\Desktop";
        internal const string WallpaperValue = "Wallpaper";
        internal const string BackgroundAppsSubkey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        internal const string BackgroundAppsDisabled = "GlobalUserDisabled";

        internal static IDesktopWallpaper CreateDesktopWallpaper()
        {
            var type = Type.GetTypeFromCLSID(new Guid(ClsidDesktopWallpaper));
            return (IDesktopWallpaper)Activator.CreateInstance(type);
        }

        internal static DesktopMonitorRectResult ReadDesktopMonitorRect(IDesktopWallpaper dw, string monitorId)
        {
            int hResult = dw.GetMonitorRECT(monitorId, out RECT rect);
            return new DesktopMonitorRectResult(hResult, rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        internal static int? HkcuDword(string subkey, string value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey);
                if (key?.GetValue(value) is int dword)
                {
                    return dword;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
            }

            return null;
        }

        internal static string HkcuString(string subkey, string value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey);
                return key?.GetValue(value) as string;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
                return null;
            }
        }

        internal static bool SetHkcuDword(string subkey, string value, int data, bool create)
        {
            try
            {
                using var key = create
                    ? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subkey)
                    : Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey, true);
                if (key == null)
                {
                    return false;
                }

                key.SetValue(value, data, Microsoft.Win32.RegistryValueKind.DWord);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry write failed for {subkey}\\{value}");
                return false;
            }
        }

        internal static bool SetHkcuString(string subkey, string value, string data)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subkey);
                if (key == null)
                {
                    return false;
                }

                key.SetValue(value, data ?? string.Empty, Microsoft.Win32.RegistryValueKind.String);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry write failed for {subkey}\\{value}");
                return false;
            }
        }

        internal static bool TryGetHkcuString(string subkey, string value, out bool exists, out string data)
        {
            exists = false;
            data = null;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey);
                if (key == null)
                {
                    return true;
                }

                object raw = key.GetValue(value, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (raw == null)
                {
                    return true;
                }
                if (raw is not string text)
                {
                    return false;
                }

                exists = true;
                data = text;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
                return false;
            }
        }

        internal static bool DeleteHkcuValue(string subkey, string value)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey, true);
                if (key == null)
                {
                    return true;
                }

                key.DeleteValue(value, throwOnMissingValue: false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Registry delete failed for {subkey}\\{value}");
                return false;
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

        internal static WallpaperMode DetectCurrentMode(IDesktopWallpaper dw)
        {
            int? backgroundType = HkcuDword(WallpapersSubkey, BackgroundTypeValue);
            bool slideshowActive = SafeGetStatus(dw).HasFlag(DesktopSlideshowState.Slideshow);
            string liveWallpaper = HkcuString(ControlPanelDesktopSubkey, WallpaperValue);
            return ClassifyWallpaperMode(
                backgroundType,
                slideshowActive,
                !string.IsNullOrEmpty(liveWallpaper),
                SpotlightProvider.IsSpotlightProviderPath(liveWallpaper));
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
                SpotlightProvider.IsSpotlightProviderPath(liveWallpaper));
            return true;
        }

        internal static bool TryDetectCurrentModeForApply(IDesktopWallpaper dw, out WallpaperMode mode)
        {
            return TryDetectCurrentModeForApply(
                HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                HkcuString(ControlPanelDesktopSubkey, WallpaperValue),
                () => (uint)dw.GetStatus(),
                out mode);
        }

        internal static List<string> ReadSlideshowSourcePaths(IDesktopWallpaper dw)
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

        internal static DesktopWallpaperPosition SafeGetPosition(IDesktopWallpaper dw)
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

        internal static uint SafeGetBackgroundColor(IDesktopWallpaper dw)
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

        internal static DesktopSlideshowState SafeGetStatus(IDesktopWallpaper dw)
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
        internal static bool? GetSlideshowActiveForOwnership(IDesktopWallpaper dw)
        {
            return TryGetSlideshowActive(() => (uint)dw.GetStatus(), out bool slideshowActive)
                ? slideshowActive
                : null;
        }
        internal static void NotifyWallpaperSettingsChanged()
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

        public static string NormalizePosition(string pos) => PositionToString(StringToPosition(pos));

        internal static string PositionToString(DesktopWallpaperPosition pos)
        {
            return pos switch
            {
                DesktopWallpaperPosition.Center => "center",
                DesktopWallpaperPosition.Tile => "tile",
                DesktopWallpaperPosition.Stretch => "stretch",
                DesktopWallpaperPosition.Fit => "fit",
                DesktopWallpaperPosition.Span => "span",
                _ => "fill",
            };
        }
        internal static DesktopWallpaperPosition StringToPosition(string pos)
        {
            return pos switch
            {
                "center" => DesktopWallpaperPosition.Center,
                "tile" => DesktopWallpaperPosition.Tile,
                "stretch" => DesktopWallpaperPosition.Stretch,
                "fit" => DesktopWallpaperPosition.Fit,
                "span" => DesktopWallpaperPosition.Span,
                _ => DesktopWallpaperPosition.Fill,
            };
        }
    }
}
