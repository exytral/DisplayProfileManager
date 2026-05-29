using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigPathSourceInfoTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void CloneGroupId_DoesNotExistAsProperty()
        {
            var prop = typeof(DisplayConfigHelper.DisplayConfigPathSourceInfo)
                .GetProperty("CloneGroupId", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNull(prop,
                "CloneGroupId must not exist — clone group is encoded in the lower 16 bits of modeInfoIdx.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModeInfoIdx_Lower16Bits_IsCloneGroupId()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo { modeInfoIdx = 0xABCD_1234 };

            Assert.AreEqual(0x1234u, src.modeInfoIdx & 0xFFFF);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModeInfoIdx_Upper16Bits_IsSourceModeIndex()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo { modeInfoIdx = 0xABCD_1234 };

            Assert.AreEqual(0xABCDu, src.modeInfoIdx >> 16);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModeInfoIdx_InvalidSourceModeIndex_Is0xFFFF()
        {
            const uint DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID = 0xFFFF;

            Assert.AreEqual(0xFFFFu, DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_SetsUpperBitsToInvalid()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            src.ResetModeAndSetCloneGroup(2);

            Assert.AreEqual(0xFFFFu, src.modeInfoIdx >> 16,
                "Upper 16 bits must be 0xFFFF (invalid source mode index) after ResetModeAndSetCloneGroup.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_SetsLowerBitsToCloneGroup()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            src.ResetModeAndSetCloneGroup(5);

            Assert.AreEqual(5u, src.modeInfoIdx & 0xFFFF);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_ProducesExpectedRawValue()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            src.ResetModeAndSetCloneGroup(3);

            Assert.AreEqual(0xFFFF_0003u, src.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_CalledTwice_ReplacesNotAccumulates()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            src.ResetModeAndSetCloneGroup(1);
            src.ResetModeAndSetCloneGroup(7);

            Assert.AreEqual(0xFFFF_0007u, src.modeInfoIdx,
                "Second call must overwrite completely, not OR into the previous value.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_ZeroGroup_ProducesInvalidIndexOnly()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            src.ResetModeAndSetCloneGroup(0);

            Assert.AreEqual(0xFFFF_0000u, src.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DirectAssignment_OverwritesEntireField()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo { modeInfoIdx = 0xFFFF_0003 };

            src.modeInfoIdx = 2;

            Assert.AreEqual(2u, src.modeInfoIdx,
                "Direct assignment must overwrite the entire modeInfoIdx field.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DirectAssignment_WithZero_ClearsEntireField()
        {
            var src = new DisplayConfigHelper.DisplayConfigPathSourceInfo { modeInfoIdx = 0xFFFF_0007 };

            src.modeInfoIdx = 0;

            Assert.AreEqual(0u, src.modeInfoIdx);
        }
    }
}