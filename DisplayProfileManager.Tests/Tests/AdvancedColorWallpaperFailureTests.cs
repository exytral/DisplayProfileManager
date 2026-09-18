using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AdvancedColorCapabilityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_WcgOnlyTarget_ExposesWcgWithoutHdr()
        {
            var mapped = Map(
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported,
                DisplayConfigHelper.DisplayConfigAdvancedColorMode.Wcg);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsTrue(mapped.IsWcgSupported);
            Assert.IsFalse(mapped.IsHdrEnabled);
            Assert.IsTrue(mapped.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_HdrAndWcgCapableTarget_MapsBothCapabilitiesIndependently()
        {
            var mapped = Map(
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported,
                DisplayConfigHelper.DisplayConfigAdvancedColorMode.Hdr);

            Assert.IsTrue(mapped.IsHdrSupported);
            Assert.IsTrue(mapped.IsWcgSupported);
            Assert.IsTrue(mapped.IsHdrEnabled);
            Assert.IsFalse(mapped.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_GenericAdvancedColorBit_AloneDoesNotInventHdrOrWcgCapability()
        {
            var mapped = Map(
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported,
                DisplayConfigHelper.DisplayConfigAdvancedColorMode.Sdr);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsFalse(mapped.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Info2_PolicyLimitedTarget_HidesHdrAndWcgCapabilities()
        {
            var mapped = Map(
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported |
                DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorLimitedByPolicy,
                DisplayConfigHelper.DisplayConfigAdvancedColorMode.Sdr);

            Assert.IsFalse(mapped.IsHdrSupported);
            Assert.IsFalse(mapped.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EditorWcgAvailability_DependsOnWcgCapabilityNotHdrCapability()
        {
            Assert.IsTrue(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: true, isWcgSupported: true));
            Assert.IsFalse(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: true, isWcgSupported: false));
            Assert.IsFalse(DisplaySettingControl.IsWcgControlAvailable(usesDedicatedWcgApi: false, isWcgSupported: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EditorReconciliation_RefreshesIndependentHdrAndWcgCapabilitiesFromLiveTarget()
        {
            var setting = new DisplaySetting { IsHdrSupported = true, IsWcgSupported = false };
            var live = new DisplayConfigHelper.DisplayConfigInfo { IsHdrSupported = false, IsWcgSupported = true };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);

            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorCapabilities_AreLiveOnlyAndDoNotChangeSchema6Json()
        {
            string json = JsonConvert.SerializeObject(new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = true,
                IsWcgEnabled = true
            });

            Assert.IsFalse(json.Contains("isHdrSupported"));
            Assert.IsFalse(json.Contains("isWcgSupported"));
            StringAssert.Contains(json, "isHdrEnabled");
            StringAssert.Contains(json, "isWcgEnabled");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAdvancedColorState_WcgOnlyTarget_CanEnterWcgWithoutHdrSupport()
        {
            var operations = new List<string>();
            var desired = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrEnabled = false,
                IsWcgEnabled = true,
                TargetId = 9,
            };
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                IsHdrSupported = false,
                IsWcgSupported = true,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
                TargetId = 9,
                RawTargetId = 9,
                FriendlyName = "WCG-only",
            };

            bool result = DisplayConfigHelper.ApplyAdvancedColorState(
                new List<DisplayConfigHelper.DisplayConfigInfo> { desired },
                new List<DisplayConfigHelper.DisplayConfigInfo> { live },
                true,
                (_, _, enable) => { operations.Add($"HDR:{enable}"); return true; },
                (_, _, enable) => { operations.Add($"WCG:{enable}"); return true; },
                (_, _, _) => true);

            Assert.IsTrue(result);
            CollectionAssert.AreEqual(new[] { "WCG:True" }, operations);
        }

        private static DisplayConfigHelper.DisplayConfigInfo Map(
            DisplayConfigHelper.DisplayConfigAdvancedColorInfo2Flags flags,
            DisplayConfigHelper.DisplayConfigAdvancedColorMode mode)
        {
            var target = new DisplayConfigHelper.DisplayConfigInfo();
            DisplayConfigHelper.ApplyAdvancedColorInfo2(target, new DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2
            {
                values = flags,
                activeColorMode = mode,
                colorEncoding = DisplayConfigHelper.DisplayConfigColorEncoding.Rgb,
                bitsPerColorChannel = 10,
            });
            return target;
        }
    }

    [TestClass]
    public class WallpaperBackgroundColorOwnershipTests
    {
        [TestMethod]
        [DataRow(WallpaperMode.Solid, true)]
        [DataRow(WallpaperMode.Picture, true)]
        [DataRow(WallpaperMode.Slideshow, true)]
        [DataRow(WallpaperMode.Spotlight, false)]
        [TestCategory("Unit")]
        public void ModeOwnsBackgroundColor_Mode_ReportsSemanticOwnership(WallpaperMode mode, bool expected)
        {
            Assert.AreEqual(expected, WallpaperHelper.ModeOwnsBackgroundColor(mode));
        }
    }

    [TestClass]
    public class WallpaperFailureTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowPreflight_MissingSourcesHasNoResolvableDestination()
        {
            string source = WallpaperHelper.SelectSlideshowSource(new[] { @"C:\MissingA", @"C:\MissingB" }, _ => false);

            Assert.IsNull(source);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_ZeroViableAssignmentsFailsBeforeTransition()
        {
            var snapshot = PictureSnapshot(("DISPLAY1", @"C:\missing.jpg", "MON1"));
            var monitorMap = new Dictionary<string, string> { ["DISPLAY1"] = "MON1" };

            var preflight = WallpaperHelper.PreflightPictureAssignments(snapshot, monitorMap, _ => false);

            Assert.IsFalse(preflight.HasViableAssignments);
            Assert.AreEqual(1, preflight.MissingCount);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: 0, setterFailures: 0));
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

            var preflight = WallpaperHelper.PreflightPictureAssignments(
                snapshot,
                monitorMap,
                path => path.EndsWith("valid.jpg", StringComparison.OrdinalIgnoreCase));

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.MissingCount);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Partial, WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_DisconnectedRequestedAssignmentIsReportedPartial()
        {
            var snapshot = PictureSnapshot(
                ("DISPLAY1", @"C:\valid.jpg", "MON1"),
                ("DISPLAY2", @"C:\other.jpg", "OLD-MON2"));
            var monitorMap = new Dictionary<string, string> { ["DISPLAY1"] = "MON1" };

            var preflight = WallpaperHelper.PreflightPictureAssignments(snapshot, monitorMap, _ => true);

            Assert.AreEqual(1, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
            Assert.AreEqual(0, preflight.MissingCount);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Partial, WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PictureSetter_AllFailuresCannotReportSuccess()
        {
            var snapshot = PictureSnapshot(("DISPLAY1", @"C:\valid.jpg", "MON1"));
            var preflight = WallpaperHelper.PreflightPictureAssignments(
                snapshot,
                new Dictionary<string, string> { ["DISPLAY1"] = "MON1" },
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: 0, setterFailures: 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RequiredSlideshowFailure_StopsBeforeOptionsAndSuccessNotification()
        {
            var plan = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Picture, WallpaperMode.Slideshow);
            var observed = new List<WallpaperHelper.WallpaperTransitionStep>();

            var outcome = WallpaperHelper.ExecuteTransitionSteps(plan, step =>
            {
                observed.Add(step);
                return step == WallpaperHelper.WallpaperTransitionStep.SetSlideshowSource
                    ? WallpaperHelper.WallpaperApplyOutcome.Failed
                    : WallpaperHelper.WallpaperApplyOutcome.Success;
            });

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
            CollectionAssert.Contains(observed, WallpaperHelper.WallpaperTransitionStep.SetSlideshowSource);
            CollectionAssert.DoesNotContain(observed, WallpaperHelper.WallpaperTransitionStep.SetSlideshowOptions);
            CollectionAssert.DoesNotContain(observed, WallpaperHelper.WallpaperTransitionStep.NotifySettings);
        }

        private static WallpaperSettings PictureSnapshot(params (string device, string path, string monitorId)[] entries)
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            foreach (var entry in entries)
            {
                snapshot.PerMonitor[entry.device] = new MonitorWallpaper
                {
                    Path = entry.path,
                    MonitorId = entry.monitorId,
                };
            }
            return snapshot;
        }
    }
}
