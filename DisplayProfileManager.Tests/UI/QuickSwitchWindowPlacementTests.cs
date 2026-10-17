using System;
using System.Windows;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchWindowPlacementTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveTargetMonitor_PrimaryOnly_InvokesOnlyPrimary()
        {
            int primary = 0, mouse = 0, focus = 0;

            var result = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                false,
                QuickSwitchActiveMonitorSource.MousePosition,
                () => { primary++; return new QuickSwitchMonitorSelection(new IntPtr(1), null, QuickSwitchTargetSource.Primary, false); },
                () => { mouse++; throw new AssertFailedException(); },
                () => { focus++; throw new AssertFailedException(); });

            Assert.AreEqual(new IntPtr(1), result.Monitor);
            Assert.AreEqual(1, primary);
            Assert.AreEqual(0, mouse);
            Assert.AreEqual(0, focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveTargetMonitor_MouseAndFocus_DoNotCrossFallback()
        {
            int primary = 0, mouse = 0, focus = 0;

            var mouseSuccess = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                true,
                QuickSwitchActiveMonitorSource.MousePosition,
                () => { primary++; return new QuickSwitchMonitorSelection(new IntPtr(1), null, QuickSwitchTargetSource.Primary, false); },
                () => { mouse++; return new QuickSwitchMonitorSelection(new IntPtr(2), null, QuickSwitchTargetSource.MousePosition, false); },
                () => { focus++; throw new AssertFailedException(); });

            Assert.AreEqual(new IntPtr(2), mouseSuccess.Monitor);
            Assert.AreEqual(0, primary);
            Assert.AreEqual(1, mouse);
            Assert.AreEqual(0, focus);

            primary = mouse = focus = 0;
            var focusFallback = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                true,
                QuickSwitchActiveMonitorSource.WindowFocus,
                () => { primary++; return new QuickSwitchMonitorSelection(new IntPtr(1), null, QuickSwitchTargetSource.Primary, false); },
                () => { mouse++; throw new AssertFailedException(); },
                () => { focus++; return default; });

            Assert.AreEqual(new IntPtr(1), focusFallback.Monitor);
            Assert.AreEqual(QuickSwitchTargetSource.WindowFocus, focusFallback.Source);
            Assert.IsTrue(focusFallback.FellBackToPrimary);
            Assert.AreEqual(1, primary);
            Assert.AreEqual(0, mouse);
            Assert.AreEqual(1, focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveTargetMonitor_WindowFocusSuccess_InvokesFocusOnly()
        {
            int primary = 0, mouse = 0, focus = 0;
            var focusMonitor = new IntPtr(3);

            var result = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                true,
                QuickSwitchActiveMonitorSource.WindowFocus,
                () => { primary++; throw new AssertFailedException(); },
                () => { mouse++; throw new AssertFailedException(); },
                () =>
                {
                    focus++;
                    return new QuickSwitchMonitorSelection(
                        focusMonitor,
                        144,
                        QuickSwitchTargetSource.WindowFocus,
                        false);
                });

            Assert.AreEqual(focusMonitor, result.Monitor);
            Assert.AreEqual(QuickSwitchTargetSource.WindowFocus, result.Source);
            Assert.IsFalse(result.FellBackToPrimary);
            Assert.AreEqual(0, primary);
            Assert.AreEqual(0, mouse);
            Assert.AreEqual(1, focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveTargetMonitor_MousePositionFailure_FallsBackOnlyToPrimary()
        {
            int primary = 0, mouse = 0, focus = 0;
            var primaryMonitor = new IntPtr(1);

            var result = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                true,
                QuickSwitchActiveMonitorSource.MousePosition,
                () =>
                {
                    primary++;
                    return new QuickSwitchMonitorSelection(
                        primaryMonitor,
                        null,
                        QuickSwitchTargetSource.Primary,
                        false);
                },
                () => { mouse++; return default; },
                () => { focus++; throw new AssertFailedException(); });

            Assert.AreEqual(primaryMonitor, result.Monitor);
            Assert.AreEqual(QuickSwitchTargetSource.MousePosition, result.Source);
            Assert.IsTrue(result.FellBackToPrimary);
            Assert.AreEqual(1, primary);
            Assert.AreEqual(1, mouse);
            Assert.AreEqual(0, focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveTargetMonitor_UndefinedSource_FailsDirectlyToPrimary()
        {
            int primary = 0, mouse = 0, focus = 0;

            var result = QuickSwitchWindowPlacement.ResolveTargetMonitor(
                true,
                (QuickSwitchActiveMonitorSource)99,
                () => { primary++; return new QuickSwitchMonitorSelection(new IntPtr(1), null, QuickSwitchTargetSource.Primary, false); },
                () => { mouse++; throw new AssertFailedException(); },
                () => { focus++; throw new AssertFailedException(); });

            Assert.AreEqual(new IntPtr(1), result.Monitor);
            Assert.AreEqual(QuickSwitchTargetSource.Primary, result.Source);
            Assert.IsFalse(result.FellBackToPrimary);
            Assert.AreEqual(1, primary);
            Assert.AreEqual(0, mouse);
            Assert.AreEqual(0, focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ShellTaskbar_AllEdges_TightenInwardWithoutRelaxingRawWork()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var raw = new QuickSwitchMonitorRect(5, 6, 1910, 1070);

            var left = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(1), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(0, 100, 40, 900), true);
            var top = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(2), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(100, 0, 1000, 30), true);
            var right = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(3), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(1880, 100, 1920, 900), true);
            var bottom = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(4), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(100, 1040, 1000, 1080), true);

            Assert.AreEqual(40, QuickSwitchWindowPlacement.ResolveWorkArea(monitor, raw, default, left).Left);
            Assert.AreEqual(30, QuickSwitchWindowPlacement.ResolveWorkArea(monitor, raw, default, top).Top);
            Assert.AreEqual(1880, QuickSwitchWindowPlacement.ResolveWorkArea(monitor, raw, default, right).Right);
            Assert.AreEqual(1040, QuickSwitchWindowPlacement.ResolveWorkArea(monitor, raw, default, bottom).Bottom);

            var weakerBottom = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(5), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(100, 1075, 1000, 1080), true);
            Assert.AreEqual(1070, QuickSwitchWindowPlacement.ResolveWorkArea(monitor, raw, default, weakerBottom).Bottom);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ShellTaskbar_ForeignBoundarySquareAndMultipleCandidates_FailSafe()
        {
            var monitor = new QuickSwitchMonitorRect(1280, 1440, 3840, 2880);

            Assert.IsFalse(QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(1), "Shell_TrayWnd", monitor, new QuickSwitchMonitorRect(5051, 0, 5120, 1440), true).Present);
            Assert.IsFalse(QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(2), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(1280, 1440, 1380, 1540), true).Present);
            Assert.IsFalse(QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(3), "Shell_SecondaryTrayWnd", monitor, monitor, true).Present);

            var a = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(4), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(1280, 2840, 3840, 2880), true);
            var b = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(5), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(1280, 1440, 3840, 1480), true);
            Assert.IsFalse(QuickSwitchWindowPlacement.SelectUniqueShellTaskbar([a, b]).Present);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateWorkArea_ShellTaskbarFeedsSameDpiAndTwelveDipInset()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 2560, 1440);
            var taskbar = QuickSwitchWindowPlacement.QualifyShellTaskbar(
                new IntPtr(1), "Shell_SecondaryTrayWnd", monitor, new QuickSwitchMonitorRect(0, 1400, 2560, 1440), true);

            var work96 = QuickSwitchWindowPlacement.CreateWorkArea(monitor, monitor, 96, null, default, taskbar);
            var work144 = QuickSwitchWindowPlacement.CreateWorkArea(monitor, monitor, 144, null, default, taskbar);
            var size = new Size(360, 250);

            var final96 = QuickSwitchWindowPlacement.CalculateBounds(work96, size);
            var final144 = QuickSwitchWindowPlacement.CalculateBounds(work144, size);

            Assert.AreEqual(12, work96.Bottom - (final96.Y + final96.Height));
            Assert.AreEqual(18, work144.Bottom - (final144.Y + final144.Height));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateWorkArea_TargetMonitorDpiWinsOverForeignForegroundWindowDpi()
        {
            var monitor = new QuickSwitchMonitorRect(1920, 0, 4480, 1440);
            var work = new QuickSwitchMonitorRect(1920, 0, 4480, 1400);

            var result = QuickSwitchWindowPlacement.CreateWorkArea(
                monitor,
                work,
                targetDpi: 144,
                foregroundDpi: 96,
                default);

            Assert.AreEqual(144u, result.Dpi);
            Assert.AreEqual(96u, result.ForegroundDpi);
            Assert.AreEqual(work.Left, result.Left);
            Assert.AreEqual(work.Top, result.Top);
            Assert.AreEqual(work.Right, result.Right);
            Assert.AreEqual(work.Bottom, result.Bottom);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProbeAutoHideEdges_QueriesAllEdgesForExactMonitor()
        {
            var monitor = new QuickSwitchMonitorRect(1920, 0, 4480, 1440);
            var seen = new System.Collections.Generic.List<QuickSwitchWindowPlacement.AppBarEdge>();

            var edges = QuickSwitchWindowPlacement.ProbeAutoHideEdges(
                monitor,
                (edge, probedMonitor) =>
                {
                    Assert.AreEqual(monitor, probedMonitor);
                    seen.Add(edge);
                    return edge == QuickSwitchWindowPlacement.AppBarEdge.Bottom
                        ? new QuickSwitchAutoHideEdge(new System.IntPtr(42), true, new QuickSwitchMonitorRect(1920, 1438, 4480, 1440), true)
                        : default;
                });

            CollectionAssert.AreEqual(
                new[]
                {
                    QuickSwitchWindowPlacement.AppBarEdge.Left,
                    QuickSwitchWindowPlacement.AppBarEdge.Top,
                    QuickSwitchWindowPlacement.AppBarEdge.Right,
                    QuickSwitchWindowPlacement.AppBarEdge.Bottom
                },
                seen);
            Assert.IsFalse(edges.Left.Present);
            Assert.IsFalse(edges.Top.Present);
            Assert.IsFalse(edges.Right.Present);
            Assert.IsTrue(edges.Bottom.Present);
            Assert.IsTrue(edges.Bottom.GeometryAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveAutoHideWorkArea_AllTouchingEdges_ReserveMeasuredIntersection()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var raw = monitor;
            var edges = new QuickSwitchAutoHideEdges(
                new QuickSwitchAutoHideEdge(new System.IntPtr(1), true, new QuickSwitchMonitorRect(0, 100, 2, 900), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(2), true, new QuickSwitchMonitorRect(100, 0, 1000, 3), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(3), true, new QuickSwitchMonitorRect(1918, 100, 1920, 900), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(4), true, new QuickSwitchMonitorRect(100, 1077, 1000, 1080), true));

            var resolved = QuickSwitchWindowPlacement.ResolveAutoHideWorkArea(monitor, raw, edges);

            Assert.AreEqual(new QuickSwitchMonitorRect(2, 3, 1918, 1077), resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveAutoHideWorkArea_AdjacentOrNonIntersectingGeometry_IsRejected()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var raw = monitor;
            var edges = new QuickSwitchAutoHideEdges(
                new QuickSwitchAutoHideEdge(new System.IntPtr(1), true, new QuickSwitchMonitorRect(-4, 1200, 2, 1300), true),
                default,
                new QuickSwitchAutoHideEdge(new System.IntPtr(2), true, new QuickSwitchMonitorRect(1920, 0, 1922, 1080), true),
                default);

            var resolved = QuickSwitchWindowPlacement.ResolveAutoHideWorkArea(monitor, raw, edges);

            Assert.AreEqual(raw, resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveAutoHideWorkArea_NeverRelaxesStrongerRawReservation()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var raw = new QuickSwitchMonitorRect(10, 20, 1880, 1040);
            var edges = new QuickSwitchAutoHideEdges(
                new QuickSwitchAutoHideEdge(new System.IntPtr(1), true, new QuickSwitchMonitorRect(0, 0, 2, 1080), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(2), true, new QuickSwitchMonitorRect(0, 0, 1920, 3), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(3), true, new QuickSwitchMonitorRect(1918, 0, 1920, 1080), true),
                new QuickSwitchAutoHideEdge(new System.IntPtr(4), true, new QuickSwitchMonitorRect(0, 1077, 1920, 1080), true));

            var resolved = QuickSwitchWindowPlacement.ResolveAutoHideWorkArea(monitor, raw, edges);

            Assert.AreEqual(raw, resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResolveAutoHideWorkArea_GeometryUnavailable_PreservesRawWork()
        {
            var monitor = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var raw = new QuickSwitchMonitorRect(0, 0, 1920, 1080);
            var edges = new QuickSwitchAutoHideEdges(
                default,
                default,
                new QuickSwitchAutoHideEdge(new System.IntPtr(3), false, default, false),
                default);

            var resolved = QuickSwitchWindowPlacement.ResolveAutoHideWorkArea(monitor, raw, edges);

            Assert.AreEqual(raw, resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CalculateBounds_PrimaryScale_AnchorsBottomRightInsideWorkArea()
        {
            var workArea = new QuickSwitchWorkArea(0, 0, 1920, 1040, 96);

            var result = QuickSwitchWindowPlacement.CalculateBounds(workArea, new Size(340, 500));

            Assert.AreEqual(1568, result.X);
            Assert.AreEqual(528, result.Y);
            Assert.AreEqual(340, result.Width);
            Assert.AreEqual(500, result.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CalculateBounds_TaskbarReservedWorkArea_AppliesMarginInsideRcWork()
        {
            var workArea = new QuickSwitchWorkArea(0, 0, 1920, 1040, 96);

            var result = QuickSwitchWindowPlacement.CalculateBounds(workArea, new Size(360, 480));

            Assert.AreEqual(workArea.Right - 12, result.X + result.Width);
            Assert.AreEqual(workArea.Bottom - 12, result.Y + result.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CalculateBounds_MixedDpiSecondary_UsesTargetMonitorDpiAndWorkArea()
        {
            var workArea = new QuickSwitchWorkArea(1920, 0, 4480, 1400, 144);

            var result = QuickSwitchWindowPlacement.CalculateBounds(workArea, new Size(340, 400));

            Assert.AreEqual(3952, result.X);
            Assert.AreEqual(782, result.Y);
            Assert.AreEqual(510, result.Width);
            Assert.AreEqual(600, result.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CalculateBounds_WindowLargerThanWorkArea_ClampsToTopLeftMargin()
        {
            var workArea = new QuickSwitchWorkArea(100, 200, 500, 600, 96);

            var result = QuickSwitchWindowPlacement.CalculateBounds(workArea, new Size(800, 700));

            Assert.AreEqual(112, result.X);
            Assert.AreEqual(212, result.Y);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreShowStaging_CapturedWorkArea_ParksBeyondBottomRightEdge()
        {
            var workArea = new QuickSwitchWorkArea(1920, -200, 4480, 1240, 144);

            var staging = QuickSwitchWindowPlacement.CalculatePreShowStagingBounds(workArea);

            Assert.AreEqual(workArea.Right, staging.X);
            Assert.AreEqual(workArea.Bottom, staging.Y);
            Assert.AreEqual(0, staging.Width);
            Assert.AreEqual(0, staging.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlacementFlags_InitialOwnsSizeBeforeReveal()
        {
            uint flags = QuickSwitchWindowPlacement.GetInitialPlacementFlags();

            Assert.AreEqual(0x14u, flags);
            Assert.AreEqual(0u, flags & 0x0001u);
            Assert.AreEqual(0u, flags & QuickSwitchWindowPlacement.SwpNoRedraw);
            Assert.AreEqual(0u, flags & QuickSwitchWindowPlacement.SwpNoCopyBits);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PlacementFlags_PreShowParkingAllowsRedrawWithoutNoCopyBits()
        {
            uint flags = QuickSwitchWindowPlacement.GetPreShowParkingFlags();

            Assert.AreEqual(0x15u, flags);
            Assert.AreEqual(0u, flags & QuickSwitchWindowPlacement.SwpNoRedraw);
            Assert.AreEqual(0u, flags & QuickSwitchWindowPlacement.SwpNoCopyBits);
        }

    }
}
