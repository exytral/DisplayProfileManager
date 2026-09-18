using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class WallpaperMonitorIdentityTests
    {
        private static DisplayConfigHelper.DisplayConfigInfo Display(string device, string path, uint target = 1) => new DisplayConfigHelper.DisplayConfigInfo
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

            var map = WallpaperHelper.BuildMonitorMapFromCcd(ccd, new[] { monitorB, monitorA });

            Assert.AreEqual(monitorA, map[@"\\.\DISPLAY1"]);
            Assert.AreEqual(monitorB, map[@"\\.\DISPLAY2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMap_NormalizesOuterWhitespaceAndMatchesCaseInsensitivelyOnly()
        {
            var map = WallpaperHelper.BuildMonitorMapFromCcd(
                new[] { Display(@"\\.\DISPLAY2", "  " + @"\\?\DISPLAY#BBB0002#UID22" + "  ") },
                new[] { @"\\?\display#bbb0002#uid22" });

            Assert.AreEqual(@"\\?\display#bbb0002#uid22", map[@"\\.\DISPLAY2"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MonitorMap_AmbiguousCcdTargetsForOneGdiSource_AreNotCrossBound()
        {
            var map = WallpaperHelper.BuildMonitorMapFromCcd(
                new[]
                {
                    Display(@"\\.\DISPLAY1", @"\\?\DISPLAY#PANELA#UID1", 1),
                    Display(@"\\.\DISPLAY1", @"\\?\DISPLAY#PANELB#UID2", 2),
                },
                new[] { @"\\?\DISPLAY#PANELA#UID1", @"\\?\DISPLAY#PANELB#UID2" });

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

            var preflight = WallpaperHelper.PreflightPictureAssignments(snapshot, map, _ => true);
            var outcome = WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: 1, setterFailures: 0);

            Assert.AreEqual(1, preflight.DisconnectedCount);
            Assert.IsTrue(preflight.HasUnavailableLiveAssignments);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_DetachedStoredMonitorId_DoesNotRedirectToDifferentActiveMonitor()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY9"] = new MonitorWallpaper { Path = @"C:\old.jpg", MonitorId = "DETACHED" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "ACTIVE" };

            var preflight = WallpaperHelper.PreflightPictureAssignments(snapshot, map, _ => true);

            Assert.AreEqual(0, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PicturePreflight_StaleStoredMonitorIdForDifferentLiveDisplay_DoesNotRedirect()
        {
            var snapshot = new WallpaperSettings { Mode = WallpaperMode.Picture };
            snapshot.PerMonitor[@"\\.\DISPLAY2"] = new MonitorWallpaper { Path = @"C:\B.jpg", MonitorId = "MON1" };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [@"\\.\DISPLAY1"] = "MON1" };

            var preflight = WallpaperHelper.PreflightPictureAssignments(snapshot, map, _ => true);
            var outcome = WallpaperHelper.ClassifyPictureApplyOutcome(preflight, applied: preflight.Assignments.Count, setterFailures: 0);

            Assert.AreEqual(0, preflight.Assignments.Count);
            Assert.AreEqual(1, preflight.DisconnectedCount);
            Assert.IsTrue(preflight.HasUnavailableLiveAssignments);
            Assert.AreNotEqual(WallpaperHelper.WallpaperApplyOutcome.Success, outcome);
        }
    }

    [TestClass]
    public class WallpaperOwnershipTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void NonSpotlightPlans_ExplicitlyEstablishNotifyAndVerifyDestinationMode()
        {
            foreach (var mode in new[] { WallpaperMode.Solid, WallpaperMode.Picture, WallpaperMode.Slideshow })
            {
                var steps = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Spotlight, mode).Steps.ToList();
                int establish = steps.IndexOf(WallpaperHelper.WallpaperTransitionStep.EstablishDestinationMode);
                int notify = steps.IndexOf(WallpaperHelper.WallpaperTransitionStep.NotifySettings);
                int verify = steps.IndexOf(WallpaperHelper.WallpaperTransitionStep.VerifyDestinationMode);

                Assert.IsTrue(establish >= 0, mode.ToString());
                Assert.IsTrue(notify > establish, mode.ToString());
                Assert.IsTrue(verify > notify, mode.ToString());
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DestinationMode_WriteAndReadbackMustBothAgree()
        {
            int? stored = null;
            var success = WallpaperHelper.EstablishDestinationMode(
                WallpaperMode.Solid,
                value => { stored = value; return true; },
                () => stored);
            var mismatch = WallpaperHelper.EstablishDestinationMode(
                WallpaperMode.Picture,
                _ => true,
                () => 2);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Success, success);
            Assert.AreEqual(1, stored);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, mismatch);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DestinationMode_VerificationRejectsDifferentDetectedOwner()
        {
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, WallpaperHelper.VerifyDestinationMode(WallpaperMode.Solid, () => WallpaperMode.Slideshow));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightPlan_DoesNotDisableDesktopBackgroundBeforeProviderAcquisition()
        {
            var steps = WallpaperHelper.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Spotlight).Steps.ToList();

            CollectionAssert.DoesNotContain(steps, WallpaperHelper.WallpaperTransitionStep.DisableDesktopBackground);
            CollectionAssert.AreEqual(new[] { WallpaperHelper.WallpaperTransitionStep.ActivateSpotlight }, steps);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightActivation_FinalAuthorityNeverAppears_ReturnsHardFailure()
        {
            string provider = @"C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image_0.jpg";
            var outcome = WallpaperHelper.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => false,
                () => provider,
                _ => true);

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightVerification_RequiresProviderModeAndRenderedProviderPath()
        {
            string provider = @"C:\Users\u\AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image.jpg";

            Assert.IsTrue(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { provider }));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { @"C:\Pictures\old.jpg" }));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, Array.Empty<string>()));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, true, new[] { provider }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightVerification_RejectsAnyAttachedMonitorWithoutProviderRendering()
        {
            string provider = @"C:\Users\u\AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image.jpg";

            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { provider, string.Empty }));
            Assert.IsFalse(WallpaperHelper.IsSpotlightDestinationEstablished(3, 1, false, new[] { provider, @"C:\Pictures\stale.jpg" }));
        }
    }

    [TestClass]
    public class ProfileEditorAdvancedColorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowEditor_ExposesColorFitmentAndSlideshowControls()
        {
            var state = ProfileEditWindow.GetWallpaperOptionApplicability(WallpaperMode.Slideshow);

            Assert.IsTrue(state.HasColor);
            Assert.IsTrue(state.HasFitment);
            Assert.IsTrue(state.HasSlideshow);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrEditorCycle_PreHdrWcgOff_RestoresOffAfterHdr()
        {
            var enter = DisplaySettingControl.ResolveHdrWcgEditorState(true, false, false, false);
            var exit = DisplaySettingControl.ResolveHdrWcgEditorState(false, true, enter.WcgChecked, enter.RememberedWcg);

            Assert.IsTrue(enter.WcgChecked, "WCG is visually checked while HDR owns the effective destination.");
            Assert.IsFalse(enter.RememberedWcg);
            Assert.IsFalse(exit.WcgChecked);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrEditorCycle_PreHdrWcgOn_RestoresOnAfterHdr()
        {
            var enter = DisplaySettingControl.ResolveHdrWcgEditorState(true, false, true, true);
            var exit = DisplaySettingControl.ResolveHdrWcgEditorState(false, true, enter.WcgChecked, enter.RememberedWcg);

            Assert.IsTrue(enter.WcgChecked);
            Assert.IsTrue(enter.RememberedWcg);
            Assert.IsTrue(exit.WcgChecked);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrSave_RemainsCanonicalHdrWithoutPersistentWcgBit()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
            };

            var saved = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: true, requestedHdrEnabled: true, requestedWcgEnabled: true);

            Assert.IsTrue(saved.IsHdrEnabled);
            Assert.IsFalse(saved.IsWcgEnabled);
        }
    }
}
