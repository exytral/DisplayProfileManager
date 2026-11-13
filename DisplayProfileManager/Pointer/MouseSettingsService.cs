using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Pointer.Models;

using static DisplayProfileManager.Pointer.Interop.MouseNative;

namespace DisplayProfileManager.Pointer
{
    // Pointer and wheel settings from the Windows Mouse control panel; changes persist for the current user
    public class MouseSettingsService : IMouseSettingsService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public MouseSetting CaptureCurrent()
        {
            var current = new MouseSetting();

            uint speed = 0;
            if (SystemParametersInfo(SpiGetMouseSpeed, 0, ref speed, 0))
                current.PointerSpeed = (int)speed;

            var acceleration = new int[3];
            if (SystemParametersInfo(SpiGetMouse, 0, acceleration, 0))
                current.EnhancePointerPrecision = acceleration[2] != 0;

            uint lines = 0;
            if (SystemParametersInfo(SpiGetWheelScrollLines, 0, ref lines, 0))
                current.VerticalScrollLines = (int)Math.Clamp(lines, (uint)MouseSetting.MinScrollAmount, (uint)MouseSetting.MaxScrollAmount);

            uint chars = 0;
            if (SystemParametersInfo(SpiGetWheelScrollChars, 0, ref chars, 0))
                current.HorizontalScrollChars = (int)Math.Clamp(chars, (uint)MouseSetting.MinScrollAmount, (uint)MouseSetting.MaxScrollAmount);

            current.SwapButtons = GetSystemMetrics(SmSwapButton) != 0;
            return current;
        }

        public bool ApplyMouseSettings(MouseSetting settings)
        {
            if (settings == null || !settings.AppliesAny()) return true;

            var failures = new List<string>();

            if (settings.ApplyPointerSpeed)
            {
                int speed = Math.Clamp(settings.PointerSpeed, MouseSetting.MinPointerSpeed, MouseSetting.MaxPointerSpeed);
                if (!SystemParametersInfo(SpiSetMouseSpeed, 0, (IntPtr)speed, SpifUpdateIniFileAndSendChange))
                    failures.Add($"pointer speed ({Marshal.GetLastWin32Error()})");
            }

            if (settings.ApplyPointerPrecision)
            {
                int[] acceleration = settings.EnhancePointerPrecision ? [.. PrecisionOn] : [.. PrecisionOff];
                if (!SystemParametersInfo(SpiSetMouse, 0, acceleration, SpifUpdateIniFileAndSendChange))
                    failures.Add($"pointer precision ({Marshal.GetLastWin32Error()})");
            }

            if (settings.ApplyVerticalScroll)
            {
                uint lines = (uint)Math.Clamp(settings.VerticalScrollLines, MouseSetting.MinScrollAmount, MouseSetting.MaxScrollAmount);
                if (!SystemParametersInfo(SpiSetWheelScrollLines, lines, IntPtr.Zero, SpifUpdateIniFileAndSendChange))
                    failures.Add($"scroll lines ({Marshal.GetLastWin32Error()})");
            }

            if (settings.ApplyHorizontalScroll)
            {
                uint chars = (uint)Math.Clamp(settings.HorizontalScrollChars, MouseSetting.MinScrollAmount, MouseSetting.MaxScrollAmount);
                if (!SystemParametersInfo(SpiSetWheelScrollChars, chars, IntPtr.Zero, SpifUpdateIniFileAndSendChange))
                    failures.Add($"horizontal scroll ({Marshal.GetLastWin32Error()})");
            }

            if (settings.ApplySwapButtons)
            {
                if (!SystemParametersInfo(SpiSetMouseButtonSwap, settings.SwapButtons ? 1u : 0u, IntPtr.Zero, SpifUpdateIniFileAndSendChange))
                    failures.Add($"button swap ({Marshal.GetLastWin32Error()})");
            }

            if (failures.Count > 0)
            {
                _logger.Warn($"Mouse settings partly failed: {string.Join(", ", failures)}");
                return false;
            }

            _logger.Info($"Mouse settings applied -> {settings}");
            return true;
        }
    }
}
