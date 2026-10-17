using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays.Interop
{
    public class DpiScalingAdapter
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly uint[] _dpiVals = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigDeviceInfoHeader requestPacket);
        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DisplayConfigDeviceInfoHeader setPacket);

        #endregion

        #region Enums

        public enum DisplayConfigDeviceInfoTypeCustom : int
        {
            GetDpiScale = -3,
            SetDpiScale = -4,
            GetMonitorUniqueName = -7
        }

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigDeviceInfoHeader
        {
            public DisplayConfigDeviceInfoTypeCustom type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSourceDpiScaleGet
        {
            public DisplayConfigDeviceInfoHeader header;
            public int minScaleRel;
            public int curScaleRel;
            public int maxScaleRel;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DisplayConfigSourceDpiScaleSet
        {
            public DisplayConfigDeviceInfoHeader header;
            public int scaleRel;
        }

        #endregion

        #region Public Methods

        public static uint[] GetSupportedDpiScalingOnly(string deviceName, DisplayConfigInfo displayConfig = null)
        {
            DpiScalingInfo dpiInfo = GetDPIScalingInfo(deviceName, displayConfig);

            return [.. _dpiVals.Where(v => v >= dpiInfo.Minimum && v <= dpiInfo.Maximum)];
        }

        public static DpiScalingInfo GetDPIScalingInfo(string deviceName, DisplayConfigInfo displayConfig = null)
        {
            DisplayConfigInfo foundConfig = displayConfig;
            if (foundConfig == null)
            {
                List<DisplayConfigInfo> displayConfigs = DisplayConfigService.GetDisplayConfigs();
                if (displayConfigs.Count > 0)
                    foundConfig = displayConfigs.Find(x => x.DeviceName == deviceName);
            }

            var dpiInfo = new DpiScalingInfo();

            if (foundConfig != null)
            {
                LUID adapterId = new()
                {
                    LowPart = foundConfig.AdapterId.LowPart,
                    HighPart = foundConfig.AdapterId.HighPart
                };

                var requestPacket = new DisplayConfigSourceDpiScaleGet
                {
                    header = new DisplayConfigDeviceInfoHeader
                    {
                        type = DisplayConfigDeviceInfoTypeCustom.GetDpiScale,
                        size = (uint)Marshal.SizeOf<DisplayConfigSourceDpiScaleGet>(),
                        adapterId = adapterId,
                        id = foundConfig.SourceId
                    }
                };

                int result = DisplayConfigGetDeviceInfo(ref requestPacket.header);
                if (result == 0)
                {
                    if (requestPacket.curScaleRel < requestPacket.minScaleRel)
                        requestPacket.curScaleRel = requestPacket.minScaleRel;
                    else if (requestPacket.curScaleRel > requestPacket.maxScaleRel)
                        requestPacket.curScaleRel = requestPacket.maxScaleRel;

                    int minAbs = Math.Abs(requestPacket.minScaleRel);
                    if (_dpiVals.Length >= minAbs + requestPacket.maxScaleRel + 1)
                    {
                        dpiInfo.Current = _dpiVals[minAbs + requestPacket.curScaleRel];
                        dpiInfo.Recommended = _dpiVals[minAbs];
                        dpiInfo.Maximum = _dpiVals[minAbs + requestPacket.maxScaleRel];
                        dpiInfo.Minimum = _dpiVals[0];
                        dpiInfo.IsInitialized = true;
                        dpiInfo.AdapterId = new AdapterLuid { LowPart = adapterId.LowPart, HighPart = adapterId.HighPart };
                        dpiInfo.SourceId = foundConfig.SourceId;
                    }
                }
            }

            return dpiInfo;
        }

        public static bool SetDPIScaling(string deviceName, uint dpiPercentToSet, DisplayConfigInfo displayConfig = null)
        {
            var dpiScalingInfo = GetDPIScalingInfo(deviceName, displayConfig);

            // Refuse to write when display scale cannot be read
            if (!dpiScalingInfo.IsInitialized)
            {
                _logger.Warn($"No DPI scaling info for {deviceName} -> scaling left unchanged");
                return false;
            }

            if (dpiPercentToSet == dpiScalingInfo.Current)
            {
                return true;
            }

            if (dpiPercentToSet < dpiScalingInfo.Minimum)
                dpiPercentToSet = dpiScalingInfo.Minimum;
            else if (dpiPercentToSet > dpiScalingInfo.Maximum)
                dpiPercentToSet = dpiScalingInfo.Maximum;

            // Snap unsupported values to nearest supported step
            if (!_dpiVals.Contains(dpiPercentToSet))
            {
                uint nearest = _dpiVals.OrderBy(v => Math.Abs((int)v - (int)dpiPercentToSet)).First();
                _logger.Warn($"{dpiPercentToSet}% is not supported scaling step for {deviceName} -> using {nearest}%");
                dpiPercentToSet = nearest;

                if (dpiPercentToSet == dpiScalingInfo.Current)
                {
                    return true;
                }
            }

            int idx1 = -1, idx2 = -1;

            for (int i = 0; i < _dpiVals.Length; i++)
            {
                if (_dpiVals[i] == dpiPercentToSet)
                    idx1 = i;
                if (_dpiVals[i] == dpiScalingInfo.Recommended)
                    idx2 = i;
            }

            if (idx1 == -1 || idx2 == -1)
            {
                _logger.Warn($"No scaling table entry for {dpiPercentToSet}% or recommended {dpiScalingInfo.Recommended}% on {deviceName}");
                return false;
            }

            int dpiRelativeVal = idx1 - idx2;
            var setPacket = new DisplayConfigSourceDpiScaleSet
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigDeviceInfoTypeCustom.SetDpiScale,
                    size = (uint)Marshal.SizeOf<DisplayConfigSourceDpiScaleSet>(),
                    adapterId = new LUID { LowPart = dpiScalingInfo.AdapterId.LowPart, HighPart = dpiScalingInfo.AdapterId.HighPart },
                    id = dpiScalingInfo.SourceId
                },
                scaleRel = dpiRelativeVal
            };

            int result = DisplayConfigSetDeviceInfo(ref setPacket.header);
            if (result != 0)
                _logger.Warn($"DisplayConfigSetDeviceInfo returned {result} setting {deviceName} to {dpiPercentToSet}%");

            return result == 0;
        }

        #endregion
    }
}
