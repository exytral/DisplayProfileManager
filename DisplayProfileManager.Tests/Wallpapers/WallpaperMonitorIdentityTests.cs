using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperMonitorIdentityTests
    {
        private static DisplayConfigInfo Display(string device, string path, uint target = 1) => new()
        {
            DeviceName = device,
            MonitorDevicePath = path,
            TargetId = target,
            IsEnabled = true,
        };

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMap_JoinsGdiNameThroughExactCcdTargetPath_NotEnumerationOrder()
        {
            string monitorA = @"\\?\DISPLAY#AAA0001#UID11";
            string monitorB = @"\\?\DISPLAY#BBB0002#UID22";
            var ccd = new[]
            {
                Display(@"\\.\DISPLAY1", monitorA, 11),
                Display(@"\\.\DISPLAY2", monitorB, 22),
            };

            var map = WallpaperMonitorMapper.BuildMonitorMapFromCcd(ccd, [monitorB, monitorA]);

            Assert.AreEqual(monitorA, map[@"\\.\DISPLAY1"]);
            Assert.AreEqual(monitorB, map[@"\\.\DISPLAY2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMap_NormalizesOuterWhitespaceAndMatchesCaseInsensitivelyOnly()
        {
            var map = WallpaperMonitorMapper.BuildMonitorMapFromCcd(
                [Display(@"\\.\DISPLAY2", "  " + @"\\?\DISPLAY#BBB0002#UID22" + "  ")],
                [@"\\?\display#bbb0002#uid22"]);

            Assert.AreEqual(@"\\?\display#bbb0002#uid22", map[@"\\.\DISPLAY2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMap_AmbiguousCcdTargetsForOneGdiSource_AreNotCrossBound()
        {
            var map = WallpaperMonitorMapper.BuildMonitorMapFromCcd(
                [
                    Display(@"\\.\DISPLAY1", @"\\?\DISPLAY#PANELA#UID1", 1),
                    Display(@"\\.\DISPLAY1", @"\\?\DISPLAY#PANELB#UID2", 2),
                ],
                [@"\\?\DISPLAY#PANELA#UID1", @"\\?\DISPLAY#PANELB#UID2"]);

            Assert.IsFalse(map.ContainsKey(@"\\.\DISPLAY1"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_OneResolvedAndOneUnresolvedAssignment_IsPartialNotSuccess()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY1"] = new MonitorWallpaper { Path = @"C:\one.jpg", MonitorId = "MON1" };
            snapshot.PerMonitor[@"\\.\DISPLAY2"] = new MonitorWallpaper { Path = @"C:\two.jpg", MonitorId = "DETACHED" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "MON1" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);
            var outcome = WallpaperApply.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0);

            Assert.AreEqual(1, preflight.DisconnectedCount);
            Assert.IsTrue(preflight.HasUnavailableLiveAssignments);
            Assert.AreEqual(WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_DetachedStoredMonitorId_DoesNotRedirectToDifferentActiveMonitor()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY9"] = new MonitorWallpaper { Path = @"C:\old.jpg", MonitorId = "DETACHED" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "ACTIVE" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(0, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_SavedDeviceNameReusedByDifferentMonitor_DoesNotRedirect()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY1"] = new MonitorWallpaper { Path = @"C:\A.jpg", MonitorId = "PATH-A" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "PATH-B" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(0, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_CapturedMonitorMovedDeviceName_FollowsMonitorIdUniquely()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY1"] = new MonitorWallpaper { Path = @"C:\A.jpg", MonitorId = " path-a " };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = "PATH-B",
                [@"\\.\DISPLAY2"] = "PATH-A"
            };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(@"\\.\DISPLAY2", preflight.Assignments[0].DeviceName);
            Assert.AreEqual("PATH-A", preflight.Assignments[0].MonitorId);
            Assert.AreEqual(@"C:\A.jpg", preflight.Assignments[0].Path);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_DuplicateCapturedMonitorRelationships_FailClosedForBoth()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY1"] = new MonitorWallpaper { Path = @"C:\A.jpg", MonitorId = "PATH-A" };
            snapshot.PerMonitor[@"\\.\DISPLAY2"] = new MonitorWallpaper { Path = @"C:\B.jpg", MonitorId = "path-a" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY9"] = "PATH-A" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(0, preflight.Assignments.Count);
            Assert.AreEqual(2, preflight.DisconnectedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_LegacyNullMonitorId_RetainsDeviceNameFallback()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY1"] = new MonitorWallpaper { Path = @"C:\legacy.jpg", MonitorId = null };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "PATH-B" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(@"\\.\DISPLAY1", preflight.Assignments[0].DeviceName);
            Assert.AreEqual("PATH-B", preflight.Assignments[0].MonitorId);
        }
    }
}
