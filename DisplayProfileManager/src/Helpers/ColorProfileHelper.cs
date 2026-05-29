using DisplayProfileManager.Core;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    public static class ColorProfileHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        #region P/Invoke

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetColorDirectory(
            string pMachineName,
            System.Text.StringBuilder pBuffer,
            ref uint pdwSize);

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int ColorProfileSetDisplayDefaultAssociation(
            WcsProfileManagementScope scope,
            string profileName,
            ColorProfileType profileType,
            ColorProfileSubtype profileSubType,
            DisplayConfigHelper.LUID targetAdapterId,
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
            DisplayConfigHelper.LUID targetAdapterId,
            uint sourceId,
            ColorProfileType profileType,
            ColorProfileSubtype profileSubType,
            out IntPtr profileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        #endregion

        #region Structs and Enums

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

        #region Private Methods

        private static uint ReadBigEndianUInt32(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string GetDisplayRegistryKey(string gdiDeviceName)
        {
            var dd = new DISPLAY_DEVICE();
            dd.cb = (uint)Marshal.SizeOf(dd);
            if (EnumDisplayDevices(gdiDeviceName, 0, ref dd, 0)) return dd.DeviceKey;

            return null;
        }

        private static bool ApplyColorFile(string deviceName, DisplayConfigHelper.LUID adapterId, uint sourceId, string filename)
        {
            try
            {
                string colorDir = GetSystemColorDirectory();
                string fullPath = Path.Combine(colorDir, filename);

                if (!File.Exists(fullPath))
                {
                    logger.Warn($"Color profile '{filename}' not found in {colorDir} — skipping");
                    return false;
                }

                string registryKey = GetDisplayRegistryKey(deviceName);
                if (registryKey != null)
                {
                    bool usePerUser;
                    WcsGetUsePerUserProfiles(registryKey, DeviceClassFlags.Monitor, out usePerUser);
                    if (!usePerUser)
                    {
                        logger.Debug($"ColorProfileSetDisplayDefaultAssociation: device='{deviceName}' profile='{filename}'");
                        WcsSetUsePerUserProfiles(registryKey, DeviceClassFlags.Monitor, true);
                    }
                }

                logger.Debug($"ColorProfileSetDisplayDefaultAssociation: device='{deviceName}' profile='{filename}'");

                int result = ColorProfileSetDisplayDefaultAssociation(
                    WcsProfileManagementScope.CurrentUser,
                    filename,
                    ColorProfileType.Icc,
                    ColorProfileSubtype.StandardDisplayColorMode,
                    adapterId,
                    sourceId);
                if (result == 0)
                {
                    logger.Info($"Set color profile '{filename}' on {deviceName}");
                    return true;
                }

                logger.Error($"ColorProfileSetDisplayDefaultAssociation failed for {deviceName}: error {result}");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error applying color profile '{filename}' to {deviceName}");
                return false;
            }
        }

        #endregion

        #region Public Methods

        public static string GetSystemColorDirectory()
        {
            uint size = 0;
            GetColorDirectory(null, null, ref size);

            var sb = new System.Text.StringBuilder((int)size + 2);
            if (GetColorDirectory(null, sb, ref size)) return sb.ToString();

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\drivers\color");
        }

        public static IReadOnlyList<string> GetInstalledColorProfilesFiltered(bool hdrOnly)
        {
            try
            {
                string dir = GetSystemColorDirectory();
                if (!Directory.Exists(dir)) return Array.Empty<string>();

                var files = Directory.GetFiles(dir, "*.icc").Concat(Directory.GetFiles(dir, "*.icm")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                return files.Where(f => !hdrOnly || IccProfileIsHdr(f)).Select(Path.GetFileName).ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to enumerate system color profiles");
                return Array.Empty<string>();
            }
        }

        public static bool IccProfileIsHdr(string fullPath)
        {
            try
            {
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 512, useAsync: false))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 132) return false;

                    br.ReadBytes(128);
                    uint tagCount = ReadBigEndianUInt32(br);
                    if (tagCount > 1000) return false;
                    if (fs.Length < 132 + (tagCount * 12L)) return false;

                    const uint SIG_CICP = 0x63696370;
                    const uint SIG_MHC2 = 0x4D484332;
                    uint? cicpOffset = null, cicpSize = null;
                    for (uint i = 0; i < tagCount; i++)
                    {
                        uint sig = ReadBigEndianUInt32(br);
                        uint offset = ReadBigEndianUInt32(br);
                        uint size = ReadBigEndianUInt32(br);

                        if (sig == SIG_MHC2)
                        {
                            return true;
                        }
                        if (sig == SIG_CICP)
                        {
                            cicpOffset = offset; cicpSize = size;
                        }
                    }

                    if (cicpOffset.HasValue && cicpSize >= 12)
                    {
                        fs.Seek(cicpOffset.Value + 8, SeekOrigin.Begin);
                        br.ReadByte();
                        byte transferFunction = br.ReadByte();

                        return transferFunction == 16 || transferFunction == 18;
                    }

                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetDisplayDefaultColorProfile(DisplayConfigHelper.LUID adapterId, uint sourceId)
        {
            try
            {
                IntPtr namePtr;
                int hr = ColorProfileGetDisplayDefault(
                    WcsProfileManagementScope.CurrentUser,
                    adapterId,
                    sourceId,
                    ColorProfileType.Icc,
                    ColorProfileSubtype.StandardDisplayColorMode,
                    out namePtr);
                if (hr != 0 || namePtr == IntPtr.Zero)
                {
                    hr = ColorProfileGetDisplayDefault(
                        WcsProfileManagementScope.SystemWide,
                        adapterId,
                        sourceId,
                        ColorProfileType.Icc,
                        ColorProfileSubtype.StandardDisplayColorMode,
                        out namePtr);
                }

                if (hr != 0 || namePtr == IntPtr.Zero) return null;

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
                logger.Warn(ex, $"Failed to get display default color profile for adapter {adapterId.LowPart}");
                return null;
            }
        }

        public static bool ApplyColorProfile(DisplaySetting setting, List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs)
        {
            if (string.IsNullOrEmpty(setting.ColorProfile) || !setting.IsEnabled) return true;

            return ApplyColorFile(setting.DeviceName, setting.AdapterLuid, setting.SourceId, setting.ColorProfile);
        }

        #endregion
    }
}