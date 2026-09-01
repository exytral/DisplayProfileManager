using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    public class DisplayHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, ChangeDisplaySettingsFlags flags, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE
        {
            public const int DmPelsWidth = 0x80000;
            public const int DmPelsHeight = 0x100000;
            public const int DmDisplayFrequency = 0x400000;
            public const int DmInterlaced = 0x00000002;
            public const int DmPosition = 0x00000020;
            private const int CchDeviceName = 32;
            private const int CchFormName = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [Flags]
        public enum DisplayDeviceStateFlags : uint
        {
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8,
            VGACompatible = 0x10,
            Removable = 0x20,
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000
        }

        [Flags]
        public enum ChangeDisplaySettingsFlags : uint
        {
            UpdateRegistry = 0x00000001,
            Test = 0x00000002,
            Fullscreen = 0x00000004,
            Global = 0x00000008,
            SetPrimary = 0x00000010,
            VideoParameters = 0x00000020,
            EnableUnsafeModes = 0x00000100,
            DisableUnsafeModes = 0x00000200,
            Reset = 0x40000000,
            ResetEx = 0x20000000,
            NoReset = 0x10000000,
        }
        public enum DispChange : int
        {
            Successful = 0,
            Restart = 1,
            Failed = -1,
            BadMode = -2,
            NotUpdated = -3,
            BadFlags = -4,
            BadParam = -5,
            BadDualView = -6
        }

        #endregion

        #region Constants

        private const int EnumCurrentSettings = -1;

        #endregion

        #region Public Classes

        public class DisplayInfo
        {
            public string DeviceName { get; set; } = string.Empty;
            public string DeviceString { get; set; } = string.Empty;
            public string ReadableDeviceName { get; set; } = string.Empty;
            public string DeviceInstanceId { get; set; } = string.Empty;
            public int Width { get; set; }
            public int Height { get; set; }
            public int Frequency { get; set; }
            public int BitsPerPixel { get; set; }
            public bool IsInterlaced { get; set; }
            public bool IsPrimary { get; set; }
            public DEVMODE DevMode { get; set; }
        }

        public class ResolutionInfo
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Frequency { get; set; }
            public int BitsPerPixel { get; set; }
            public bool IsInterlaced { get; set; }
        }

        public class DisplayCapabilities
        {
            public List<string> Resolutions { get; set; } = new List<string>();
            public Dictionary<string, List<int>> RefreshRates { get; set; } = new Dictionary<string, List<int>>();
        }

        #endregion

        #region Public Methods

        public static List<DisplayInfo> GetDisplays()
        {
            var displays = new List<DisplayInfo>();

            var displayDevice = new DISPLAY_DEVICE();
            displayDevice.cb = Marshal.SizeOf(displayDevice);

            uint deviceIndex = 0;
            while (EnumDisplayDevices(null, deviceIndex, ref displayDevice, 0))
            {
                if ((displayDevice.StateFlags & DisplayDeviceStateFlags.AttachedToDesktop) != 0)
                {
                    var devMode = new DEVMODE();
                    devMode.dmSize = (short)Marshal.SizeOf(devMode);

                    if (EnumDisplaySettings(displayDevice.DeviceName, EnumCurrentSettings, ref devMode))
                    {
                        var displayInfo = new DisplayInfo
                        {
                            DeviceName = displayDevice.DeviceName,
                            DeviceString = displayDevice.DeviceString,
                            ReadableDeviceName = displayDevice.DeviceName,
                            DeviceInstanceId = displayDevice.DeviceID,
                            Width = devMode.dmPelsWidth,
                            Height = devMode.dmPelsHeight,
                            Frequency = devMode.dmDisplayFrequency,
                            BitsPerPixel = devMode.dmBitsPerPel,
                            IsPrimary = (displayDevice.StateFlags & DisplayDeviceStateFlags.PrimaryDevice) != 0,
                            DevMode = devMode
                        };

                        logger.Debug($"Display[{deviceIndex}]: Device={displayDevice.DeviceName}, " + $"String={displayDevice.DeviceString}, DeviceID={displayDevice.DeviceID}, Primary={displayInfo.IsPrimary}");
                        displays.Add(displayInfo);
                    }
                }

                deviceIndex++;
            }

            // Append index of duplicate monitor names
            var nameGroups = displays.GroupBy(d => d.ReadableDeviceName).Where(g => g.Count() > 1);
            foreach (var group in nameGroups)
            {
                int index = 1;
                foreach (var display in group)
                {
                    display.ReadableDeviceName = $"{display.ReadableDeviceName} ({index})";
                    index++;
                }
            }

            return displays;
        }

        public static List<ResolutionInfo> GetAvailableResolutions(string deviceName)
        {
            var resolutions = new List<ResolutionInfo>();
            var uniqueResolutions = new HashSet<string>();

            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);

            int modeIndex = 0;
            while (EnumDisplaySettings(deviceName, modeIndex, ref devMode))
            {
                var resolution = new ResolutionInfo
                {
                    Width = devMode.dmPelsWidth,
                    Height = devMode.dmPelsHeight,
                    Frequency = devMode.dmDisplayFrequency,
                    BitsPerPixel = devMode.dmBitsPerPel,
                    IsInterlaced = (devMode.dmDisplayFlags & DEVMODE.DmInterlaced) != 0
                };

                string key = $"{resolution.Width}x{resolution.Height} • {resolution.Frequency}Hz{(resolution.IsInterlaced ? " • i" : "")}";
                if (!uniqueResolutions.Contains(key))
                {
                    uniqueResolutions.Add(key);
                    resolutions.Add(resolution);
                }

                modeIndex++;
            }

            resolutions.Sort((a, b) =>
            {
                if (a.Width != b.Width)
                {
                    return b.Width.CompareTo(a.Width);
                }

                if (a.Height != b.Height)
                {
                    return b.Height.CompareTo(a.Height);
                }

                return b.Frequency.CompareTo(a.Frequency);
            });

            return resolutions;
        }

        public static List<string> GetSupportedResolutionsOnly(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return new List<string>();

            var allResolutions = GetAvailableResolutions(deviceName);
            var uniqueResolutions = new HashSet<string>();
            var resolutionList = new List<(int width, int height, string text)>();

            foreach (var resolution in allResolutions)
            {
                var resolutionText = $"{resolution.Width}x{resolution.Height}";
                if (!uniqueResolutions.Contains(resolutionText))
                {
                    uniqueResolutions.Add(resolutionText);
                    resolutionList.Add((resolution.Width, resolution.Height, resolutionText));
                }
            }

            resolutionList.Sort((a, b) =>
            {
                if (a.width != b.width)
                {
                    return b.width.CompareTo(a.width);
                }

                return b.height.CompareTo(a.height);
            });

            return resolutionList.Select(r => r.text).ToList();
        }

        public static DisplayCapabilities GetDisplayCapabilities(string deviceName)
        {
            var capabilities = new DisplayCapabilities();
            if (string.IsNullOrEmpty(deviceName))
            {
                return capabilities;
            }

            var allModes = GetAvailableResolutions(deviceName);
            var order = new List<(int width, int height, string text)>();
            var rates = new Dictionary<string, HashSet<int>>();

            foreach (var mode in allModes)
            {
                var text = $"{mode.Width}x{mode.Height}";
                if (!rates.ContainsKey(text))
                {
                    rates[text] = new HashSet<int>();
                    order.Add((mode.Width, mode.Height, text));
                }

                rates[text].Add(mode.Frequency);
            }

            order.Sort((a, b) =>
            {
                if (a.width != b.width)
                {
                    return b.width.CompareTo(a.width);
                }

                return b.height.CompareTo(a.height);
            });

            capabilities.Resolutions = order.Select(r => r.text).ToList();
            foreach (var text in capabilities.Resolutions)
            {
                var sorted = rates[text].ToList();
                sorted.Sort((a, b) => b.CompareTo(a));
                capabilities.RefreshRates[text] = sorted;
            }

            return capabilities;
        }

        public static List<int> GetAvailableRefreshRates(string deviceName, int width, int height)
        {
            if (string.IsNullOrEmpty(deviceName)) return new List<int>();

            var refreshRates = new HashSet<int>();
            var allResolutions = GetAvailableResolutions(deviceName);
            foreach (var resolution in allResolutions)
                if (resolution.Width == width && resolution.Height == height)
                    refreshRates.Add(resolution.Frequency);

            var sortedRates = refreshRates.ToList();
            sortedRates.Sort((a, b) => b.CompareTo(a));

            return sortedRates;
        }

        public static bool IsMonitorConnected(string deviceName)
        {
            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);

            return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode);
        }

        #endregion
    }
}