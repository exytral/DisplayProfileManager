using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;

namespace DisplayProfileManager.Tests.Profiles
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

            Assert.ThrowsExactly<ProfileCaptureService.AdvancedColorCaptureUnavailableException>(() =>
                ProfileCaptureService.CaptureDisplayConfigsForProfile(
                    () =>
                    {
                        calls++;
                        return
                        [
                            new DisplayConfigInfo
                            {
                                DeviceName = "DISPLAY1",
                                FriendlyName = "Unavailable display",
                                IsAdvancedColorInfoAvailable = false,
                            }
                        ];
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
            var configs = ProfileCaptureService.CaptureDisplayConfigsForProfile(
                () =>
                {
                    calls++;
                    return
                    [
                        new DisplayConfigInfo
                        {
                            DeviceName = "DISPLAY1",
                            IsAdvancedColorInfoAvailable = calls > 1,
                            IsWcgSupported = calls > 1,
                            IsWcgEnabled = calls > 1,
                        }
                    ];
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
            var configs = ProfileCaptureService.CaptureDisplayConfigsForProfile(
                () =>
                {
                    calls++;
                    return
                    [
                        new DisplayConfigInfo { IsAdvancedColorInfoAvailable = false }
                    ];
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
            var live = new DisplayConfigInfo
            {
                IsAdvancedColorInfoAvailable = false,
            };

            Assert.ThrowsExactly<ProfileCaptureService.AdvancedColorCaptureUnavailableException>(() => ProfileCaptureService.ProjectAdvancedColorForCapture(setting, live, requireKnownAdvancedColor: true));

            Assert.IsTrue(setting.IsHdrEnabled);
        }
    }
}
