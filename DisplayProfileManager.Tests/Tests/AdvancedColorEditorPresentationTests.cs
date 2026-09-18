using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AdvancedColorEditorPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void HdrOff_RestoresRememberedWcgIntent()
        {
            var state = DisplaySettingControl.ResolveHdrWcgEditorState(
                hdrOn: false,
                wasHdrOn: true,
                currentWcgChecked: true,
                rememberedWcg: false);

            Assert.IsFalse(state.WcgChecked);
            Assert.IsFalse(state.RememberedWcg);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrOn_ForcesWcgCheckedWithoutReplacingRememberedIntent()
        {
            var state = DisplaySettingControl.ResolveHdrWcgEditorState(
                hdrOn: true,
                wasHdrOn: false,
                currentWcgChecked: false,
                rememberedWcg: true);

            Assert.IsTrue(state.WcgChecked);
            Assert.IsFalse(state.RememberedWcg);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableEnabledHdrForced_UsesNormalControlOpacity()
        {
            var presentation = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: true,
                displayEnabled: true);

            Assert.IsFalse(presentation.IsEnabled);
            Assert.AreEqual(1.0, presentation.Opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableDisabledHdrForced_PreservesDisplayInactiveOpacity()
        {
            var presentation = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: true,
                displayEnabled: false);

            Assert.IsFalse(presentation.IsEnabled);
            Assert.AreEqual(UiOpacity.Inactive, presentation.Opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_Unavailable_RemainsBlocked()
        {
            var presentation = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: false,
                wcgSupported: true,
                hdrForced: false,
                displayEnabled: true);

            Assert.IsFalse(presentation.IsEnabled);
            Assert.AreEqual(UiOpacity.Blocked, presentation.Opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WcgPresentation_AvailableEnabledHdrOff_IsEnabledAtNormalOpacity()
        {
            var presentation = DisplaySettingControl.ResolveWcgControlPresentation(
                advancedColorAvailable: true,
                wcgSupported: true,
                hdrForced: false,
                displayEnabled: true);

            Assert.IsTrue(presentation.IsEnabled);
            Assert.AreEqual(1.0, presentation.Opacity);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AdvancedColorAvailability_CloneMembersResolveIndependently()
        {
            var availableMember = new DisplaySettingBuilder().WithTargetId(1).Build();
            var unavailableMember = new DisplaySettingBuilder().WithTargetId(2).Build();
            var live = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                new DisplayConfigInfoBuilder().WithTargetId(1).WithAdvancedColorAvailability(true).Build(),
                new DisplayConfigInfoBuilder().WithTargetId(2).WithAdvancedColorAvailability(false).Build()
            };

            Assert.IsTrue(DisplaySettingControl.ResolveAdvancedColorInfoAvailability(availableMember, live));
            Assert.IsFalse(DisplaySettingControl.ResolveAdvancedColorInfoAvailability(unavailableMember, live));
        }
    }
}
