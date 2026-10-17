using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class AdvancedColorEditorCaptureTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ExistingHdrProfile_UnavailableReconcileAndSavePreservesHdrIntent()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = true,
                IsWcgEnabled = false,
            };
            var live = new DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);
            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: false, requestedHdrEnabled: false, requestedWcgEnabled: false);

            Assert.IsTrue(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
            Assert.IsTrue(isHdrEnabled);
            Assert.IsFalse(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExistingWcgProfile_UnavailableReconcileAndSavePreservesWcgIntent()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = true,
                IsHdrEnabled = false,
                IsWcgEnabled = true,
            };
            var live = new DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);
            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: false, requestedHdrEnabled: false, requestedWcgEnabled: false);

            Assert.IsTrue(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
            Assert.IsFalse(isHdrEnabled);
            Assert.IsTrue(isWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LaterAvailableLiveRow_ResumesIndependentCapabilityReconciliation()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsWcgSupported = false,
                IsHdrEnabled = true,
            };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(
                setting,
                new DisplayConfigInfo { IsAdvancedColorInfoAvailable = false });

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(
                setting,
                new DisplayConfigInfo
                {
                    IsAdvancedColorInfoAvailable = true,
                    IsHdrSupported = false,
                    IsWcgSupported = true,
                });

            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);

            var (isHdrEnabled, isWcgEnabled) = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: true, requestedHdrEnabled: false, requestedWcgEnabled: true);
            Assert.IsFalse(isHdrEnabled);
            Assert.IsTrue(isWcgEnabled);
        }
    }
}
