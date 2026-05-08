using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class DisplaySettingTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void IsPartOfCloneGroup_DefaultConstruction_ReturnsFalse()
        {
            var setting = new DisplaySetting();

            Assert.IsFalse(setting.IsPartOfCloneGroup(),
                "A DisplaySetting with no CloneGroupId must be treated as extended mode.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPartOfCloneGroup_WhenCloneGroupIdIsEmpty_ReturnsFalse()
        {
            var setting = new DisplaySettingBuilder().Build();

            Assert.IsFalse(setting.IsPartOfCloneGroup());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPartOfCloneGroup_WhenCloneGroupIdIsNull_ReturnsFalse()
        {
            var setting = new DisplaySettingBuilder().WithCloneGroup(null).Build();

            Assert.IsFalse(setting.IsPartOfCloneGroup());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void IsPartOfCloneGroup_WhenCloneGroupIdIsSet_ReturnsTrue()
        {
            var setting = new DisplaySettingBuilder().WithCloneGroup("clone-group-1").Build();

            Assert.IsTrue(setting.IsPartOfCloneGroup());
        }
    }
}