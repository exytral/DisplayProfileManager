using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class EdidDecodeTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeEdidManufacturer_ValidId_DecodesThreeLetterCode()
        {
            var manufacturer = DisplayConfigService.DecodeEdidManufacturer(0x2E34);

            Assert.AreEqual("MAN", manufacturer);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeEdidManufacturer_AnotherValidId_DecodesThreeLetterCode()
        {
            var manufacturer = DisplayConfigService.DecodeEdidManufacturer(0xB610);

            Assert.AreEqual("DEV", manufacturer);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeEdidManufacturer_Zero_ReturnsEmptyString()
        {
            var manufacturer = DisplayConfigService.DecodeEdidManufacturer(0);

            Assert.AreEqual(string.Empty, manufacturer);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DecodeEdidManufacturer_OutOfRangeLetterCode_ReturnsEmptyString()
        {
            // Encodes first 5-bit letter field of 0, which is outside the valid A-Z (1-26) range
            var manufacturer = DisplayConfigService.DecodeEdidManufacturer(0x2100);

            Assert.AreEqual(string.Empty, manufacturer);
        }
    }
}
