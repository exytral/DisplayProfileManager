using DisplayProfileManager.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    public static class WallpaperHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private static readonly uint[] _standardIntervalSeconds = { 60, 600, 1800, 3600, 21600, 86400 };
        private static readonly HashSet<string> _imageExtensions = new HashSet<string>([".jpg", ".jpeg", ".png", ".bmp", ".gif"], StringComparer.OrdinalIgnoreCase);

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
            RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
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
            [return: MarshalAs(UnmanagedType.Bool)]
            bool Enable();
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            [MarshalAs(UnmanagedType.U4)] public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            [MarshalAs(UnmanagedType.U4)] public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        private const int EddGetDeviceInterfaceName = 0x00000001;
        private const int DisplayDeviceAttachedToDesktop = 0x00000001;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

        private const uint SpiSetDeskWallpaper = 0x0014;
        private const uint SpifUpdateIniFile = 0x01;
        private const uint SpifSendChange = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        #endregion

        private static Dictionary<string, string> BuildMonitorMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dw = CreateDesktopWallpaper();
                uint count = dw.GetMonitorDevicePathCount();
                var dwPaths = new List<string>();

                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        dwPaths.Add(dw.GetMonitorDevicePathAt(i));
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"GetMonitorDevicePathAt failed at index {i}");
                    }
                }

                if (dwPaths.Count == 0)
                {
                    logger.Warn("IDesktopWallpaper reported no monitors");
                    return map;
                }

                var dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
                {
                    // Ignore display slots that are not attached
                    if ((dd.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
                    {
                        dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                        continue;
                    }

                    // Resolve interface path used by IDesktopWallpaper
                    var dd2 = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (EnumDisplayDevices(dd.DeviceName, 0, ref dd2, EddGetDeviceInterfaceName))
                    {
                        var match = dwPaths.FirstOrDefault(p => string.Equals(p, dd2.DeviceID, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            // Keep first device name for shared interface path
                            var prior = map.FirstOrDefault(kv => string.Equals(kv.Value, match, StringComparison.OrdinalIgnoreCase));
                            if (prior.Key != null)
                                logger.Debug($"{dd.DeviceName} resolves to same monitor as {prior.Key}, skipping");
                            else
                                map[dd.DeviceName] = match;
                        }
                        else
                            logger.Debug($"No IDesktopWallpaper match for {dd.DeviceName} (interface path '{dd2.DeviceID}')");
                    }

                    dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "BuildMonitorMap failed");
            }

            foreach (var kvp in map)
                logger.Debug($"Wallpaper monitor map: {kvp.Key} -> {kvp.Value}");

            if (map.Count == 0)
                logger.Warn("Wallpaper monitor map is empty -> every apply will skip every monitor");

            return map;
        }

        #region Spotlight Detection

        private const string SpotlightAssetFolderMarker = @"Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets";

        private const string WallpapersSubkey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
        private const int BackgroundTypeSpotlight = 3;

        private const string DesktopSpotlightSubkey = @"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings";
        private const string DesktopSpotlightValue = "EnabledState";

        private const string SpotlightNamespaceKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{2cc5ca98-6485-489a-920e-b3e88a6ccce3}";

        private const string ContentDeliverySubkey = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string ContentDeliverySpotlightValue = "SubscribedContent-338387Enabled";

        private static bool IsSpotlightPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.IndexOf(SpotlightAssetFolderMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
                logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
            }

            return null;
        }

        private static string HkcuString(string subkey, string value)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey))
                {
                    return key?.GetValue(value) as string;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"Registry read failed for {subkey}\\{value}");
                return null;
            }
        }

        private static bool HkcuKeyExists(string subkey)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey))
                {
                    return key != null;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"Registry probe failed for {subkey}");
                return false;
            }
        }

        private static bool IsSpotlightActive()
        {
            if (HkcuDword(WallpapersSubkey, "BackgroundType") == BackgroundTypeSpotlight)
            {
                return true;
            }

            if (HkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue) == 1)
            {
                return true;
            }

            return HkcuKeyExists(SpotlightNamespaceKey) && HkcuDword(ContentDeliverySubkey, ContentDeliverySpotlightValue) == 1;
        }

        private static string FindSpotlightImage()
        {
            // Prefer wallpaper path currently used by shell
            var live = HkcuString(@"Control Panel\Desktop", "Wallpaper");

            if (!string.IsNullOrEmpty(live) && live.IndexOf("IrisService", StringComparison.OrdinalIgnoreCase) >= 0 && System.IO.File.Exists(live))
            {
                return live;
            }

            try
            {
                var root = new System.IO.DirectoryInfo(SpotlightCacheRoot);
                if (!root.Exists) return null;

                // Use first landscape asset in filename order
                return root.GetFiles("*", System.IO.SearchOption.AllDirectories)
                    .Where(f => f.Length > 100 * 1024)
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .Select(f => f.FullName)
                    .FirstOrDefault(IsLandscape);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Could not scan Spotlight cache");
                return null;
            }
        }

        #endregion

        #region Capture

        public static WallpaperSettings Capture()
        {
            var snapshot = new WallpaperSettings();

            try
            {
                var dw = CreateDesktopWallpaper();
                var status = dw.GetStatus();
                if (status.HasFlag(DesktopSlideshowState.Slideshow))
                {
                    snapshot.Mode = WallpaperMode.Slideshow;
                    CaptureSlideshow(dw, snapshot);
                    logger.Info("Wallpaper capture: Slideshow mode");
                    return snapshot;
                }

                var monitorMap = BuildMonitorMap();
                bool anyPicture = false;
                bool anySpotlightPath = false;

                foreach (var kvp in monitorMap)
                {
                    string path = null;

                    try
                    {
                        path = dw.GetWallpaper(kvp.Value);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex, $"GetWallpaper failed for {kvp.Key}");
                    }

                    if (IsSpotlightPath(path))
                    {
                        anySpotlightPath = true;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(path))
                    {
                        anyPicture = true;
                        snapshot.PerMonitor[kvp.Key] = new MonitorWallpaper
                        {
                            Path = path,
                            MonitorId = kvp.Value
                        };
                    }
                }

                snapshot.SolidColorArgb = SafeGetBackgroundColor(dw);
                snapshot.Position = PositionToString(SafeGetPosition(dw));

                if (anySpotlightPath || IsSpotlightActive())
                {
                    snapshot.Mode = WallpaperMode.Spotlight;
                    snapshot.PerMonitor.Clear();
                    logger.Info("Wallpaper capture: Spotlight mode");
                    return snapshot;
                }

                if (anyPicture)
                {
                    snapshot.Mode = WallpaperMode.Picture;

                    var detached = snapshot.PerMonitor.Keys.Count(k => monitorMap.TryGetValue(k, out var id) && !IsMonitorAttached(dw, id));
                    var detachedNote = detached > 0 ? $", {TextHelper.Plural(detached, "monitor")} currently detached" : "";
                    logger.Info($"Wallpaper capture: Picture mode, {TextHelper.Plural(snapshot.PerMonitor.Count, "monitor")}{detachedNote}");
                }
                else
                {
                    snapshot.Mode = WallpaperMode.Solid;
                    logger.Info("Wallpaper capture: Solid Color mode");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Wallpaper capture failed");
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

                // Preserve non-standard intervals rather than rewriting captured state
                if (!_standardIntervalSeconds.Contains(seconds))
                    logger.Warn($"Slideshow interval reads {seconds}s, which Windows does not offer -> capturing it as-is");

                snapshot.SlideshowConfig = new SlideshowConfig
                {
                    IntervalSeconds = seconds,
                    Shuffle = options.HasFlag(DesktopSlideshowOptions.ShuffleImages),
                    SourcePaths = ReadSlideshowSourcePaths(dw),
                };
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "GetSlideshowOptions failed -> capturing without interval or shuffle");
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

                // Preserve every slideshow source returned by shell
                items.GetCount(out uint count);

                for (uint i = 0; i < count; i++)
                {
                    items.GetItemAt(i, out IShellItem item);
                    if (item == null)
                        continue;

                    item.GetDisplayName(SigdnFileSysPath, out string path);

                    if (!string.IsNullOrEmpty(path))
                        paths.Add(path);

                    Marshal.ReleaseComObject(item);
                }

                Marshal.ReleaseComObject(items);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "GetSlideshow failed -> capturing without source folder");
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
                logger.Warn(ex, "GetPosition failed -> defaulting to Fill");
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
                logger.Warn(ex, "GetBackgroundColor failed -> defaulting to black");
                return 0;
            }
        }

        private static bool IsMonitorAttached(IDesktopWallpaper dw, string monitorId)
        {
            try
            {
                dw.GetMonitorRECT(monitorId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Apply

        public static void Apply(WallpaperSettings snapshot)
        {
            if (snapshot == null || snapshot.Mode == WallpaperMode.Unknown) return;

            if (snapshot.Mode == WallpaperMode.Spotlight)
            {
                ApplySpotlight();
                return;
            }

            try
            {
                var dw = CreateDesktopWallpaper();

                if (snapshot.Mode == WallpaperMode.Slideshow)
                {
                    ApplySlideshow(dw, snapshot);
                    return;
                }

                if (snapshot.Mode == WallpaperMode.Solid)
                {
                    dw.SetBackgroundColor(snapshot.SolidColorArgb);
                    ClearAllWallpapers(dw);

                    logger.Info("Wallpaper applied: Solid Color");
                    return;
                }

                if (snapshot.Mode == WallpaperMode.Picture)
                    ApplyPicture(dw, snapshot);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Wallpaper apply failed");
            }
        }

        private const string BackgroundAppsSubkey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
        private const string BackgroundAppsDisabled = "GlobalUserDisabled";

        private const string SpotlightCacheRootRelativePath = @"Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService";
        private static string SpotlightCacheRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SpotlightCacheRootRelativePath);

        private static void ApplySpotlight()
        {
            // Spotlight cannot run when background apps are disabled
            if (HkcuDword(BackgroundAppsSubkey, BackgroundAppsDisabled) == 1)
            {
                logger.Warn("Spotlight apply: background apps disabled globally -> provider cannot run");
                return;
            }

            // Enable provider before selecting Spotlight mode
            if (!SetHkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue, 1, create: true))
            {
                logger.Warn("Spotlight apply: cannot write DesktopSpotlight provider switch");
                return;
            }

            // Update existing wallpaper mode value only
            if (!SetHkcuDword(WallpapersSubkey, "BackgroundType", BackgroundTypeSpotlight, create: false))
            {
                logger.Warn("Spotlight apply: cannot write BackgroundType");
                return;
            }

            SetHkcuDword(ContentDeliverySubkey, ContentDeliverySpotlightValue, 1, create: false);

            // Toggle mode to trigger desktop repaint
            if (SetHkcuDword(WallpapersSubkey, "BackgroundType", 0, create: false))
            {
                RefreshDesktop();
                System.Threading.Thread.Sleep(120);
                SetHkcuDword(WallpapersSubkey, "BackgroundType", BackgroundTypeSpotlight, create: false);
            }

            RefreshDesktop();
            logger.Info("Desktop Spotlight applied and refreshed");

            if (SettingsManager.Instance.Debug.SkipSpotlightRepaint)
                logger.Warn("[debugFlag: skipSpotlightRepaint] Not painting -> leaving repaint to Windows");
            else
                PaintSpotlightImage();
        }

        private static bool SetHkcuDword(string subkey, string value, int data, bool create)
        {
            try
            {
                using (var key = create ? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subkey) : Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subkey, true))
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
                logger.Debug(ex, $"Registry write failed for {subkey}\\{value}");
                return false;
            }
        }

        private static void PaintSpotlightImage()
        {
            var path = FindSpotlightImage();

            if (path == null)
            {
                logger.Debug("Spotlight: no image to paint, leaving repaint to Windows");
                return;
            }

            try
            {
                CreateDesktopWallpaper().SetWallpaper(null, path);
                logger.Info($"Spotlight: painted {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Spotlight: could not paint, leaving desktop as-is");
            }
        }

        private static void ApplyPicture(IDesktopWallpaper dw, WallpaperSettings snapshot)
        {
            var monitorMap = BuildMonitorMap();

            if (monitorMap.Count == 0)
            {
                logger.Warn("Wallpaper apply: no addressable monitors, nothing applied");
                return;
            }

            try
            {
                dw.SetBackgroundColor(snapshot.SolidColorArgb);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "SetBackgroundColor failed -> letterbox color left as-is");
            }

            int applied = 0;
            int skipped = 0;
            int missing = 0;

            foreach (var kvp in snapshot.PerMonitor)
            {
                if (!monitorMap.TryGetValue(kvp.Key, out var monitorId))
                {
                    monitorId = kvp.Value.MonitorId;
                }

                if (string.IsNullOrEmpty(monitorId))
                {
                    skipped++;
                    logger.Debug($"{kvp.Key} not connected and no stored monitor id, skipping");
                    continue;
                }

                if (!string.IsNullOrEmpty(kvp.Value.Path) && !System.IO.File.Exists(kvp.Value.Path))
                {
                    missing++;
                    logger.Warn($"Wallpaper file not found for {kvp.Key}: {kvp.Value.Path}");
                    continue;
                }

                try
                {
                    dw.SetWallpaper(monitorId, kvp.Value.Path);
                    applied++;
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"SetWallpaper failed for {kvp.Key}");
                }
            }

            try
            {
                dw.SetPosition(StringToPosition(snapshot.Position));
                logger.Info($"Wallpaper position set to '{snapshot.Position}'");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"SetPosition({snapshot.Position}) failed");
            }

            logger.Info($"Wallpaper applied: Picture mode, {applied} applied, {skipped} skipped (disconnected), {missing} skipped (file missing)");
            RefreshDesktop();
        }

        private static void ApplySlideshow(IDesktopWallpaper dw, WallpaperSettings snapshot)
        {
            if (snapshot.SlideshowConfig == null)
            {
                logger.Warn("Wallpaper apply: Slideshow mode with no config, nothing applied");
                return;
            }

            bool sourceSet = ApplySlideshowSource(dw, snapshot.SlideshowConfig.SourcePaths);

            try
            {
                var options = snapshot.SlideshowConfig.Shuffle
                    ? DesktopSlideshowOptions.ShuffleImages
                    : DesktopSlideshowOptions.None;

                dw.SetSlideshowOptions(options, snapshot.SlideshowConfig.IntervalSeconds * 1000);

                var source = sourceSet ? "source applied" : "source unavailable, timing only";
                logger.Info($"Wallpaper applied: Slideshow every {snapshot.SlideshowConfig.IntervalSeconds}s, " + $"shuffle {snapshot.SlideshowConfig.Shuffle}, {source}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "SetSlideshowOptions failed");
            }
        }

        private static bool ApplySlideshowSource(IDesktopWallpaper dw, List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return false;
            }

            var folder = paths.FirstOrDefault(p => System.IO.Directory.Exists(p));

            if (folder == null)
            {
                logger.Warn($"Slideshow source folder no longer exists: {string.Join(", ", paths)}");
                return false;
            }

            try
            {
                var itemGuid = typeof(IShellItem).GUID;
                var arrayGuid = typeof(IShellItemArray).GUID;

                SHCreateItemFromParsingName(
                    folder,
                    IntPtr.Zero,
                    ref itemGuid,
                    out IShellItem item);

                SHCreateShellItemArrayFromShellItem(
                    item,
                    ref arrayGuid,
                    out IShellItemArray array);

                dw.SetSlideshow(array);

                Marshal.ReleaseComObject(array);
                Marshal.ReleaseComObject(item);
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"SetSlideshow failed for {folder}");
                return false;
            }
        }

        private static void ClearAllWallpapers(IDesktopWallpaper dw)
        {
            try
            {
                uint count = dw.GetMonitorDevicePathCount();

                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        string monitorId = dw.GetMonitorDevicePathAt(i);

                        if (!string.IsNullOrEmpty(monitorId) && IsMonitorAttached(dw, monitorId))
                            dw.SetWallpaper(monitorId, string.Empty);
                    }
                    catch (Exception ex)
                    {
                        logger.Debug(ex, $"Could not clear wallpaper at index {i}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not enumerate monitors to clear wallpaper");
            }
        }

        private static void RefreshDesktop()
        {
            SystemParametersInfo(SpiSetDeskWallpaper, 0, null, SpifUpdateIniFile | SpifSendChange);
        }

        private static bool IsLandscape(string path)
        {
            try
            {
                using (var stream = System.IO.File.OpenRead(path))
                {
                    // Read image dimensions without decoding full bitmap
                    var frame = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
                    return frame.PixelWidth > frame.PixelHeight;
                }
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"Could not read image dimensions for {path}");
                return false;
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
                // Use the captured profile image rather than live desktop
                return snapshot.PerMonitor.Values.Select(m => m.Path).FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));
            }

            if (snapshot.Mode == WallpaperMode.Slideshow)
            {
                return FirstImageInSourceFolders(snapshot.SlideshowConfig?.SourcePaths);
            }

            if (snapshot.Mode == WallpaperMode.Spotlight)
            {
                return FindSpotlightImage();
            }

            return null;
        }

        private static string FirstImageInSourceFolders(List<string> paths)
        {
            if (paths == null) return null;

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
                    logger.Debug(ex, $"Could not read slideshow folder {folder}");
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
        [JsonProperty("mode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WallpaperMode Mode { get; set; } = WallpaperMode.Unknown;

        [JsonProperty("solidColorArgb")]
        public uint SolidColorArgb { get; set; } = 0;

        [JsonProperty("position")]
        public string Position { get; set; } = "fill";

        [JsonProperty("perMonitor")]
        public Dictionary<string, MonitorWallpaper> PerMonitor { get; set; } =
            new Dictionary<string, MonitorWallpaper>();

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
