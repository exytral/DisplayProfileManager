using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.UI.Windows
{
    internal readonly record struct QuickSwitchMonitorRect(int Left, int Top, int Right, int Bottom);

    internal readonly record struct QuickSwitchAutoHideEdge(
        IntPtr Hwnd,
        bool GeometryAvailable,
        QuickSwitchMonitorRect Bounds,
        bool UsedExtendedFrameBounds)
    {
        internal bool Present => Hwnd != IntPtr.Zero;
    }

    internal readonly record struct QuickSwitchAutoHideEdges(
        QuickSwitchAutoHideEdge Left,
        QuickSwitchAutoHideEdge Top,
        QuickSwitchAutoHideEdge Right,
        QuickSwitchAutoHideEdge Bottom);

    internal readonly record struct QuickSwitchShellTaskbar(
        IntPtr Hwnd,
        string ClassName,
        bool GeometryAvailable,
        QuickSwitchMonitorRect Bounds,
        bool UsedExtendedFrameBounds,
        QuickSwitchWindowPlacement.AppBarEdge? Edge)
    {
        internal bool Present => Hwnd != IntPtr.Zero;
    }

    internal enum QuickSwitchTargetSource
    {
        Primary,
        MousePosition,
        WindowFocus
    }

    internal readonly record struct QuickSwitchMonitorSelection(
        IntPtr Monitor,
        uint? ForegroundDpi,
        QuickSwitchTargetSource Source,
        bool FellBackToPrimary);

    internal readonly record struct QuickSwitchWorkArea(int Left, int Top, int Right, int Bottom, uint Dpi)
    {
        internal QuickSwitchMonitorRect Monitor { get; init; }
        internal QuickSwitchMonitorRect RawWork { get; init; }
        internal uint? ForegroundDpi { get; init; }
        internal QuickSwitchAutoHideEdges AutoHideEdges { get; init; }
        internal QuickSwitchShellTaskbar ShellTaskbar { get; init; }
        internal QuickSwitchTargetSource TargetSource { get; init; }
        internal bool TargetFallbackToPrimary { get; init; }
    }

    internal readonly record struct QuickSwitchWindowBounds(int X, int Y, int Width, int Height);

    internal static class QuickSwitchWindowPlacement
    {
        internal const double EdgeMarginDip = 12.0;

        private const uint MonitorDefaultToNull = 0;
        private const uint MonitorDefaultToPrimary = 1;
        private const uint MonitorDefaultToNearest = 2;
        private const uint AbmGetAutoHideBarEx = 0x0000000B;
        private const int DwmwaExtendedFrameBounds = 9;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoSize = 0x0001;
        internal const uint SwpNoRedraw = 0x0008;
        internal const uint SwpNoCopyBits = 0x0100;

        private enum MonitorDpiType
        {
            Effective = 0
        }

        internal enum AppBarEdge : uint
        {
            Left = 0,
            Top = 1,
            Right = 2,
            Bottom = 3
        }

        internal delegate QuickSwitchAutoHideEdge AutoHideBarProbe(AppBarEdge edge, QuickSwitchMonitorRect monitor);

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            internal uint Size;
            internal RectNative Monitor;
            internal RectNative Work;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            internal uint Size;
            internal IntPtr Hwnd;
            internal uint CallbackMessage;
            internal uint Edge;
            internal RectNative Rect;
            internal IntPtr Param;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out PointNative point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(PointNative point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            out RectNative value,
            uint size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);

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

        internal static QuickSwitchWorkArea CreateWorkArea(
            QuickSwitchMonitorRect monitor,
            QuickSwitchMonitorRect work,
            uint targetDpi,
            uint? foregroundDpi,
            QuickSwitchAutoHideEdges autoHideEdges,
            QuickSwitchShellTaskbar shellTaskbar = default,
            QuickSwitchTargetSource targetSource = QuickSwitchTargetSource.Primary,
            bool targetFallbackToPrimary = false)
        {
            var effective = ResolveWorkArea(monitor, work, autoHideEdges, shellTaskbar);
            return new QuickSwitchWorkArea(
                effective.Left,
                effective.Top,
                effective.Right,
                effective.Bottom,
                targetDpi == 0 ? 96u : targetDpi)
            {
                Monitor = monitor,
                RawWork = work,
                ForegroundDpi = foregroundDpi,
                AutoHideEdges = autoHideEdges,
                ShellTaskbar = shellTaskbar,
                TargetSource = targetSource,
                TargetFallbackToPrimary = targetFallbackToPrimary
            };
        }

        internal static QuickSwitchMonitorRect ResolveAutoHideWorkArea(
            QuickSwitchMonitorRect monitor,
            QuickSwitchMonitorRect rawWork,
            QuickSwitchAutoHideEdges edges)
        {
            int left = ResolveLeft(monitor, rawWork.Left, edges.Left);
            int top = ResolveTop(monitor, rawWork.Top, edges.Top);
            int right = ResolveRight(monitor, rawWork.Right, edges.Right);
            int bottom = ResolveBottom(monitor, rawWork.Bottom, edges.Bottom);
            return new QuickSwitchMonitorRect(left, top, right, bottom);
        }

        internal static QuickSwitchMonitorRect ResolveWorkArea(
            QuickSwitchMonitorRect monitor,
            QuickSwitchMonitorRect rawWork,
            QuickSwitchAutoHideEdges edges,
            QuickSwitchShellTaskbar shellTaskbar)
        {
            var effective = ResolveAutoHideWorkArea(monitor, rawWork, edges);
            if (!shellTaskbar.GeometryAvailable || !shellTaskbar.Edge.HasValue)
            {
                return effective;
            }

            var shellEdge = new QuickSwitchAutoHideEdge(
                shellTaskbar.Hwnd,
                true,
                shellTaskbar.Bounds,
                shellTaskbar.UsedExtendedFrameBounds);

            return shellTaskbar.Edge.Value switch
            {
                AppBarEdge.Left => effective with { Left = ResolveLeft(monitor, effective.Left, shellEdge) },
                AppBarEdge.Top => effective with { Top = ResolveTop(monitor, effective.Top, shellEdge) },
                AppBarEdge.Right => effective with { Right = ResolveRight(monitor, effective.Right, shellEdge) },
                AppBarEdge.Bottom => effective with { Bottom = ResolveBottom(monitor, effective.Bottom, shellEdge) },
                _ => effective
            };
        }

        internal static QuickSwitchShellTaskbar SelectUniqueShellTaskbar(
            IEnumerable<QuickSwitchShellTaskbar> candidates)
        {
            if (candidates == null)
            {
                return default;
            }

            QuickSwitchShellTaskbar selected = default;
            int count = 0;
            foreach (var candidate in candidates)
            {
                if (!candidate.Present)
                    continue;

                selected = candidate;
                count++;
                if (count > 1)
                {
                    return default;
                }
            }

            return count == 1 ? selected : default;
        }

        internal static QuickSwitchShellTaskbar QualifyShellTaskbar(
            IntPtr hwnd,
            string className,
            QuickSwitchMonitorRect monitor,
            QuickSwitchMonitorRect bounds,
            bool usedExtendedFrameBounds)
        {
            int left = Math.Max(monitor.Left, bounds.Left);
            int top = Math.Max(monitor.Top, bounds.Top);
            int right = Math.Min(monitor.Right, bounds.Right);
            int bottom = Math.Min(monitor.Bottom, bounds.Bottom);
            if (right <= left || bottom <= top)
            {
                return default;
            }

            var intersection = new QuickSwitchMonitorRect(left, top, right, bottom);
            int width = right - left;
            int height = bottom - top;
            if (width == height || (width == monitor.Right - monitor.Left && height == monitor.Bottom - monitor.Top))
            {
                return default;
            }

            AppBarEdge? edge;
            if (width >= height)
            {
                bool topEdge = top == monitor.Top;
                bool bottomEdge = bottom == monitor.Bottom;
                if (topEdge == bottomEdge)
                {
                    return default;
                }
                edge = topEdge ? AppBarEdge.Top : AppBarEdge.Bottom;
            }
            else
            {
                bool leftEdge = left == monitor.Left;
                bool rightEdge = right == monitor.Right;
                if (leftEdge == rightEdge)
                {
                    return default;
                }
                edge = leftEdge ? AppBarEdge.Left : AppBarEdge.Right;
            }

            return new QuickSwitchShellTaskbar(
                hwnd,
                className,
                true,
                intersection,
                usedExtendedFrameBounds,
                edge);
        }

        private static int ResolveLeft(QuickSwitchMonitorRect monitor, int raw, QuickSwitchAutoHideEdge edge)
        {
            if (!edge.GeometryAvailable || !HasVerticalIntersection(monitor, edge.Bounds) ||
                !(edge.Bounds.Left <= monitor.Left && monitor.Left < edge.Bounds.Right))
            {
                return raw;
            }

            int reserve = Math.Min(monitor.Right, edge.Bounds.Right) - monitor.Left;
            return Math.Max(raw, monitor.Left + reserve);
        }

        private static int ResolveTop(QuickSwitchMonitorRect monitor, int raw, QuickSwitchAutoHideEdge edge)
        {
            if (!edge.GeometryAvailable || !HasHorizontalIntersection(monitor, edge.Bounds) ||
                !(edge.Bounds.Top <= monitor.Top && monitor.Top < edge.Bounds.Bottom))
            {
                return raw;
            }

            int reserve = Math.Min(monitor.Bottom, edge.Bounds.Bottom) - monitor.Top;
            return Math.Max(raw, monitor.Top + reserve);
        }

        private static int ResolveRight(QuickSwitchMonitorRect monitor, int raw, QuickSwitchAutoHideEdge edge)
        {
            if (!edge.GeometryAvailable || !HasVerticalIntersection(monitor, edge.Bounds) ||
                !(edge.Bounds.Left < monitor.Right && monitor.Right <= edge.Bounds.Right))
            {
                return raw;
            }

            int reserve = monitor.Right - Math.Max(monitor.Left, edge.Bounds.Left);
            return Math.Min(raw, monitor.Right - reserve);
        }

        private static int ResolveBottom(QuickSwitchMonitorRect monitor, int raw, QuickSwitchAutoHideEdge edge)
        {
            if (!edge.GeometryAvailable || !HasHorizontalIntersection(monitor, edge.Bounds) ||
                !(edge.Bounds.Top < monitor.Bottom && monitor.Bottom <= edge.Bounds.Bottom))
            {
                return raw;
            }

            int reserve = monitor.Bottom - Math.Max(monitor.Top, edge.Bounds.Top);
            return Math.Min(raw, monitor.Bottom - reserve);
        }

        private static bool HasVerticalIntersection(QuickSwitchMonitorRect a, QuickSwitchMonitorRect b) => Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top);

        private static bool HasHorizontalIntersection(QuickSwitchMonitorRect a, QuickSwitchMonitorRect b) => Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left);

        internal static QuickSwitchAutoHideEdges ProbeAutoHideEdges(
            QuickSwitchMonitorRect monitor,
            AutoHideBarProbe probe)
        {
            if (probe == null)
            {
                return default;
            }

            return new QuickSwitchAutoHideEdges(
                probe(AppBarEdge.Left, monitor),
                probe(AppBarEdge.Top, monitor),
                probe(AppBarEdge.Right, monitor),
                probe(AppBarEdge.Bottom, monitor));
        }

        internal static QuickSwitchWindowBounds CalculateBounds(QuickSwitchWorkArea workArea, Size windowSizeDip)
        {
            double scale = Math.Max(1.0, workArea.Dpi / 96.0);
            int width = Math.Max(1, (int)Math.Ceiling(windowSizeDip.Width * scale));
            int height = Math.Max(1, (int)Math.Ceiling(windowSizeDip.Height * scale));
            int margin = Math.Max(1, (int)Math.Round(EdgeMarginDip * scale));

            int x = Math.Max(workArea.Left + margin, workArea.Right - width - margin);
            int y = Math.Max(workArea.Top + margin, workArea.Bottom - height - margin);
            return new QuickSwitchWindowBounds(x, y, width, height);
        }

        internal static QuickSwitchMonitorSelection ResolveTargetMonitor(
            bool useActiveMonitor,
            QuickSwitchActiveMonitorSource activeSource,
            Func<QuickSwitchMonitorSelection> primaryProvider,
            Func<QuickSwitchMonitorSelection> mouseProvider,
            Func<QuickSwitchMonitorSelection> focusProvider)
        {
            QuickSwitchMonitorSelection primary() =>
                primaryProvider?.Invoke() ?? default;

            if (!useActiveMonitor ||
                !Enum.IsDefined(typeof(QuickSwitchActiveMonitorSource), activeSource))
            {
                return primary();
            }

            QuickSwitchMonitorSelection selected = activeSource switch
            {
                QuickSwitchActiveMonitorSource.MousePosition => mouseProvider?.Invoke() ?? default,
                QuickSwitchActiveMonitorSource.WindowFocus => focusProvider?.Invoke() ?? default,
                _ => default
            };

            if (selected.Monitor != IntPtr.Zero)
            {
                return selected;
            }

            var fallback = primary();
            QuickSwitchTargetSource requestedSource = activeSource == QuickSwitchActiveMonitorSource.WindowFocus
                ? QuickSwitchTargetSource.WindowFocus
                : QuickSwitchTargetSource.MousePosition;
            return fallback with
            {
                Source = requestedSource,
                ForegroundDpi = selected.ForegroundDpi,
                FellBackToPrimary = true
            };
        }

        internal static QuickSwitchWorkArea? CaptureTarget(
            bool useActiveMonitor,
            QuickSwitchActiveMonitorSource activeSource)
        {
            try
            {
                QuickSwitchMonitorSelection selection = ResolveTargetMonitor(
                    useActiveMonitor,
                    activeSource,
                    GetPrimarySelection,
                    GetMouseSelection,
                    GetFocusSelection);

                return Capture(
                    selection.Monitor,
                    selection.ForegroundDpi,
                    selection.Source,
                    selection.FellBackToPrimary);
            }
            catch
            {
                return null;
            }
        }

        internal static QuickSwitchWorkArea? CaptureForeground() => CaptureTarget(true, QuickSwitchActiveMonitorSource.WindowFocus);

        private static QuickSwitchMonitorSelection GetPrimarySelection()
        {
            var origin = new PointNative { X = 0, Y = 0 };
            return new QuickSwitchMonitorSelection(
                MonitorFromPoint(origin, MonitorDefaultToPrimary),
                null,
                QuickSwitchTargetSource.Primary,
                false);
        }

        private static QuickSwitchMonitorSelection GetMouseSelection()
        {
            if (!GetCursorPos(out var point))
            {
                return default;
            }

            return new QuickSwitchMonitorSelection(
                MonitorFromPoint(point, MonitorDefaultToNearest),
                null,
                QuickSwitchTargetSource.MousePosition,
                false);
        }

        private static QuickSwitchMonitorSelection GetFocusSelection()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return default;
            }

            return new QuickSwitchMonitorSelection(
                MonitorFromWindow(hwnd, MonitorDefaultToNearest),
                TryGetWindowDpi(hwnd),
                QuickSwitchTargetSource.WindowFocus,
                false);
        }

        internal static QuickSwitchWorkArea? CaptureForWindow(Window window)
        {
            if (window == null) { return null; }

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) { return null; }

                return Capture(
                    MonitorFromWindow(hwnd, MonitorDefaultToNearest),
                    null,
                    QuickSwitchTargetSource.Primary,
                    false);
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryPlace(Window window, QuickSwitchWorkArea workArea)
        {
            if (window == null || window.ActualWidth <= 0 || window.ActualHeight <= 0)
            {
                return false;
            }

            var bounds = CalculateBounds(workArea, new Size(window.ActualWidth, window.ActualHeight));
            return TryPlace(window, bounds);
        }

        internal static uint GetInitialPlacementFlags() => SwpNoActivate | SwpNoZOrder;

        internal static uint GetPreShowParkingFlags() => SwpNoActivate | SwpNoZOrder | SwpNoSize;

        internal static QuickSwitchWindowBounds CalculatePreShowStagingBounds(QuickSwitchWorkArea workArea) => new(workArea.Right, workArea.Bottom, 0, 0);

        internal static bool TryParkOffscreen(Window window, QuickSwitchWorkArea workArea)
        {
            if (window == null)
            {
                return false;
            }

            return TrySetWindowPos(
                window,
                CalculatePreShowStagingBounds(workArea),
                GetPreShowParkingFlags());
        }

        internal static bool TryPlace(Window window, QuickSwitchWindowBounds bounds)
        {
            if (window == null || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            return TrySetWindowPos(window, bounds, GetInitialPlacementFlags());
        }

        internal static bool TryGetWindowBounds(Window window, out QuickSwitchWindowBounds bounds)
        {
            bounds = default;
            if (window == null)
            {
                return false;
            }

            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                {
                    return false;
                }

                bounds = new QuickSwitchWindowBounds(
                    rect.Left,
                    rect.Top,
                    rect.Right - rect.Left,
                    rect.Bottom - rect.Top);
                return bounds.Width > 0 && bounds.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetWindowPos(Window window, QuickSwitchWindowBounds bounds, uint flags)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                return SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    flags);
            }
            catch
            {
                return false;
            }
        }

        private static QuickSwitchWorkArea? Capture(
            IntPtr monitor,
            uint? foregroundDpi,
            QuickSwitchTargetSource targetSource,
            bool targetFallbackToPrimary)
        {
            if (monitor == IntPtr.Zero) { return null; }

            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return null;
            }

            var monitorRect = new QuickSwitchMonitorRect(
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right,
                info.Monitor.Bottom);
            var workRect = new QuickSwitchMonitorRect(
                info.Work.Left,
                info.Work.Top,
                info.Work.Right,
                info.Work.Bottom);
            uint dpi = GetEffectiveDpi(monitor);
            var autoHideEdges = ProbeAutoHideEdges(monitorRect, ProbeAutoHideBar);
            var shellTaskbar = ProbeSelectedMonitorShellTaskbar(monitor, monitorRect);

            return CreateWorkArea(
                monitorRect,
                workRect,
                dpi,
                foregroundDpi,
                autoHideEdges,
                shellTaskbar,
                targetSource,
                targetFallbackToPrimary);
        }

        private static QuickSwitchShellTaskbar ProbeSelectedMonitorShellTaskbar(
            IntPtr selectedMonitor,
            QuickSwitchMonitorRect monitorRect)
        {
            var qualified = new List<QuickSwitchShellTaskbar>();

            void AddCandidate(IntPtr hwnd, string className)
            {
                if (hwnd == IntPtr.Zero ||
                    !IsWindowVisible(hwnd) ||
                    MonitorFromWindow(hwnd, MonitorDefaultToNull) != selectedMonitor ||
                    !TryGetVisibleBounds(hwnd, out var bounds, out bool usedExtendedFrameBounds))
                {
                    return;
                }

                var candidate = QualifyShellTaskbar(
                    hwnd,
                    className,
                    monitorRect,
                    bounds,
                    usedExtendedFrameBounds);
                if (candidate.Present)
                    qualified.Add(candidate);
            }

            AddCandidate(FindWindow("Shell_TrayWnd", null), "Shell_TrayWnd");

            IntPtr previous = IntPtr.Zero;
            while (true)
            {
                IntPtr hwnd = FindWindowEx(IntPtr.Zero, previous, "Shell_SecondaryTrayWnd", null);
                if (hwnd == IntPtr.Zero)
                    break;

                AddCandidate(hwnd, "Shell_SecondaryTrayWnd");
                previous = hwnd;
            }

            return SelectUniqueShellTaskbar(qualified);
        }

        private static uint GetEffectiveDpi(IntPtr monitor)
        {
            try
            {
                if (GetDpiForMonitor(monitor, MonitorDpiType.Effective, out uint dpiX, out _) == 0 && dpiX != 0)
                {
                    return dpiX;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            return 96;
        }

        private static uint? TryGetWindowDpi(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                return dpi == 0 ? null : dpi;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        private static QuickSwitchAutoHideEdge ProbeAutoHideBar(AppBarEdge edge, QuickSwitchMonitorRect monitor)
        {
            try
            {
                var data = new AppBarData
                {
                    Size = (uint)Marshal.SizeOf<AppBarData>(),
                    Edge = (uint)edge,
                    Rect = new RectNative
                    {
                        Left = monitor.Left,
                        Top = monitor.Top,
                        Right = monitor.Right,
                        Bottom = monitor.Bottom
                    }
                };

                UIntPtr result = SHAppBarMessage(AbmGetAutoHideBarEx, ref data);
                if (result == UIntPtr.Zero)
                {
                    return default;
                }

                IntPtr hwnd = unchecked((IntPtr)(long)result.ToUInt64());
                if (TryGetVisibleBounds(hwnd, out var bounds, out bool usedExtendedFrameBounds))
                {
                    return new QuickSwitchAutoHideEdge(hwnd, true, bounds, usedExtendedFrameBounds);
                }

                return new QuickSwitchAutoHideEdge(hwnd, false, default, false);
            }
            catch (DllNotFoundException)
            {
                return default;
            }
            catch (EntryPointNotFoundException)
            {
                return default;
            }
        }

        private static bool TryGetVisibleBounds(
            IntPtr hwnd,
            out QuickSwitchMonitorRect bounds,
            out bool usedExtendedFrameBounds)
        {
            bounds = default;
            usedExtendedFrameBounds = false;

            try
            {
                if (DwmGetWindowAttribute(
                    hwnd,
                    DwmwaExtendedFrameBounds,
                    out var extended,
                    (uint)Marshal.SizeOf<RectNative>()) == 0 &&
                    extended.Right > extended.Left &&
                    extended.Bottom > extended.Top)
                {
                    bounds = new QuickSwitchMonitorRect(
                        extended.Left,
                        extended.Top,
                        extended.Right,
                        extended.Bottom);
                    usedExtendedFrameBounds = true;
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                if (!GetWindowRect(hwnd, out var rect) ||
                    rect.Right <= rect.Left ||
                    rect.Bottom <= rect.Top)
                {
                    return false;
                }

                bounds = new QuickSwitchMonitorRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
