using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays.ColorProfiles
{
    public static class ColorProfileAdapter
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        #region P/Invoke

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int ColorProfileSetDisplayDefaultAssociation(
            WcsProfileManagementScope scope,
            string profileName,
            ColorProfileType profileType,
            ColorProfileSubtype profileSubType,
            DisplayConfigNative.LUID targetAdapterId,
            uint sourceId);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WcsGetUsePerUserProfiles(
            string deviceName,
            DeviceClassFlags deviceClass,
            out bool usePerUserProfiles);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WcsSetUsePerUserProfiles(
            string deviceName,
            DeviceClassFlags deviceClass,
            bool usePerUserProfiles);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(
            string lpDevice,
            uint iDevNum,
            ref DISPLAY_DEVICE lpDisplayDevice,
            uint dwFlags);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int ColorProfileGetDisplayDefault(
            WcsProfileManagementScope scope,
            DisplayConfigNative.LUID targetAdapterId,
            uint sourceId,
            ColorProfileType profileType,
            ColorProfileSubtype profileSubType,
            out IntPtr profileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        #endregion

        #region Enums

        private enum WcsProfileManagementScope : uint
        {
            SystemWide = 0,
            CurrentUser = 1
        }

        private enum ColorProfileType : uint
        {
            Icc = 0,
            Dmp = 1,
            Camp = 2,
            Gmmp = 3
        }

        private enum ColorProfileSubtype : uint
        {
            Perceptual = 0,
            RelativeColorimetric = 1,
            Saturation = 2,
            AbsoluteColorimetric = 3,
            None = 4,
            RgbWorkingSpace = 5,
            CustomWorkingSpace = 6,
            StandardDisplayColorMode = 7,
            ExtendedDisplayColorMode = 8
        }

        private enum DeviceClassFlags : uint
        {
            Monitor = 0x6d6e7472
        }

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        #endregion

        #region Private Methods

        private static string GetDisplayRegistryKey(string gdiDeviceName)
        {
            var dd = new DISPLAY_DEVICE();
            dd.cb = (uint)Marshal.SizeOf(dd);
            if (EnumDisplayDevices(gdiDeviceName, 0, ref dd, 0))
            {
                return dd.DeviceKey;
            }

            return null;
        }

        private static bool ApplyColorFile(string deviceName, AdapterLuid adapterId, uint sourceId, string filename)
        {
            try
            {
                string colorDir = ColorProfileCatalog.GetSystemColorDirectory();
                string fullPath = Path.Combine(colorDir, filename);

                if (!File.Exists(fullPath))
                {
                    _logger.Warn($"Color profile '{filename}' not found in {colorDir} — skipping");
                    return false;
                }

                string registryKey = GetDisplayRegistryKey(deviceName);
                if (registryKey != null)
                {
                    WcsGetUsePerUserProfiles(registryKey, DeviceClassFlags.Monitor, out bool usePerUser);
                    if (!usePerUser)
                    {
                        _logger.Debug($"ColorProfileSetDisplayDefaultAssociation: device='{deviceName}' profile='{filename}'");
                        WcsSetUsePerUserProfiles(registryKey, DeviceClassFlags.Monitor, true);
                    }
                }

                _logger.Debug($"ColorProfileSetDisplayDefaultAssociation: device='{deviceName}' profile='{filename}'");

                int result = ColorProfileSetDisplayDefaultAssociation(
                    WcsProfileManagementScope.CurrentUser,
                    filename,
                    ColorProfileType.Icc,
                    ColorProfileSubtype.StandardDisplayColorMode,
                    DisplayConfigNative.ToNative(adapterId),
                    sourceId);
                if (result == 0)
                {
                    _logger.Info($"Set color profile '{filename}' on {deviceName}");
                    return true;
                }

                _logger.Error($"ColorProfileSetDisplayDefaultAssociation failed for {deviceName}: error {result}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error applying color profile '{filename}' to {deviceName}");
                return false;
            }
        }

        #endregion

        #region Public Methods

        public static string GetDisplayDefaultColorProfile(AdapterLuid adapterId, uint sourceId)
        {
            try
            {
                int hr = ColorProfileGetDisplayDefault(
                    WcsProfileManagementScope.CurrentUser,
                    DisplayConfigNative.ToNative(adapterId),
                    sourceId,
                    ColorProfileType.Icc,
                    ColorProfileSubtype.StandardDisplayColorMode,
                    out IntPtr namePtr);
                if (hr != 0 || namePtr == IntPtr.Zero)
                {
                    hr = ColorProfileGetDisplayDefault(
                        WcsProfileManagementScope.SystemWide,
                        DisplayConfigNative.ToNative(adapterId),
                        sourceId,
                        ColorProfileType.Icc,
                        ColorProfileSubtype.StandardDisplayColorMode,
                        out namePtr);
                }

                if (hr != 0 || namePtr == IntPtr.Zero) { return null; }

                try
                {
                    string fullPath = Marshal.PtrToStringUni(namePtr);
                    return string.IsNullOrEmpty(fullPath) ? null : Path.GetFileName(fullPath);
                }
                finally
                {
                    LocalFree(namePtr);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get display default color profile for adapter {adapterId.LowPart}");
                return null;
            }
        }

        public static bool ApplyColorProfile(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs)
        {
            if (string.IsNullOrEmpty(setting.ColorProfile) || !setting.IsEnabled)
            {
                return true;
            }

            var targetKey = CcdAddress.Target(setting.AdapterLuid, setting.TargetId);
            var live = liveConfigs?.FirstOrDefault(config => CcdAddress.Target(config).Equals(targetKey));
            if (live == null)
            {
                _logger.Warn($"Could not find active display matching target {targetKey} to apply color profile");
                return false;
            }

            return ApplyColorFile(live.DeviceName, live.AdapterId, live.SourceId, setting.ColorProfile);
        }

        #endregion

        public static bool ApplyColorProfiles(List<DisplayConfigInfo> displayConfigs)
        {
            _logger.Info("Applying color profiles...");
            return ApplyColorProfiles(displayConfigs, DisplayConfigService.GetDisplayConfigs(), ApplyColorProfile);
        }

        internal static bool ApplyColorProfiles(
            List<DisplayConfigInfo> displayConfigs,
            List<DisplayConfigInfo> liveConfigs,
            Func<DisplaySetting, List<DisplayConfigInfo>, bool> applyColorProfile)
        {
            bool allSuccessful = true;

            foreach (var profileDisplay in displayConfigs)
            {
                if (!profileDisplay.IsEnabled || string.IsNullOrEmpty(profileDisplay.ColorProfile)) continue;

                var targetKey = CcdAddress.Target(profileDisplay);
                var activeDisplay = liveConfigs.FirstOrDefault(c => CcdAddress.Target(c).Equals(targetKey));
                if (activeDisplay == null)
                {
                    _logger.Warn($"Could not find active display matching target {targetKey} to apply color profile");
                    allSuccessful = false;
                    continue;
                }

                var setting = new DisplaySetting
                {
                    DeviceName = activeDisplay.DeviceName,
                    AdapterLuid = activeDisplay.AdapterId,
                    TargetId = activeDisplay.TargetId,
                    ColorProfile = profileDisplay.ColorProfile,
                    IsEnabled = profileDisplay.IsEnabled
                };

                if (!applyColorProfile(setting, liveConfigs))
                    allSuccessful = false;
            }

            return allSuccessful;
        }
    }
}
