using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.Displays;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayGroupBuilderTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_EmptyList_ReturnsEmptyList()
        {
            var groups = DisplayGroupBuilder.GroupDisplaysForUI([]);

            Assert.AreEqual(0, groups.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_IndependentDisplays_RemainSeparateGroups()
        {
            var displays = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").Build(),
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").Build()
            };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreEqual(2, groups.Count);
            Assert.IsFalse(groups[0].IsCloneGroup);
            Assert.IsFalse(groups[1].IsCloneGroup);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_CloneMembers_FormOneGroup()
        {
            var displays = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").WithCloneGroup("group-a").AsCloneSource().Build(),
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").WithCloneGroup("group-a").Build()
            };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreEqual(1, groups.Count);
            Assert.IsTrue(groups[0].IsCloneGroup);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_CloneMembers_AllPresentInAllMembers()
        {
            var displays = new List<DisplaySetting>
            {
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").WithCloneGroup("group-a").AsCloneSource().Build(),
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").WithCloneGroup("group-a").Build(),
                new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY3").WithCloneGroup("group-a").Build()
            };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreEqual(3, groups[0].AllMembers.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_CloneSourceMarked_BecomesRepresentative()
        {
            var source = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").WithCloneGroup("group-a").AsCloneSource().Build();
            var attached = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").WithCloneGroup("group-a").Build();
            var displays = new List<DisplaySetting> { attached, source };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreSame(source, groups[0].RepresentativeSetting);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_NoCloneSourceMarked_FallsBackToFirstMember()
        {
            var first = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").WithCloneGroup("group-a").Build();
            var second = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").WithCloneGroup("group-a").Build();
            var displays = new List<DisplaySetting> { first, second };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreSame(first, groups[0].RepresentativeSetting);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GroupDisplaysForUI_MixedIndependentAndClone_PreservesInputOrderForGroups()
        {
            var independentFirst = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY1").Build();
            var cloneSource = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY2").WithCloneGroup("group-a").AsCloneSource().Build();
            var cloneAttached = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY3").WithCloneGroup("group-a").Build();
            var independentLast = new DisplaySettingBuilder().WithDeviceName("\\\\.\\DISPLAY4").Build();
            var displays = new List<DisplaySetting> { independentFirst, cloneSource, cloneAttached, independentLast };

            var groups = DisplayGroupBuilder.GroupDisplaysForUI(displays);

            Assert.AreEqual(3, groups.Count);
            Assert.AreSame(independentFirst, groups[0].RepresentativeSetting);
            Assert.AreSame(cloneSource, groups[1].RepresentativeSetting);
            Assert.AreSame(independentLast, groups[2].RepresentativeSetting);
        }
    }
}
