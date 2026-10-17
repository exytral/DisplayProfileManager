using System;
using System.Runtime.InteropServices;

namespace DisplayProfileManager.UI.Interop
{
    internal readonly record struct SystemBackdropNativeCallResult(int HResult, bool ManagedFailure)
    {
        internal bool Succeeded => !ManagedFailure && HResult == 0;
    }

    internal readonly record struct SystemBackdropNativeStyleReadResult(bool Succeeded, long Style);

    internal readonly record struct SystemBackdropNativeFrameResult(
        bool StyleReadSucceeded,
        long RawStyle,
        long DesiredStyle,
        bool StyleWriteSucceeded,
        bool FrameChangedSucceeded,
        bool ConfirmedStyleReadSucceeded,
        long ConfirmedStyle)
    {
        internal bool NativeFrameReady => StyleReadSucceeded && StyleWriteSucceeded && FrameChangedSucceeded;
        internal bool HasConfirmedBorder => ConfirmedStyleReadSucceeded && (ConfirmedStyle & SystemBackdrop.WsBorder) != 0;
        internal bool HasConfirmedCaption => ConfirmedStyleReadSucceeded && (ConfirmedStyle & SystemBackdrop.WsCaption) == SystemBackdrop.WsCaption;
        internal bool HasConfirmedThickFrame => ConfirmedStyleReadSucceeded && (ConfirmedStyle & SystemBackdrop.WsThickFrame) != 0;
    }

    internal readonly record struct SystemBackdropFrameMetrics(
        int WindowLeft,
        int WindowTop,
        int WindowRight,
        int WindowBottom,
        int ClientLeft,
        int ClientTop,
        int ClientRight,
        int ClientBottom)
    {
        internal int LeftMargin => ClientLeft - WindowLeft;
        internal int TopMargin => ClientTop - WindowTop;
        internal int RightMargin => WindowRight - ClientRight;
        internal int BottomMargin => WindowBottom - ClientBottom;
    }

    internal static class SystemBackdrop
    {
        internal const long WsBorder = 0x00800000L;
        internal const long WsCaption = 0x00C00000L;
        internal const long WsThickFrame = 0x00040000L;

        private const int MinimumRoundedCornerBuild = 22000;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaVisibleFrameBorderThickness = 37;
        private const int GwlStyle = -16;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        private enum DwmWindowCornerPreference
        {
            Round = 2
        }

        internal delegate int DwmSetInt32Attribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            uint cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            out uint pvAttribute,
            uint cbAttribute);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out RectNative rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RectNative rect, uint points);

        internal static bool CanUseRoundedCorners(int osBuild) => osBuild >= MinimumRoundedCornerBuild;

        internal static SystemBackdropNativeFrameResult TryPrepareQuickSwitchNativeFrame(IntPtr hwnd)
        {
            return TryPrepareQuickSwitchNativeFrameCore(
                () => TryGetWindowStyle(hwnd),
                style => TrySetWindowStyle(hwnd, style),
                flags => SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, flags),
                () => TryGetWindowStyle(hwnd));
        }

        internal static SystemBackdropNativeFrameResult TryPrepareQuickSwitchNativeFrameCore(
            Func<SystemBackdropNativeStyleReadResult> readStyle,
            Func<long, bool> applyStyle,
            Func<uint, bool> frameChanged,
            Func<SystemBackdropNativeStyleReadResult> confirmStyle)
        {
            var initial = InvokeStyleRead(readStyle);
            if (!initial.Succeeded)
            {
                return new SystemBackdropNativeFrameResult(false, 0, 0, false, false, false, 0);
            }

            long desiredStyle = initial.Style | WsBorder;
            bool styleWriteSucceeded = InvokeBool(() => applyStyle(desiredStyle));
            if (!styleWriteSucceeded)
            {
                return new SystemBackdropNativeFrameResult(true, initial.Style, desiredStyle, false, false, false, 0);
            }

            bool frameChangedSucceeded =
                InvokeBool(() => frameChanged(SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged));
            var confirmed = InvokeStyleRead(confirmStyle);

            return new SystemBackdropNativeFrameResult(
                true,
                initial.Style,
                desiredStyle,
                true,
                frameChangedSucceeded,
                confirmed.Succeeded,
                confirmed.Style);
        }

        internal static bool TryGetFrameMetrics(IntPtr hwnd, out SystemBackdropFrameMetrics metrics)
        {
            metrics = default;
            try
            {
                if (!GetWindowRect(hwnd, out var windowRect) || !GetClientRect(hwnd, out var clientRect))
                {
                    return false;
                }
                Marshal.SetLastPInvokeError(0);
                if (MapWindowPoints(hwnd, IntPtr.Zero, ref clientRect, 2) == 0 && Marshal.GetLastPInvokeError() != 0)
                {
                    return false;
                }

                metrics = new SystemBackdropFrameMetrics(
                    windowRect.Left,
                    windowRect.Top,
                    windowRect.Right,
                    windowRect.Bottom,
                    clientRect.Left,
                    clientRect.Top,
                    clientRect.Right,
                    clientRect.Bottom);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryApplyRoundedCorners(IntPtr hwnd) => TryApplyRoundedCorners(hwnd, out _, out _);

        internal static bool TryApplyRoundedCorners(IntPtr hwnd, out int? hResult) => TryApplyRoundedCorners(hwnd, out hResult, out _);

        internal static bool TryApplyRoundedCorners(IntPtr hwnd, out int? hResult, out bool managedFailure)
        {
            hResult = null;
            managedFailure = false;
            if (!CanUseRoundedCorners(Environment.OSVersion.Version.Build))
            {
                return false;
            }

            var result = InvokeNative(() => SetWindowCornerPreferenceCore(
                hwnd,
                (int)DwmWindowCornerPreference.Round,
                DwmSetWindowAttribute));
            hResult = result.HResult;
            managedFailure = result.ManagedFailure;
            return result.Succeeded;
        }

        internal static int SetWindowCornerPreferenceCore(
            IntPtr hwnd,
            int preference,
            DwmSetInt32Attribute setAttribute)
        {
            int value = preference;
            return setAttribute(hwnd, DwmwaWindowCornerPreference, ref value, sizeof(int));
        }

        internal static bool TryGetVisibleFrameBorderThickness(IntPtr hwnd, out uint thickness)
        {
            thickness = 0;
            try
            {
                return DwmGetWindowAttribute(
                    hwnd,
                    DwmwaVisibleFrameBorderThickness,
                    out thickness,
                    (uint)Marshal.SizeOf<uint>()) == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static SystemBackdropNativeStyleReadResult TryGetWindowStyleState(IntPtr hwnd) => InvokeStyleRead(() => TryGetWindowStyle(hwnd));

        internal static bool TryGetWindowCornerPreference(IntPtr hwnd, out uint preference)
        {
            preference = 0;
            try
            {
                return DwmGetWindowAttribute(
                    hwnd,
                    DwmwaWindowCornerPreference,
                    out preference,
                    (uint)Marshal.SizeOf<uint>()) == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static string FormatNativeReadback(bool succeeded, long value, bool hexadecimal = false) =>
            succeeded
                ? hexadecimal ? $"0x{value:X}" : value.ToString()
                : "unavailable";

        internal static uint GetQualifiedVisibleFrameThickness(bool nativeFrameReady, uint visibleFrameThickness) =>
            nativeFrameReady ? visibleFrameThickness : 0;

        internal static double GetWpfOuterBorderThicknessDip(uint visibleFrameThickness, uint dpi, bool highContrast)
        {
            if (visibleFrameThickness > 0)
            {
                return 0.0;
            }

            uint effectiveDpi = dpi == 0 ? 96u : dpi;
            return 96.0 / effectiveDpi;
        }

        private static SystemBackdropNativeStyleReadResult TryGetWindowStyle(IntPtr hwnd)
        {
            Marshal.SetLastPInvokeError(0);
            long style = IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, GwlStyle).ToInt64()
                : unchecked((uint)GetWindowLong32(hwnd, GwlStyle));
            int error = Marshal.GetLastPInvokeError();

            return EvaluateWindowStyleRead(style, error);
        }

        internal static SystemBackdropNativeStyleReadResult EvaluateWindowStyleRead(long style, int lastError) =>
            style != 0 || lastError == 0
                ? new SystemBackdropNativeStyleReadResult(true, style)
                : new SystemBackdropNativeStyleReadResult(false, 0);

        private static bool TrySetWindowStyle(IntPtr hwnd, long style)
        {
            Marshal.SetLastPInvokeError(0);
            if (IntPtr.Size == 8)
            {
                IntPtr result = SetWindowLongPtr64(hwnd, GwlStyle, new IntPtr(style));
                return result != IntPtr.Zero || Marshal.GetLastPInvokeError() == 0;
            }

            int result32 = SetWindowLong32(hwnd, GwlStyle, unchecked((int)style));
            return result32 != 0 || Marshal.GetLastPInvokeError() == 0;
        }

        private static SystemBackdropNativeStyleReadResult InvokeStyleRead(Func<SystemBackdropNativeStyleReadResult> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return new SystemBackdropNativeStyleReadResult(false, 0);
            }
        }

        private static bool InvokeBool(Func<bool> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return false;
            }
        }

        private static SystemBackdropNativeCallResult InvokeNative(Func<int> action)
        {
            try
            {
                return new SystemBackdropNativeCallResult(action(), false);
            }
            catch (Exception ex)
            {
                return new SystemBackdropNativeCallResult(
                    ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005),
                    true);
            }
        }
    }
}
