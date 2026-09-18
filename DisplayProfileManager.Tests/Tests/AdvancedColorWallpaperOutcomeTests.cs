using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AdvancedColorReadPathTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ModernInfo2Failure_DoesNotInvokeLegacyCapabilityMapping()
        {
            bool legacyCalled = false;
            var target = new DisplayConfigHelper.DisplayConfigInfo
            {
                DeviceName = "DISPLAY1",
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = true,
                IsWcgEnabled = true,
            };

            DisplayConfigHelper.CaptureAdvancedColorState(
                target,
                default,
                7,
                useModernContract: true,
                (DisplayConfigHelper.LUID adapterId, uint targetId, out DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2 info) =>
                {
                    info = default;
                    return false;
                },
                (DisplayConfigHelper.LUID adapterId, uint targetId, out DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    legacyCalled = true;
                    info = new DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo
                    {
                        values = DisplayConfigHelper.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                                 DisplayConfigHelper.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                        colorEncoding = DisplayConfigHelper.DisplayConfigColorEncoding.YCbCr444,
                        bitsPerColorChannel = 10,
                    };
                    return true;
                });

            Assert.IsFalse(legacyCalled);
            Assert.IsFalse(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsFalse(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsFalse(target.IsAdvancedColorInfoAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModernUnavailableRead_CannotBeTreatedAsKnownSdrNoOp()
        {
            var desired = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                TargetId = 11,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
            };
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsEnabled = true,
                TargetId = 11,
                RawTargetId = 11,
                FriendlyName = "Unknown Advanced Color",
                IsAdvancedColorInfoAvailable = false,
            };
            bool setterCalled = false;

            bool result = DisplayConfigHelper.ApplyAdvancedColorState(
                new System.Collections.Generic.List<DisplayConfigHelper.DisplayConfigInfo> { desired },
                new System.Collections.Generic.List<DisplayConfigHelper.DisplayConfigInfo> { live },
                true,
                (_, _, _) => { setterCalled = true; return true; },
                (_, _, _) => { setterCalled = true; return true; },
                (_, _, _) => { setterCalled = true; return true; });

            Assert.IsFalse(result);
            Assert.IsFalse(setterCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_RetainsGenericAdvancedColorMapping()
        {
            bool modernCalled = false;
            var target = new DisplayConfigHelper.DisplayConfigInfo();

            DisplayConfigHelper.CaptureAdvancedColorState(
                target,
                default,
                8,
                useModernContract: false,
                (DisplayConfigHelper.LUID adapterId, uint targetId, out DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo2 info) =>
                {
                    modernCalled = true;
                    info = default;
                    return false;
                },
                (DisplayConfigHelper.LUID adapterId, uint targetId, out DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    info = new DisplayConfigHelper.DisplayConfigGetAdvancedColorInfo
                    {
                        values = DisplayConfigHelper.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                                 DisplayConfigHelper.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                        colorEncoding = DisplayConfigHelper.DisplayConfigColorEncoding.YCbCr444,
                        bitsPerColorChannel = 10,
                    };
                    return true;
                });

            Assert.IsFalse(modernCalled);
            Assert.IsTrue(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsTrue(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsTrue(target.IsAdvancedColorInfoAvailable);
        }
    }

    [TestClass]
    public class WallpaperApplyOutcomeTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BackgroundColorFailure_IsHardFailure()
        {
            var outcome = WallpaperHelper.ApplyBackgroundColor( () => throw new InvalidOperationException("background color failed"));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PositionFailure_IsPartialAndCannotReportFullSuccess()
        {
            var outcome = WallpaperHelper.ApplyPosition( () => throw new InvalidOperationException("position failed"));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowOptionsFailure_IsPartialAndCannotReportFullSuccess()
        {
            var outcome = WallpaperHelper.ApplySlideshowOptions( () => throw new InvalidOperationException("options failed"));

            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Partial, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightActivationFailure_IsHardFailure()
        {
            bool modeAttempted = false;

            var outcome = WallpaperHelper.ActivateSpotlight(
                () => false,
                () =>
                {
                    modeAttempted = true;
                    return true;
                });

            Assert.IsTrue(modeAttempted);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SpotlightDeactivationRequiredProviderFailure_IsHardFailure()
        {
            bool providerAttempted = false;

            var outcome = WallpaperHelper.DeactivateSpotlight(
                modeIsSpotlight: false,
                providerEnabled: true,
                resetMode: () => true,
                disableProvider: () =>
                {
                    providerAttempted = true;
                    return false;
                });

            Assert.IsTrue(providerAttempted);
            Assert.AreEqual(WallpaperHelper.WallpaperApplyOutcome.Failed, outcome);
        }
    }
}
