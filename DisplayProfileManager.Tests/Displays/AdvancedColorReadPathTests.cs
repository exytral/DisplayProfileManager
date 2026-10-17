using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class AdvancedColorReadPathTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ModernInfo2Failure_DoesNotInvokeLegacyCapabilityMapping()
        {
            bool legacyCalled = false;
            var target = new DisplayConfigInfo
            {
                DeviceName = "DISPLAY1",
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = true,
                IsWcgEnabled = true,
            };

            AdvancedColorService.CaptureAdvancedColorState(
                target,
                default,
                7,
                useModernContract: true,
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2 info) =>
                {
                    info = default;
                    return false;
                },
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    legacyCalled = true;
                    info = new DisplayConfigNative.DisplayConfigGetAdvancedColorInfo
                    {
                        values = DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                                 DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                        colorEncoding = DisplayConfigNative.DisplayConfigColorEncoding.YCbCr444,
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
            var desired = new DisplayConfigInfo
            {
                IsEnabled = true,
                TargetId = 11,
                IsHdrEnabled = false,
                IsWcgEnabled = false,
            };
            var live = new DisplayConfigInfo
            {
                IsEnabled = true,
                TargetId = 11,
                RawTargetId = 11,
                FriendlyName = "Unknown Advanced Color",
                IsAdvancedColorInfoAvailable = false,
            };
            bool setterCalled = false;

            bool result = AdvancedColorService.ApplyAdvancedColorState(
                [desired],
                [live],
                true,
                (_, _, _) => { setterCalled = true; return true; },
                (_, _, _) => { setterCalled = true; return true; },
                (_, _, _) => { setterCalled = true; return true; });

            Assert.IsFalse(result);
            Assert.IsFalse(setterCalled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_RgbHdrActiveWithoutWideColorEnforced_MapsHdr()
        {
            var target = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);

            Assert.IsTrue(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsTrue(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsTrue(target.IsAdvancedColorInfoAvailable);
            Assert.AreEqual(DisplayConfigNative.DisplayConfigColorEncoding.Rgb, target.ColorEncoding);
            Assert.AreEqual((uint)10, target.BitsPerColorChannel);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_YCbCrHdrActiveWithoutWideColorEnforced_MapsHdr()
        {
            var target = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                DisplayConfigNative.DisplayConfigColorEncoding.YCbCr444);

            Assert.IsTrue(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsTrue(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_WideColorEnforcedProvider_MapsWcgWithoutInventingHdr()
        {
            var target = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.WideColorEnforced |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);

            Assert.IsFalse(target.IsHdrSupported);
            Assert.IsTrue(target.IsWcgSupported);
            Assert.IsFalse(target.IsHdrEnabled);
            Assert.IsTrue(target.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_WideColorEnforcedInactive_MapsSupportedWcgOff()
        {
            var target = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.WideColorEnforced,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);

            Assert.IsFalse(target.IsHdrSupported);
            Assert.IsTrue(target.IsWcgSupported);
            Assert.IsFalse(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_UnsupportedProvider_MapsNeitherAdvancedColorCapability()
        {
            var target = CaptureLegacy(
                0,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);

            Assert.IsFalse(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsFalse(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsTrue(target.IsAdvancedColorInfoAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyContract_ForceDisabledProvider_MapsNeitherConfigurableCapability()
        {
            var target = CaptureLegacy(
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorSupported |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorEnabled |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.WideColorEnforced |
                DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags.AdvancedColorForceDisabled,
                DisplayConfigNative.DisplayConfigColorEncoding.Rgb);

            Assert.IsFalse(target.IsHdrSupported);
            Assert.IsFalse(target.IsWcgSupported);
            Assert.IsFalse(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsTrue(target.IsAdvancedColorInfoAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModernContract_ContinuesUsingInfo2CapabilitiesAndActiveMode()
        {
            bool legacyCalled = false;
            var target = new DisplayConfigInfo();

            AdvancedColorService.CaptureAdvancedColorState(
                target,
                default,
                8,
                useModernContract: true,
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2 info) =>
                {
                    info = new DisplayConfigNative.DisplayConfigGetAdvancedColorInfo2
                    {
                        values = DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.AdvancedColorSupported |
                                 DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.HighDynamicRangeSupported |
                                 DisplayConfigNative.DisplayConfigAdvancedColorInfo2Flags.WideColorSupported,
                        activeColorMode = DisplayConfigNative.DisplayConfigAdvancedColorMode.Hdr,
                        colorEncoding = DisplayConfigNative.DisplayConfigColorEncoding.Rgb,
                        bitsPerColorChannel = 10,
                    };
                    return true;
                },
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    legacyCalled = true;
                    info = default;
                    return false;
                });

            Assert.IsFalse(legacyCalled);
            Assert.IsTrue(target.IsHdrSupported);
            Assert.IsTrue(target.IsWcgSupported);
            Assert.IsTrue(target.IsHdrEnabled);
            Assert.IsFalse(target.IsWcgEnabled);
            Assert.IsTrue(target.IsAdvancedColorInfoAvailable);
        }

        private static DisplayConfigInfo CaptureLegacy(
            DisplayConfigNative.DisplayConfigAdvancedColorInfoFlags flags,
            DisplayConfigNative.DisplayConfigColorEncoding encoding)
        {
            var target = new DisplayConfigInfo();

            AdvancedColorService.CaptureAdvancedColorState(
                target,
                default,
                8,
                useModernContract: false,
                null,
                (DisplayConfigNative.LUID adapterId, uint targetId, out DisplayConfigNative.DisplayConfigGetAdvancedColorInfo info) =>
                {
                    info = new DisplayConfigNative.DisplayConfigGetAdvancedColorInfo
                    {
                        values = flags,
                        colorEncoding = encoding,
                        bitsPerColorChannel = 10,
                    };
                    return true;
                });

            return target;
        }
    }
}
