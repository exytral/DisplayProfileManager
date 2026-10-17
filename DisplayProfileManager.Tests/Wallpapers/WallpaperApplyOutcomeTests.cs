using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager.Tests.Wallpapers
{
    [TestClass]
    public class WallpaperApplyOutcomeTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BackgroundColorFailure_IsHardFailure()
        {
            var outcome = WallpaperApply.ApplyBackgroundColor( () => throw new InvalidOperationException("background color failed"));

            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PositionFailure_IsPartialAndCannotReportFullSuccess()
        {
            var outcome = WallpaperApply.ApplyPosition( () => throw new InvalidOperationException("position failed"));

            Assert.AreEqual(WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowOptionsFailure_IsPartialAndCannotReportFullSuccess()
        {
            var outcome = WallpaperApply.ApplySlideshowOptions( () => throw new InvalidOperationException("options failed"));

            Assert.AreEqual(WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightActivationFailure_IsHardFailure()
        {
            bool modeAttempted = false;

            var outcome = WallpaperApply.ActivateSpotlight(
                () => false,
                () =>
                {
                    modeAttempted = true;
                    return true;
                });

            Assert.IsTrue(modeAttempted);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightDeactivationRequiredProviderFailure_IsHardFailure()
        {
            bool providerAttempted = false;

            var outcome = WallpaperApply.DeactivateSpotlight(
                modeIsSpotlight: false,
                providerEnabled: true,
                resetMode: () => true,
                disableProvider: () =>
                {
                    providerAttempted = true;
                    return false;
                });

            Assert.IsTrue(providerAttempted);
            Assert.AreEqual(WallpaperApplyOutcome.Failed, outcome);
        }
    }
}
