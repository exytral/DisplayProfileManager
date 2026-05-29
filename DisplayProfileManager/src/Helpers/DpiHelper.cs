using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Helpers
{
    public class DpiHelper
    {
        private static readonly uint[] DpiVals = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

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

        #region Public Classes

        public class DPIScalingInfo
        {
            public uint Minimum { get; set; } = 100;
            public uint Maximum { get; set; } = 100;
            public uint Current { get; set; } = 100;
            public uint Recommended { get; set; } = 100;
            public bool IsInitialized { get; set; } = false;
            public LUID AdapterId { get; set; }
            public uint SourceId { get; set; }
        }

        #endregion

        #region Public Methods

        public static uint[] GetSupportedDpiScalingOnly(string deviceName)
        {
            DPIScalingInfo dpiInfo = GetDPIScalingInfo(deviceName);
            uint start = dpiInfo.Minimum;
            uint end = dpiInfo.Maximum;
            uint step = 25;
            uint[] dpiValues = Enumerable.Range(0, (int)((end - start) / step) + 1).Select(i => start + (uint)i * step).ToArray();

            return dpiValues;
        }

        public static DPIScalingInfo GetDPIScalingInfo(string deviceName, DisplayConfigHelper.DisplayConfigInfo displayConfig = null)
        {
            DisplayConfigHelper.DisplayConfigInfo foundConfig = displayConfig;
            if (foundConfig == null)
            {
                List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = DisplayConfigHelper.GetDisplayConfigs();
                if (displayConfigs.Count > 0)
                    foundConfig = displayConfigs.Find(x => x.DeviceName == deviceName);
            }

            var dpiInfo = new DPIScalingInfo();

            if (foundConfig != null)
            {
                LUID adapterId = new LUID()
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
                    if (DpiVals.Length >= minAbs + requestPacket.maxScaleRel + 1)
                    {
                        dpiInfo.Current = DpiVals[minAbs + requestPacket.curScaleRel];
                        dpiInfo.Recommended = DpiVals[minAbs];
                        dpiInfo.Maximum = DpiVals[minAbs + requestPacket.maxScaleRel];
                        dpiInfo.Minimum = DpiVals[0];
                        dpiInfo.IsInitialized = true;
                        dpiInfo.AdapterId = adapterId;
                        dpiInfo.SourceId = foundConfig.SourceId;
                    }
                }
            }

            return dpiInfo;
        }


        public static bool SetDPIScaling(string deviceName, uint dpiPercentToSet)
        {
            var dpiScalingInfo = GetDPIScalingInfo(deviceName);
            if (dpiPercentToSet == dpiScalingInfo.Current) return true;

            if (dpiPercentToSet < dpiScalingInfo.Minimum)
                dpiPercentToSet = dpiScalingInfo.Minimum;
            else if (dpiPercentToSet > dpiScalingInfo.Maximum)
                dpiPercentToSet = dpiScalingInfo.Maximum;

            int idx1 = -1, idx2 = -1;

            for (int i = 0; i < DpiVals.Length; i++)
            {
                if (DpiVals[i] == dpiPercentToSet)
                    idx1 = i;
                if (DpiVals[i] == dpiScalingInfo.Recommended)
                    idx2 = i;
            }

            if (idx1 == -1 || idx2 == -1) return false;

            int dpiRelativeVal = idx1 - idx2;
            var setPacket = new DisplayConfigSourceDpiScaleSet
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigDeviceInfoTypeCustom.SetDpiScale,
                    size = (uint)Marshal.SizeOf<DisplayConfigSourceDpiScaleSet>(),
                    adapterId = dpiScalingInfo.AdapterId,
                    id = dpiScalingInfo.SourceId
                },
                scaleRel = dpiRelativeVal
            };

            int result = DisplayConfigSetDeviceInfo(ref setPacket.header);
            return result == 0;
        }

        #endregion
    }
}