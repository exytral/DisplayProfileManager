using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.Pointer.Interop
{
    internal static class MouseNative
    {
        #region P/Invoke

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint action, uint uiParam, ref uint pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SystemParametersInfo(uint action, uint uiParam, [In, Out] int[] pvParam, uint winIni);

        [DllImport("user32.dll")]
        internal static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        #endregion

        #region Constants

        internal const uint SpiGetMouse = 0x0003;
        internal const uint SpiSetMouse = 0x0004;
        internal const uint SpiSetDoubleClickTime = 0x0020;
        internal const uint SpiSetMouseButtonSwap = 0x0021;
        internal const uint SpiSetMouseTrails = 0x005D;
        internal const uint SpiGetMouseTrails = 0x005E;
        internal const uint SpiGetWheelScrollLines = 0x0068;
        internal const uint SpiSetWheelScrollLines = 0x0069;
        internal const uint SpiGetWheelScrollChars = 0x006C;
        internal const uint SpiSetWheelScrollChars = 0x006D;
        internal const uint SpiGetMouseSpeed = 0x0070;
        internal const uint SpiSetMouseSpeed = 0x0071;

        internal const uint SpifUpdateIniFileAndSendChange = 0x0003;
        internal const int SmSwapButton = 23;

        // Windows defaults for the two acceleration thresholds when Enhance pointer precision is on
        internal static readonly int[] PrecisionOn = [6, 10, 1];
        internal static readonly int[] PrecisionOff = [0, 0, 0];

        #endregion
    }
}
