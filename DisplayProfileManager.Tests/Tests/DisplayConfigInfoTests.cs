using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigInfoTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstruction_DeviceNameIsEmpty()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.AreEqual(string.Empty, info.DeviceName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstruction_RotationIsIdentity()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.AreEqual(DisplayConfigHelper.DisplayConfigRotation.Identity, info.Rotation, "Default rotation must be IDENTITY for backward compat with old profiles.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstruction_IsHdrSupportedIsFalse()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.IsFalse(info.IsHdrSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstruction_IsHdrEnabledIsFalse()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.IsFalse(info.IsHdrEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PopulatePreferredResolution_CurrentSignalSizedValue_IsReplacedByPreferredMode()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo
            {
                NativeWidth = 4096,
                NativeHeight = 2160
            };
            var adapter = new DisplayConfigHelper.LUID { LowPart = 7 };

            bool result = DisplayConfigHelper.PopulatePreferredResolution(
                info,
                adapter,
                33,
                (ref DisplayConfigHelper.DisplayConfigTargetPreferredMode preferred) =>
                {
                    preferred.width = 3840;
                    preferred.height = 2160;
                    return 0;
                });

            Assert.IsTrue(result);
            Assert.AreEqual(3840, info.NativeWidth);
            Assert.AreEqual(2160, info.NativeHeight);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PopulatePreferredResolution_QueryFailure_LeavesNativeDimensionsUnknown()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo
            {
                NativeWidth = 4096,
                NativeHeight = 2160
            };

            bool result = DisplayConfigHelper.PopulatePreferredResolution(
                info,
                new DisplayConfigHelper.LUID { LowPart = 7 },
                33,
                (ref DisplayConfigHelper.DisplayConfigTargetPreferredMode preferred) => 31);

            Assert.IsFalse(result);
            Assert.AreEqual(0, info.NativeWidth);
            Assert.AreEqual(0, info.NativeHeight);
        }
    }
}
