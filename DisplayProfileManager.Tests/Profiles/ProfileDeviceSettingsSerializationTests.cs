using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

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
                    SwapButtons = true,
                    ApplyDoubleClickTime = true,
                    DoubleClickTime = 350,
                    ApplyMouseTrails = true,
                    MouseTrails = 5
                }
            };

            string json = ProfileSerializer.SerializeProfile(source);
            var loaded = ProfileSerializer.DeserializeProfile(json);

            Assert.AreEqual(2, loaded.MouseSettings.PointerSpeed);
            Assert.IsFalse(loaded.MouseSettings.EnhancePointerPrecision);
            Assert.AreEqual(1, loaded.MouseSettings.VerticalScrollLines);
            Assert.IsFalse(loaded.MouseSettings.ApplyHorizontalScroll);
            Assert.IsTrue(loaded.MouseSettings.SwapButtons);
            Assert.IsTrue(loaded.MouseSettings.ApplyDoubleClickTime);
            Assert.AreEqual(350, loaded.MouseSettings.DoubleClickTime);
            Assert.IsTrue(loaded.MouseSettings.ApplyMouseTrails);
            Assert.AreEqual(5, loaded.MouseSettings.MouseTrails);

            var mouseJson = JObject.Parse(json)["mouseSettings"];
            Assert.AreEqual(350, (int)mouseJson["doubleClickTime"]);
            Assert.AreEqual(5, (int)mouseJson["mouseTrails"]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MouseSetting_NewSettings_DefaultToUncheckedAndCount()
        {
            var defaults = new MouseSetting();
            Assert.IsFalse(defaults.AppliesAny());
            Assert.AreEqual(500, defaults.DoubleClickTime);
            Assert.AreEqual(0, defaults.MouseTrails);

            Assert.IsTrue(new MouseSetting { ApplyDoubleClickTime = true }.AppliesAny());
            Assert.IsTrue(new MouseSetting { ApplyMouseTrails = true }.AppliesAny());

            var clone = new MouseSetting { ApplyMouseTrails = true, MouseTrails = 4, DoubleClickTime = 700 }.Clone();
            Assert.AreEqual(4, clone.MouseTrails);
            Assert.AreEqual(700, clone.DoubleClickTime);

            var text = new MouseSetting { ApplyDoubleClickTime = true, DoubleClickTime = 700, ApplyMouseTrails = true, MouseTrails = 0 }.ToString();
            StringAssert.Contains(text, "Double-click: 700 ms");
            StringAssert.Contains(text, "Trails: Off");
        }

        [TestMethod]
        [TestCategory("Unit")]
        [DataRow(0, 0)]
        [DataRow(1, 0)]
        [DataRow(-3, 0)]
        [DataRow(2, 2)]
        [DataRow(7, 7)]
        [DataRow(16, 16)]
        [DataRow(40, 16)]
        public void NormalizeMouseTrails_MapsOffAndClamps(int input, int expected) =>
            Assert.AreEqual(expected, MouseSetting.NormalizeMouseTrails(input));

        [TestMethod]
        [TestCategory("Unit")]
        [DataRow(0, 200)]
        [DataRow(200, 200)]
        [DataRow(500, 500)]
        [DataRow(900, 900)]
        [DataRow(5000, 900)]
        public void ClampDoubleClickTime_KeepsControlPanelRange(int input, int expected) =>
            Assert.AreEqual(expected, MouseSetting.ClampDoubleClickTime(input));

        [TestMethod]
        [TestCategory("Unit")]
        public void DeserializeProfile_MalformedDeviceSettings_RecoversDefaults()
        {
            var profile = ProfileSerializer.DeserializeProfile("{\"name\":\"Profile\",\"mouseSettings\":[1]}");

            Assert.IsNotNull(profile);
            Assert.IsFalse(profile.MouseSettings?.Enabled ?? false);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CreateWorkingCopy_DeviceSettings_AreIndependentCopies()
        {
            var source = new Profile("Profile")
            {
                MouseSettings = new MouseSetting { Enabled = true, ApplyPointerSpeed = true, PointerSpeed = 1 }
            };

            var copy = ProfileDraftFactory.CreateWorkingCopy(source);
            copy.MouseSettings.PointerSpeed = 2;

            Assert.AreEqual(1, source.MouseSettings.PointerSpeed);
            Assert.IsTrue(copy.MouseSettings.ApplyPointerSpeed);
        }
    }
}
