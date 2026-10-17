using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperFailureTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowPreflight_MissingSourcesHasNoResolvableDestination()
        {
            string source = WallpaperApply.SelectSlideshowSource([@"C:\MissingA", @"C:\MissingB"], _ => false);

            Assert.IsNull(source);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_ZeroViableAssignmentsFailsBeforeTransition()
        {
            var snapshot = PictureSnapshot(("DISPLAY1", @"C:\missing.jpg", "MON1"));
            var monitorMap = new Dictionary<string, string> { ["DISPLAY1"] = "MON1" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, monitorMap, _ => false);

            Assert.IsFalse(preflight.HasViableAssignments);
            Assert.AreEqual(1, preflight.MissingCount);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, WallpaperApply.ClassifyPictureApplyOutcome(preflight, applied: 0, setterFailures: 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_PartialLiveAvailabilityIsReportedPartial()
        {
            var snapshot = PictureSnapshot(
                ("DISPLAY1", @"C:\valid.jpg", "MON1"),
                ("DISPLAY2", @"C:\missing.jpg", "MON2"));
            var monitorMap = new Dictionary<string, string>
            {
                ["DISPLAY1"] = "MON1",
                ["DISPLAY2"] = "MON2",
            };

            var preflight = WallpaperApply.PreflightPictureAssignments(
                snapshot,
                monitorMap,
                path => path.EndsWith("valid.jpg", StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.MissingCount);
            Assert.AreEqual(WallpaperApplyOutcome.Partial, WallpaperApply.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_DisconnectedRequestedAssignmentIsReportedPartial()
        {
            var snapshot = PictureSnapshot(
                ("DISPLAY1", @"C:\valid.jpg", "MON1"),
                ("DISPLAY2", @"C:\other.jpg", "OLD-MON2"));
            var monitorMap = new Dictionary<string, string> { ["DISPLAY1"] = "MON1" };

            var preflight = WallpaperApply.PreflightPictureAssignments(snapshot, monitorMap, _ => true);

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
            Assert.AreEqual(0, preflight.MissingCount);
            Assert.AreEqual(WallpaperApplyOutcome.Partial, WallpaperApply.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSetter_AllFailuresCannotReportSuccess()
        {
            var snapshot = PictureSnapshot(("DISPLAY1", @"C:\valid.jpg", "MON1"));
            var preflight = WallpaperApply.PreflightPictureAssignments(
                snapshot,
                new Dictionary<string, string> { ["DISPLAY1"] = "MON1" },
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, WallpaperApply.ClassifyPictureApplyOutcome(preflight, applied: 0, setterFailures: 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RequiredSlideshowFailure_StopsBeforeOptionsAndSuccessNotification()
        {
            var plan = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Slideshow);
            var observed = new List<WallpaperTransitionStep>();

            var outcome = WallpaperApply.ExecuteTransitionSteps(plan, step =>
            {
                observed.Add(step);
                return step == WallpaperTransitionStep.SetSlideshowSource
                    ? WallpaperApplyOutcome.Failed
                    : WallpaperApplyOutcome.Success;
            });

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
            CollectionAssert.Contains(observed, WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.DoesNotContain(observed, WallpaperTransitionStep.SetSlideshowOptions);
            CollectionAssert.DoesNotContain(observed, WallpaperTransitionStep.NotifySettings);
        }

        private static WallpaperSettings PictureSnapshot(params (string device, string path, string monitorId)[] entries)
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            foreach (var (device, path, monitorId) in entries)
            {
                snapshot.PerMonitor[device] = new MonitorWallpaper
                {
                    Path = path,
                    MonitorId = monitorId,
                };
            }
            return snapshot;
        }
    }
}
