using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI;
using DisplayProfileManager.UI.Controls;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class AdvancedColorEditorPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HdrOff_RestoresRememberedWcgIntent()
        {
            var (wcgChecked, rememberedWcg) = DisplaySettingControl.ResolveHdrWcgEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentWcgChecked: true,
                rememberedWcg: false);

            Assert.IsFalse(wcgChecked);
            Assert.IsFalse(rememberedWcg);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrOn_ForcesWcgCheckedWithoutReplacingRememberedIntent()
        {
            var (wcgChecked, rememberedWcg) = DisplaySettingControl.ResolveHdrWcgEditorState(
                hdrOn: true,
                wasHdrOn: false,
                currentWcgChecked: false,
                rememberedWcg: true);

            Assert.IsTrue(wcgChecked);
            Assert.IsFalse(rememberedWcg);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableEnabledHdrForced_UsesNormalControlOpacity()
        {
            var (isEnabled, opacity) = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: true,
                displayEnabled: true);

            Assert.IsFalse(isEnabled);
            Assert.AreEqual(1.0, opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableDisabledHdrForced_PreservesDisplayInactiveOpacity()
        {
            var (isEnabled, opacity) = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: true,
                displayEnabled: false);

            Assert.IsFalse(isEnabled);
            Assert.AreEqual(UiOpacity.Inactive, opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_Unavailable_RemainsBlocked()
        {
            var (isEnabled, opacity) = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: false,
                wcgSupported: true,
                hdrForced: false,
                displayEnabled: true);

            Assert.IsFalse(isEnabled);
            Assert.AreEqual(UiOpacity.Blocked, opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableEnabledHdrOff_IsEnabledAtNormalOpacity()
        {
            var (isEnabled, opacity) = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: false,
                displayEnabled: true);

            Assert.IsTrue(isEnabled);
            Assert.AreEqual(1.0, opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorAvailability_CloneMembersResolveIndependently()
        {
            var availableMember = new DisplaySettingBuilder().WithTargetId(1).Build();
            var unavailableMember = new DisplaySettingBuilder().WithTargetId(2).Build();
            var live = new List<DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithTargetId(1).WithAdvancedColorAvailability(true).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(2).WithAdvancedColorAvailability(false).Build()
            };

            Assert.IsTrue(DisplaySettingControl.ResolveAdvancedColorInfoAvailability(availableMember, live));
            Assert.IsFalse(DisplaySettingControl.ResolveAdvancedColorInfoAvailability(unavailableMember, live));
        }
    }
}
