using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperOwnershipTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void NonSpotlightPlans_ExplicitlyEstablishNotifyAndVerifyDestinationMode()
        {
            foreach (var mode in new[] { WallpaperMode.Solid, WallpaperMode.Picture, WallpaperMode.Slideshow })
            {
                var steps = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Spotlight, mode).Steps.ToList();
                int establish = steps.IndexOf(WallpaperTransitionStep.EstablishDestinationMode);
                int notify = steps.IndexOf(WallpaperTransitionStep.NotifySettings);
                int verify = steps.IndexOf(WallpaperTransitionStep.VerifyDestinationMode);

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
            var success = WallpaperApply.EstablishDestinationMode(
                WallpaperMode.Solid,
                value => { stored = value; return true; },
                () => stored);
            var mismatch = WallpaperApply.EstablishDestinationMode(
                WallpaperMode.Picture,
                _ => true,
                () => 2);

            Assert.AreEqual(WallpaperApplyOutcome.Success, success);
            Assert.AreEqual(1, stored);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, mismatch);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DestinationMode_VerificationRejectsDifferentDetectedOwner()
        {
            Assert.AreEqual(WallpaperApplyOutcome.Failed, WallpaperApply.VerifyDestinationMode(WallpaperMode.Solid, () => WallpaperMode.Slideshow));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightPlan_DoesNotDisableDesktopBackgroundBeforeProviderAcquisition()
        {
            var steps = WallpaperTransitionPlanner.BuildTransitionPlan(WallpaperMode.Solid, WallpaperMode.Spotlight).Steps.ToList();

            CollectionAssert.DoesNotContain(steps, WallpaperTransitionStep.DisableDesktopBackground);
            CollectionAssert.AreEqual(new[] { WallpaperTransitionStep.ActivateSpotlight }, steps);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightActivation_FinalAuthorityNeverAppears_ReturnsHardFailure()
        {
            string provider = @"C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image_0.jpg";
            var outcome = WallpaperApply.ActivateSpotlightDestination(
                () => true,
                () => true,
                () => true,
                () => true,
                () => true,
                () => false,
                () => provider,
                _ => true);

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightVerification_RequiresProviderModeAndRenderedProviderPath()
        {
            string provider = @"C:\Users\u\AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image.jpg";

            Assert.IsTrue(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [provider]));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [@"C:\Pictures\old.jpg"]));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, []));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, true, [provider]));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightVerification_RejectsAnyAttachedMonitorWithoutProviderRendering()
        {
            string provider = @"C:\Users\u\AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\DesktopSpotlight\Assets\Images\image.jpg";

            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [provider, string.Empty]));
            Assert.IsFalse(WallpaperApply.IsSpotlightDestinationEstablished(3, 1, false, [provider, @"C:\Pictures\stale.jpg"]));
        }
    }
}
