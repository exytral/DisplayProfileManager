using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ProfileHardwareSelfHealingTests
    {
        private ProfileManager _pm;
        private FieldInfo _profilesField;
        private MethodInfo _hasIncompleteHardwareInfoMethod;
        private MethodInfo _backfillHardwareInfoMethod;
        private MethodInfo _backfillHardwareInfoAcrossProfilesMethod;

        [TestInitialize]
        public void Setup()
        {
            _pm = ProfileManager.Instance;

            _profilesField = typeof(ProfileManager).GetField("_profiles", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_profilesField, "_profiles field not found — was it renamed?");

            _hasIncompleteHardwareInfoMethod = typeof(ProfileManager).GetMethod("HasIncompleteHardwareInfo", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(_hasIncompleteHardwareInfoMethod, "HasIncompleteHardwareInfo not found — was it renamed?");

            _backfillHardwareInfoMethod = typeof(ProfileManager).GetMethod("BackfillHardwareInfoFromLive", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(_backfillHardwareInfoMethod, "BackfillHardwareInfoFromLive not found — was it renamed?");

            _backfillHardwareInfoAcrossProfilesMethod = typeof(ProfileManager).GetMethod("BackfillHardwareInfoAcrossProfiles", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_backfillHardwareInfoAcrossProfilesMethod, "BackfillHardwareInfoAcrossProfiles not found — was it renamed?");

            _profilesField.SetValue(_pm, new List<Profile>());
        }

        private bool HasIncompleteHardwareInfo(DisplaySetting setting) =>
            (bool)_hasIncompleteHardwareInfoMethod.Invoke(null, [setting]);

        private bool BackfillHardwareInfoFromLive(
            DisplaySetting setting,
            DisplayConfigHelper.DisplayConfigInfo live) =>
            (bool)_backfillHardwareInfoMethod.Invoke(null, [setting, live]);

        private List<Profile> BackfillHardwareInfoAcrossProfiles(
            Profile appliedProfile,
            List<DisplayConfigHelper.DisplayConfigInfo> liveConfigs) =>
            (List<Profile>)_backfillHardwareInfoAcrossProfilesMethod.Invoke(_pm, [appliedProfile, liveConfigs]);

        private void SeedProfiles(params Profile[] profiles)
        {
            var list = (List<Profile>)_profilesField.GetValue(_pm);
            list.AddRange(profiles);
        }

        private static Profile ProfileWith(params DisplaySetting[] settings)
        {
            var profile = new Profile("Test Profile");
            profile.DisplaySettings.AddRange(settings);
            return profile;
        }

        private static DisplayConfigHelper.DisplayConfigInfo LiveConfig(
            uint targetId,
            int nativeWidth,
            int nativeHeight,
            string manufacturer,
            string productCode)
        {
            return new DisplayConfigHelper.DisplayConfigInfo
            {
                TargetId = targetId,
                NativeWidth = nativeWidth,
                NativeHeight = nativeHeight,
                ManufacturerName = manufacturer,
                ProductCodeID = productCode
            };
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasIncompleteHardwareInfo_MissingNativeDimensions_ReturnsTrue()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(0, 0)
                .Build();

            var result = HasIncompleteHardwareInfo(setting);

            Assert.IsTrue(result);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasIncompleteHardwareInfo_MissingEdidIdentity_ReturnsTrue()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(1920, 1080)
                .WithEdid("", "")
                .Build();

            var result = HasIncompleteHardwareInfo(setting);

            Assert.IsTrue(result);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasIncompleteHardwareInfo_ColorProfileNull_IsPreserved()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(1920, 1080)
                .WithEdid("MAN", "A1B2")
                .WithColorProfile(null)
                .Build();

            var result = HasIncompleteHardwareInfo(setting);

            Assert.IsFalse(result);
            Assert.IsNull(setting.ColorProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HasIncompleteHardwareInfo_FullyPopulated_ReturnsFalse()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(1920, 1080)
                .WithEdid("MAN", "A1B2")
                .Build();

            var result = HasIncompleteHardwareInfo(setting);

            Assert.IsFalse(result);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoFromLive_MissingNative_FillsFromLive()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(0, 0)
                .Build();
            var live = LiveConfig(1, 2560, 1440, "", "");

            var changed = BackfillHardwareInfoFromLive(setting, live);

            Assert.IsTrue(changed);
            Assert.AreEqual(2560, setting.NativeWidth);
            Assert.AreEqual(1440, setting.NativeHeight);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoFromLive_PopulatedNative_IsPreserved()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(1920, 1080)
                .Build();
            var live = LiveConfig(1, 2560, 1440, "", "");

            BackfillHardwareInfoFromLive(setting, live);

            Assert.AreEqual(1920, setting.NativeWidth);
            Assert.AreEqual(1080, setting.NativeHeight);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoFromLive_MissingEdid_FillsFromLive()
        {
            var setting = new DisplaySettingBuilder()
                .WithEdid("", "")
                .Build();
            var live = LiveConfig(1, 0, 0, "MAN", "A1B2");

            var changed = BackfillHardwareInfoFromLive(setting, live);

            Assert.IsTrue(changed);
            Assert.AreEqual("MAN", setting.ManufacturerName);
            Assert.AreEqual("A1B2", setting.ProductCodeID);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoFromLive_PopulatedEdid_IsPreserved()
        {
            var setting = new DisplaySettingBuilder()
                .WithEdid("DEV", "C3D4")
                .Build();
            var live = LiveConfig(1, 0, 0, "MAN", "A1B2");

            BackfillHardwareInfoFromLive(setting, live);

            Assert.AreEqual("DEV", setting.ManufacturerName);
            Assert.AreEqual("C3D4", setting.ProductCodeID);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoFromLive_ColorProfileNull_RemainsNull()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(0, 0)
                .WithEdid("", "")
                .WithColorProfile(null)
                .Build();
            var live = LiveConfig(1, 2560, 1440, "MAN", "A1B2");

            BackfillHardwareInfoFromLive(setting, live);

            Assert.IsNull(setting.ColorProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_LiveTargetMatch_FillsAppliedProfileNativeDimensions()
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var profile = ProfileWith(setting);
            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "", "")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(profile, liveConfigs);

            Assert.AreEqual(2560, setting.NativeWidth);
            CollectionAssert.Contains(changed, profile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_LiveTargetMatch_FillsAppliedProfileEdidIdentity()
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithEdid("", "")
                .Build();
            var profile = ProfileWith(setting);
            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 0, 0, "MAN", "A1B2")
            };

            BackfillHardwareInfoAcrossProfiles(profile, liveConfigs);

            Assert.AreEqual("MAN", setting.ManufacturerName);
            Assert.AreEqual("A1B2", setting.ProductCodeID);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_CompleteAppliedProfile_DoesNotScanOtherProfiles()
        {
            var appliedSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(1920, 1080)
                .WithEdid("MAN", "A1B2")
                .Build();
            var applied = ProfileWith(appliedSetting);

            var otherSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var other = ProfileWith(otherSetting);
            SeedProfiles(applied, other);

            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "MAN", "A1B2")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(applied, liveConfigs);

            Assert.AreEqual(0, changed.Count);
            Assert.AreEqual(0, otherSetting.NativeWidth);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_AppliedProfileRepair_AlsoRepairsOtherLoadedProfileSameTargetId()
        {
            var appliedSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var applied = ProfileWith(appliedSetting);

            var otherSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var other = ProfileWith(otherSetting);
            SeedProfiles(applied, other);

            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "", "")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(applied, liveConfigs);

            Assert.AreEqual(2560, otherSetting.NativeWidth);
            CollectionAssert.Contains(changed, other);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_MultipleOtherProfilesSameTargetId_AllRepair()
        {
            var appliedSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var applied = ProfileWith(appliedSetting);

            var otherSetting1 = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var other1 = ProfileWith(otherSetting1);

            var otherSetting2 = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var other2 = ProfileWith(otherSetting2);
            SeedProfiles(applied, other1, other2);

            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "", "")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(applied, liveConfigs);

            Assert.AreEqual(2560, otherSetting1.NativeWidth);
            Assert.AreEqual(2560, otherSetting2.NativeWidth);
            Assert.AreEqual(3, changed.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_UnmatchedTargetId_RemainsUnchanged()
        {
            var appliedSetting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .Build();
            var applied = ProfileWith(appliedSetting);

            var otherSetting = new DisplaySettingBuilder()
                .WithTargetId(9)
                .WithNativeResolution(0, 0)
                .Build();
            var other = ProfileWith(otherSetting);
            SeedProfiles(applied, other);

            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "", "")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(applied, liveConfigs);

            Assert.AreEqual(0, otherSetting.NativeWidth);
            CollectionAssert.DoesNotContain(changed, other);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_ColorProfileNull_IsPreserved()
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(0, 0)
                .WithColorProfile(null)
                .Build();
            var profile = ProfileWith(setting);
            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "MAN", "A1B2")
            };

            BackfillHardwareInfoAcrossProfiles(profile, liveConfigs);

            Assert.IsNull(setting.ColorProfile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackfillHardwareInfoAcrossProfiles_CompleteNativeAndEdid_IsNotOverwritten()
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(1)
                .WithNativeResolution(1920, 1080)
                .WithEdid("DEV", "C3D4")
                .Build();
            var profile = ProfileWith(setting);
            var liveConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "MAN", "A1B2")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(profile, liveConfigs);

            Assert.AreEqual(0, changed.Count);
            Assert.AreEqual(1920, setting.NativeWidth);
            Assert.AreEqual("DEV", setting.ManufacturerName);
        }
    }
}