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
    }
}