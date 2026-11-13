using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Gsync.Models;
using DisplayProfileManager.Pointer.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileDeviceSettingsSerializationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_WithoutDeviceSettings_LoadsDisabledDefaults()
        {
            var profile = ProfileSerializer.DeserializeProfile("{\"name\":\"Profile\"}");

            Assert.IsNotNull(profile.GsyncSettings);
            Assert.IsFalse(profile.GsyncSettings.Enabled);
            Assert.IsNotNull(profile.MouseSettings);
            Assert.IsFalse(profile.MouseSettings.Enabled);
            Assert.IsFalse(profile.MouseSettings.AppliesAny());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeProfile_DeviceSettings_RoundTrip()
        {
            var source = new Profile("Profile")
            {
                GsyncSettings = new GsyncSetting { Enabled = true, Mode = GsyncMode.FullscreenAndWindowed },
                MouseSettings = new MouseSetting
                {
                    Enabled = true,
                    ApplyPointerSpeed = true,
                    PointerSpeed = 2,
                    ApplyPointerPrecision = true,
                    EnhancePointerPrecision = false,
                    ApplyVerticalScroll = true,
                    VerticalScrollLines = 1,
                    ApplySwapButtons = true,
                    SwapButtons = true
                }
            };

            string json = ProfileSerializer.SerializeProfile(source);
            var loaded = ProfileSerializer.DeserializeProfile(json);

            Assert.AreEqual("FullscreenAndWindowed", JObject.Parse(json)["gsyncSettings"]["mode"].Value<string>());
            Assert.IsTrue(loaded.GsyncSettings.Enabled);
            Assert.AreEqual(GsyncMode.FullscreenAndWindowed, loaded.GsyncSettings.Mode);
            Assert.AreEqual(2, loaded.MouseSettings.PointerSpeed);
            Assert.IsFalse(loaded.MouseSettings.EnhancePointerPrecision);
            Assert.AreEqual(1, loaded.MouseSettings.VerticalScrollLines);
            Assert.IsFalse(loaded.MouseSettings.ApplyHorizontalScroll);
            Assert.IsTrue(loaded.MouseSettings.SwapButtons);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedDeviceSettings_RecoversDefaults()
        {
            var profile = ProfileSerializer.DeserializeProfile("{\"name\":\"Profile\",\"gsyncSettings\":\"bad\",\"mouseSettings\":[1]}");

            Assert.IsNotNull(profile);
            Assert.IsFalse(profile.GsyncSettings?.Enabled ?? false);
            Assert.IsFalse(profile.MouseSettings?.Enabled ?? false);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateWorkingCopy_DeviceSettings_AreIndependentCopies()
        {
            var source = new Profile("Profile")
            {
                GsyncSettings = new GsyncSetting { Enabled = true, Mode = GsyncMode.Off },
                MouseSettings = new MouseSetting { Enabled = true, ApplyPointerSpeed = true, PointerSpeed = 1 }
            };

            var copy = ProfileDraftFactory.CreateWorkingCopy(source);
            copy.GsyncSettings.Mode = GsyncMode.Fullscreen;
            copy.MouseSettings.PointerSpeed = 2;

            Assert.AreEqual(GsyncMode.Off, source.GsyncSettings.Mode);
            Assert.AreEqual(1, source.MouseSettings.PointerSpeed);
            Assert.IsTrue(copy.GsyncSettings.Enabled);
            Assert.IsTrue(copy.MouseSettings.ApplyPointerSpeed);
        }
    }
}
