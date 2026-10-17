using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class ProfileEditorAdvancedColorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SlideshowEditor_ExposesColorFitmentAndSlideshowControls()
        {
            var (hasFitment, hasColor, hasSlideshow) = ProfileEditWindow.GetWallpaperOptionApplicability(WallpaperMode.Slideshow);

            Assert.IsTrue(hasColor);
            Assert.IsTrue(hasFitment);
            Assert.IsTrue(hasSlideshow);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrEditorCycle_PreHdrWcgOff_RestoresOffAfterHdr()
        {
            var enter = DisplaySettingControl.ResolveHdrWcgEditorState(true, false, false, false);
            var exit = DisplaySettingControl.ResolveHdrWcgEditorState(false, true, enter.WcgChecked, enter.RememberedWcg);

            Assert.IsTrue(enter.WcgChecked, "WCG is visually checked while HDR owns the effective destination.");
            Assert.IsFalse(enter.RememberedWcg);
            Assert.IsFalse(exit.WcgChecked);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrEditorCycle_PreHdrWcgOn_RestoresOnAfterHdr()
        {
            var enter = DisplaySettingControl.ResolveHdrWcgEditorState(true, false, true, true);
            var exit = DisplaySettingControl.ResolveHdrWcgEditorState(false, true, enter.WcgChecked, enter.RememberedWcg);

            Assert.IsTrue(enter.WcgChecked);
            Assert.IsTrue(enter.RememberedWcg);
            Assert.IsTrue(exit.WcgChecked);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HdrSave_RemainsCanonicalHdrWithoutPersistentWcgBit()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
            };

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: true, requestedHdrEnabled: true, requestedWcgEnabled: true);

            Assert.IsTrue(isHdrEnabled);
            Assert.IsFalse(isWcgEnabled);
        }
    }
}
