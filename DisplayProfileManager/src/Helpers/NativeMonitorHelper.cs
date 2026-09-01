using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace DisplayProfileManager.Helpers
{
    internal static class NativeMonitorHelper
    {
        #region P/Invoke

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        #endregion

        #region Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public uint CbSize;
            public RECT RcMonitor;
            public RECT RcWork;
            public uint DwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string SzDevice;
        }

        #endregion

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        public static bool TryGetMonitorBounds(string deviceName, out Rect bounds)
        {
            bounds = default;
            if (string.IsNullOrEmpty(deviceName))
            {
                return false;
            }

            Rect? match = null;
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                var info = new MONITORINFOEX { CbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hMonitor, ref info) || !string.Equals(info.SzDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                match = new Rect(info.RcMonitor.Left, info.RcMonitor.Top, info.RcMonitor.Right - info.RcMonitor.Left, info.RcMonitor.Bottom - info.RcMonitor.Top);
                return false;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            if (!match.HasValue)
            {
                return false;
            }

            bounds = match.Value;
            return true;
        }
    }
}