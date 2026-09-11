using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplayConfigPathSourceInfoTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_GroupId_PacksInvalidSourceIndexAndGroupId()
        {
            var source = new DisplayConfigHelper.DisplayConfigPathSourceInfo();

            source.ResetModeAndSetCloneGroup(5);

            Assert.AreEqual(0xFFFF_0005u, source.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_CalledTwice_ReplacesPreviousUnionValue()
        {
            var source = new DisplayConfigHelper.DisplayConfigPathSourceInfo();
            source.ResetModeAndSetCloneGroup(1);

            source.ResetModeAndSetCloneGroup(7);

            Assert.AreEqual(0xFFFF_0007u, source.modeInfoIdx);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ResetModeAndSetCloneGroup_ZeroGroup_PreservesInvalidSourceIndex()
        {
            var source = new DisplayConfigHelper.DisplayConfigPathSourceInfo();

            source.ResetModeAndSetCloneGroup(0);

            Assert.AreEqual(0xFFFF_0000u, source.modeInfoIdx);
        }
    }
}
