using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class CloneGroupValidationTests
    {
        private static bool Validate(List<DisplaySetting> settings)
            => DisplayConfigHelper.ValidateCloneGroups(settings);

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_EmptyList_ReturnsTrue()
        {
            Assert.IsTrue(Validate(new List<DisplaySetting>()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_OnlyExtendedDisplays_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithSourceId(1).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_SingleMemberGroup_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_TwoIdenticalMembers_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_ThreeIdenticalMembers_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_MixedCloneAndExtended_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithSourceId(1).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_MultipleValidGroups_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-2").WithSourceId(1).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-2").WithSourceId(1).Build(),
            };

            Assert.IsTrue(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DpiMismatch_ReturnsTrue()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithDpi(100).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithDpi(125).Build(),
            };

            Assert.IsTrue(Validate(settings),
                "DPI mismatch must warn, not fail validation.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentWidth_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(1920, 1080).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(2560, 1080).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentHeight_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(1920, 1080).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(1920, 720).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentFrequency_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithFrequency(60).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithFrequency(144).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentPositionX_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(0,    0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(1920, 0).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentPositionY_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(0, 0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(0, 1080).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_DifferentSourceId_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(1).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_OneValidGroupOneInvalid_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(1920, 1080).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithResolution(1920, 1080).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-2").WithSourceId(1).WithResolution(1920, 1080).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-2").WithSourceId(1).WithResolution(1280, 720).Build(),
            };

            Assert.IsFalse(Validate(settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ValidateCloneGroups_MemberRetainsExtendedPosition_ReturnsFalse()
        {
            var settings = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(0,     0).Build(),
                new DisplaySettingBuilder().WithCloneGroup("clone-1").WithSourceId(0).WithPosition(-1920, 0).Build(),
            };

            Assert.IsFalse(Validate(settings),
                "Clone group members with mismatched positions must fail — one likely retains its extended desktop offset.");
        }
    }
}