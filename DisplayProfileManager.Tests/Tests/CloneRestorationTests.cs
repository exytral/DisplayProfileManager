using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.UI.Windows;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class CloneRestorationTests
    {
        // With saved pre-clone state

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_WithSavedState_RestoresPosition()
        {
            var source = new DisplaySettingBuilder().WithPosition(0, 0).WithResolution(1920, 1080).Build();
            var member = new DisplaySettingBuilder()
                .WithSavedPreCloneState(2560, 0, isPrimary: false,
                    width: 2560, height: 1440, frequency: 144, rotation: 1, dpiScaling: 125,
                    hdrEnabled: false, wcgEnabled: false, colorProfile: null)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(2560, member.DisplayPositionX);
            Assert.AreEqual(0, member.DisplayPositionY);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_WithSavedState_RestoresResolutionFrequencyRotationDpi()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithSavedPreCloneState(0, 0, isPrimary: false,
                    width: 2560, height: 1440, frequency: 144, rotation: 2, dpiScaling: 125,
                    hdrEnabled: false, wcgEnabled: false, colorProfile: null)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(2560, member.Width);
            Assert.AreEqual(1440, member.Height);
            Assert.AreEqual(144, member.Frequency);
            Assert.AreEqual(2, member.Rotation);
            Assert.AreEqual(125u, member.DpiScaling);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_WithSavedState_RestoresColorProfileAndAdvancedColor()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithSavedPreCloneState(0, 0, isPrimary: false,
                    width: 1920, height: 1080, frequency: 60, rotation: 1, dpiScaling: 100,
                    hdrEnabled: true, wcgEnabled: true, colorProfile: "sRGB.icm")
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual("sRGB.icm", member.ColorProfile);
            Assert.IsTrue(member.IsHdrEnabled);
            Assert.IsTrue(member.IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_WithSavedPrimaryTrue_SetsPrimary()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithSavedPreCloneState(0, 0, isPrimary: true,
                    width: 1920, height: 1080, frequency: 60, rotation: 1, dpiScaling: 100,
                    hdrEnabled: false, wcgEnabled: false, colorProfile: null)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.IsTrue(member.IsPrimary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_WithSavedPrimaryFalse_ClearsPrimary()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .Primary(true)
                .WithSavedPreCloneState(0, 0, isPrimary: false,
                    width: 1920, height: 1080, frequency: 60, rotation: 1, dpiScaling: 100,
                    hdrEnabled: false, wcgEnabled: false, colorProfile: null)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.IsFalse(member.IsPrimary);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_Always_ClearsOriginalFields()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithSavedPreCloneState(2560, 0, isPrimary: true,
                    width: 2560, height: 1440, frequency: 144, rotation: 1, dpiScaling: 125,
                    hdrEnabled: true, wcgEnabled: true, colorProfile: "sRGB.icm")
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.IsTrue(member.OriginalSettings);
            Assert.IsNull(member.OriginalPositionX);
            Assert.IsNull(member.OriginalPositionY);
            Assert.IsNull(member.OriginalIsPrimary);
            Assert.IsNull(member.OriginalWidth);
            Assert.IsNull(member.OriginalHeight);
            Assert.IsNull(member.OriginalFrequency);
            Assert.IsNull(member.OriginalRotation);
            Assert.IsNull(member.OriginalDpiScaling);
            Assert.IsNull(member.OriginalIsHdrEnabled);
            Assert.IsNull(member.OriginalIsWcgEnabled);
            Assert.IsNull(member.OriginalColorProfile);
        }


        // Without saved pre-clone state (independent layout derived)

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_NoSavedState_FallsBackToNativeResolutionAndPositionRightOfSource()
        {
            var source = new DisplaySettingBuilder().WithPosition(0, 0).WithResolution(1920, 1080).Build();
            var member = new DisplaySettingBuilder()
                .WithNativeResolution(2560, 1440)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(2560, member.Width);
            Assert.AreEqual(1440, member.Height);
            Assert.AreEqual(1920, member.DisplayPositionX);
            Assert.AreEqual(0, member.DisplayPositionY);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_NoSavedState_PicksFirstAvailableRefreshRateForResolution()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithResolution(1920, 1080)
                .WithAvailableRefreshRates(new Dictionary<string, List<int>>
                {
                    ["1920x1080"] = new List<int> { 144, 120, 60 }
                })
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(144, member.Frequency);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_NoSavedState_PicksFirstAvailableDpiScaling()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithAvailableDpiScaling(new List<uint> { 150, 125, 100 })
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(150u, member.DpiScaling);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_NoSavedState_NoNativeResolution_KeepsCurrentResolution()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithResolution(1920, 1080)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(1920, member.Width);
            Assert.AreEqual(1080, member.Height);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RestoreAttachedMemberState_NoSavedState_NoAvailableDpiScaling_KeepsCurrentDpi()
        {
            var source = new DisplaySettingBuilder().Build();
            var member = new DisplaySettingBuilder()
                .WithDpi(100)
                .Build();

            DisplaySettingControl.RestoreAttachedMemberState(member, source);

            Assert.AreEqual(100u, member.DpiScaling);
        }
    }
}
