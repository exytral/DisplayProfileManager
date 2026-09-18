using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AdvancedColorCaptureTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ModernCapture_UnavailableRetriesThenRefuses()
        {
            int calls = 0;
            var retryAttempts = new List<int>();

            Assert.ThrowsExactly<ProfileManager.AdvancedColorCaptureUnavailableException>(() =>
                ProfileManager.CaptureDisplayConfigsForProfile(
                    () =>
                    {
                        calls++;
                        return new List<DisplayConfigHelper.DisplayConfigInfo>
                        {
                            new DisplayConfigHelper.DisplayConfigInfo
                            {
                                DeviceName = "DISPLAY1",
                                FriendlyName = "Unavailable display",
                                IsAdvancedColorInfoAvailable = false,
                            }
                        };
                    },
                    requireKnownAdvancedColor: true,
                    maxAttempts: 3,
                    retryDelay: attempt => retryAttempts.Add(attempt)));

            Assert.AreEqual(3, calls);
            CollectionAssert.AreEqual(new[] { 1, 2 }, retryAttempts);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModernCapture_RetryThenAvailableReturnsKnownObservation()
        {
            int calls = 0;
            var configs = ProfileManager.CaptureDisplayConfigsForProfile(
                () =>
                {
                    calls++;
                    return new List<DisplayConfigHelper.DisplayConfigInfo>
                    {
                        new DisplayConfigHelper.DisplayConfigInfo
                        {
                            DeviceName = "DISPLAY1",
                            IsAdvancedColorInfoAvailable = calls > 1,
                            IsWcgSupported = calls > 1,
                            IsWcgEnabled = calls > 1,
                        }
                    };
                },
                requireKnownAdvancedColor: true,
                maxAttempts: 3);

            Assert.AreEqual(2, calls);
            Assert.IsTrue(configs[0].IsAdvancedColorInfoAvailable);
            Assert.IsTrue(configs[0].IsWcgSupported);
            Assert.IsTrue(configs[0].IsWcgEnabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyCapture_DoesNotAcquireModernRequireKnownPolicy()
        {
            int calls = 0;
            var configs = ProfileManager.CaptureDisplayConfigsForProfile(
                () =>
                {
                    calls++;
                    return new List<DisplayConfigHelper.DisplayConfigInfo>
                    {
                        new DisplayConfigHelper.DisplayConfigInfo { IsAdvancedColorInfoAvailable = false }
                    };
                },
                requireKnownAdvancedColor: false,
                maxAttempts: 3);

            Assert.AreEqual(1, calls);
            Assert.IsFalse(configs[0].IsAdvancedColorInfoAvailable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CurrentProjection_ModernUnavailableCannotBecomeKnownSdr()
        {
            var setting = new DisplaySetting
            {
                IsHdrSupported = true,
                IsHdrEnabled = true,
            };
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            Assert.ThrowsExactly<ProfileManager.AdvancedColorCaptureUnavailableException>(() => ProfileManager.ProjectAdvancedColorForCapture(setting, live, requireKnownAdvancedColor: true));

            Assert.IsTrue(setting.IsHdrEnabled);
        }

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
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);
            var saved = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: false, requestedHdrEnabled: false, requestedWcgEnabled: false);

            Assert.IsTrue(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
            Assert.IsTrue(saved.IsHdrEnabled);
            Assert.IsFalse(saved.IsWcgEnabled);
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
            var live = new DisplayConfigHelper.DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(setting, live);
            var saved = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: false, requestedHdrEnabled: false, requestedWcgEnabled: false);

            Assert.IsTrue(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);
            Assert.IsFalse(saved.IsHdrEnabled);
            Assert.IsTrue(saved.IsWcgEnabled);
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
                new DisplayConfigHelper.DisplayConfigInfo { IsAdvancedColorInfoAvailable = false });

            ProfileEditWindow.ReconcileAdvancedColorCapabilities(
                setting,
                new DisplayConfigHelper.DisplayConfigInfo
                {
                    IsAdvancedColorInfoAvailable = true,
                    IsHdrSupported = false,
                    IsWcgSupported = true,
                });

            Assert.IsFalse(setting.IsHdrSupported);
            Assert.IsTrue(setting.IsWcgSupported);

            var saved = DisplaySettingControl.ResolveAdvancedColorIntentForSave(setting, advancedColorAvailable: true, requestedHdrEnabled: false, requestedWcgEnabled: true);
            Assert.IsFalse(saved.IsHdrEnabled);
            Assert.IsTrue(saved.IsWcgEnabled);
        }

    }
}
