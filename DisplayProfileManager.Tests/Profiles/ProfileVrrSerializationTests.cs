using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileVrrSerializationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_WithoutVrrSettings_LoadsDisabledDefaults()
        {
            var profile = ProfileSerializer.DeserializeProfile("{\"name\":\"Profile\"}");

            Assert.IsNotNull(profile.VrrSettings);
            Assert.IsFalse(profile.VrrSettings.Enabled);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SerializeProfile_VrrSettings_RoundTrip()
        {
            var source = new Profile("Profile") { VrrSettings = new VrrSetting { Enabled = true, Mode = VrrMode.OnFullscreenOnly } };

            string json = ProfileSerializer.SerializeProfile(source);
            var loaded = ProfileSerializer.DeserializeProfile(json);

            Assert.AreEqual("OnFullscreenOnly", JObject.Parse(json)["vrrSettings"]["mode"].Value<string>());
            Assert.IsTrue(loaded.VrrSettings.Enabled);
            Assert.AreEqual(VrrMode.OnFullscreenOnly, loaded.VrrSettings.Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedVrrSettings_RecoversDefaults()
        {
            var profile = ProfileSerializer.DeserializeProfile("{\"name\":\"Profile\",\"vrrSettings\":\"bad\"}");

            Assert.IsNotNull(profile);
            Assert.IsFalse(profile.VrrSettings?.Enabled ?? false);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateWorkingCopy_VrrSettings_IsIndependentCopy()
        {
            var source = new Profile("Profile") { VrrSettings = new VrrSetting { Enabled = true, Mode = VrrMode.Off } };

            var copy = ProfileDraftFactory.CreateWorkingCopy(source);
            copy.VrrSettings.Mode = VrrMode.On;

            Assert.AreEqual(VrrMode.Off, source.VrrSettings.Mode);
            Assert.IsTrue(copy.VrrSettings.Enabled);
        }
    }
}
