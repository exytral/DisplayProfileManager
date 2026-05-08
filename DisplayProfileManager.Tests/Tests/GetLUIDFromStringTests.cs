using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class GetLUIDFromStringTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_ValidString_ReturnsCorrectHighPart()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("0000000100000001");

            Assert.AreEqual(1, luid.HighPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_ValidString_ReturnsCorrectLowPart()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("0000000100000002");

            Assert.AreEqual(2u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_AllZeros_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("0000000000000000");

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_MaxValues_ParsesCorrectly()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("7FFFFFFFFFFFFFFF");

            Assert.AreEqual(0x7FFFFFFF, luid.HighPart);
            Assert.AreEqual(0xFFFFFFFFu, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_UppercaseHex_ParsesCorrectly()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("ABCD1234EFAB5678");

            Assert.AreEqual(unchecked((int)0xABCD1234), luid.HighPart);
            Assert.AreEqual(0xEFAB5678u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_NullInput_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString(null);

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_EmptyString_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString(string.Empty);

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_TooShortString_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("000000010000000");

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_TooLongString_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("00000001000000010");

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_NonHexString_ReturnsZeroLUID()
        {
            var luid = DisplayConfigHelper.GetLUIDFromString("GGGGGGGGGGGGGGGG");

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(0u, luid.LowPart);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_RoundTrip_PreservesHighAndLowParts()
        {
            int originalHigh = 0x00000042;
            uint originalLow = 0xDEADBEEF;
            string serialized = $"{originalHigh:X8}{originalLow:X8}";

            var luid = DisplayConfigHelper.GetLUIDFromString(serialized);

            Assert.AreEqual(originalHigh, luid.HighPart, "HighPart must survive the save/load round-trip.");
            Assert.AreEqual(originalLow, luid.LowPart, "LowPart must survive the save/load round-trip.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GetLUIDFromString_ZeroHighPart_RoundTrip()
        {
            string serialized = $"{0:X8}{1u:X8}";

            var luid = DisplayConfigHelper.GetLUIDFromString(serialized);

            Assert.AreEqual(0, luid.HighPart);
            Assert.AreEqual(1u, luid.LowPart);
        }
    }

    [TestClass]
    public class DisplayConfigInfoTests
    {
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
        public void DefaultConstruction_RotationIsIdentity()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.AreEqual(
                DisplayConfigHelper.DISPLAYCONFIG_ROTATION.DISPLAYCONFIG_ROTATION_IDENTITY,
                info.Rotation,
                "Default rotation must be IDENTITY for backward compat with old profiles.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DefaultConstruction_DeviceNameIsEmpty()
        {
            var info = new DisplayConfigHelper.DisplayConfigInfo();

            Assert.AreEqual(string.Empty, info.DeviceName);
        }
    }
}