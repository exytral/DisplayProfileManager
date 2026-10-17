using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Tests.Helpers;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileHardwareSelfHealingTests
    {
        private List<Profile> _profiles;

        [TestInitialize]
        public void Setup()
        {
            _profiles = [];
        }

        private static bool HasIncompleteHardwareInfo(DisplaySetting setting) =>
            ProfileHardwareReconciler.HasIncompleteHardwareInfo(setting);

        private static bool BackfillHardwareInfoFromLive(
            DisplaySetting setting,
            DisplayConfigInfo live) =>
            ProfileHardwareReconciler.BackfillHardwareInfoFromLive(setting, live);

        private List<Profile> BackfillHardwareInfoAcrossProfiles(
            Profile appliedProfile,
            List<DisplayConfigInfo> liveConfigs) =>
            ProfileHardwareReconciler.BackfillHardwareInfoAcrossProfiles(appliedProfile, _profiles, liveConfigs);

        private void SeedProfiles(params Profile[] profiles)
        {
            _profiles.AddRange(profiles);
        }

        private static Profile ProfileWith(params DisplaySetting[] settings)
        {
            var profile = new Profile("Test Profile");
            profile.DisplaySettings.AddRange(settings);
            return profile;
        }

        private static DisplayConfigInfo LiveConfig(
            uint targetId,
            int nativeWidth,
            int nativeHeight,
            string manufacturer,
            string productCode)
        {
            return new DisplayConfigInfo
            {
                TargetId = targetId,
                NativeWidth = nativeWidth,
                NativeHeight = nativeHeight,
                ManufacturerName = manufacturer,
                ProductCodeID = productCode
            };
        }

        private static DisplayConfigInfo LiveCapability(
            uint targetId,
            string manufacturer,
            string productCode,
            bool hdrSupported,
            bool wcgSupported,
            bool available = true)
        {
            return new DisplayConfigInfo
            {
                TargetId = targetId,
                RawTargetId = targetId,
                ManufacturerName = manufacturer,
                ProductCodeID = productCode,
                IsAdvancedColorInfoAvailable = available,
                IsHdrSupported = hdrSupported,
                IsWcgSupported = wcgSupported
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
        public void BackfillHardwareInfoFromLive_UsesPreferredNativeMetadataInsteadOfCurrentSignalSize()
        {
            var setting = new DisplaySettingBuilder()
                .WithNativeResolution(0, 0)
                .Build();
            var live = new DisplayConfigInfo
            {
                TargetId = 1,
                Width = 4096,
                Height = 2160,
                NativeWidth = 3840,
                NativeHeight = 2160
            };

            var changed = BackfillHardwareInfoFromLive(setting, live);

            Assert.IsTrue(changed);
            Assert.AreEqual(3840, setting.NativeWidth);
            Assert.AreEqual(2160, setting.NativeHeight);
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
            var liveConfigs = new List<DisplayConfigInfo>
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
            var liveConfigs = new List<DisplayConfigInfo>
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

            var liveConfigs = new List<DisplayConfigInfo>
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

            var liveConfigs = new List<DisplayConfigInfo>
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

            var liveConfigs = new List<DisplayConfigInfo>
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

            var liveConfigs = new List<DisplayConfigInfo>
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
            var liveConfigs = new List<DisplayConfigInfo>
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
            var liveConfigs = new List<DisplayConfigInfo>
            {
                LiveConfig(1, 2560, 1440, "MAN", "A1B2")
            };

            var changed = BackfillHardwareInfoAcrossProfiles(profile, liveConfigs);

            Assert.AreEqual(0, changed.Count);
            Assert.AreEqual(1920, setting.NativeWidth);
            Assert.AreEqual("DEV", setting.ManufacturerName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SavedProfileCapabilityMerge_RoundTrippedSameTargetDifferentEdid_RemainsIndependentBothDirections()
        {
            var canonical = ProfileWith(
                new DisplaySetting
                {
                    TargetId = 1,
                    ManufacturerName = "AAA",
                    ProductCodeID = "1001",
                    LastKnownHdrSupported = true,
                    LastKnownWcgSupported = false
                },
                new DisplaySetting
                {
                    TargetId = 1,
                    ManufacturerName = "BBB",
                    ProductCodeID = "2002",
                    LastKnownHdrSupported = false,
                    LastKnownWcgSupported = true
                });
            canonical.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;
            canonical = ProfileSerializer.DeserializeProfile(ProfileSerializer.SerializeProfile(canonical));

            var draft = ProfileSerializer.DeserializeProfile(ProfileSerializer.SerializeProfile(canonical));
            draft.DisplaySettings[0].LastKnownHdrSupported = null;
            draft.DisplaySettings[0].LastKnownWcgSupported = null;
            draft.DisplaySettings[1].LastKnownHdrSupported = null;
            draft.DisplaySettings[1].LastKnownWcgSupported = null;

            ProfileHardwareReconciler.MergeCanonicalHardwareSelfHealMetadata(draft, canonical);

            Assert.AreEqual(true, draft.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, draft.DisplaySettings[0].LastKnownWcgSupported);
            Assert.AreEqual(false, draft.DisplaySettings[1].LastKnownHdrSupported);
            Assert.AreEqual(true, draft.DisplaySettings[1].LastKnownWcgSupported);

            var newestCanonical = ProfileSerializer.DeserializeProfile(ProfileSerializer.SerializeProfile(canonical));
            newestCanonical.DisplaySettings[0].LastKnownHdrSupported = null;
            newestCanonical.DisplaySettings[0].LastKnownWcgSupported = null;
            newestCanonical.DisplaySettings[1].LastKnownHdrSupported = null;
            newestCanonical.DisplaySettings[1].LastKnownWcgSupported = null;

            ProfileHardwareReconciler.MergeHardwareSelfHealMetadata(newestCanonical, canonical);

            Assert.AreEqual(true, newestCanonical.DisplaySettings[0].LastKnownHdrSupported);
            Assert.AreEqual(false, newestCanonical.DisplaySettings[0].LastKnownWcgSupported);
            Assert.AreEqual(false, newestCanonical.DisplaySettings[1].LastKnownHdrSupported);
            Assert.AreEqual(true, newestCanonical.DisplaySettings[1].LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SavedProfileCapabilityMerge_AmbiguousTargetWithoutUniqueEdid_RemainsUnresolved()
        {
            var canonical = ProfileWith(
                new DisplaySetting { TargetId = 1, LastKnownHdrSupported = true },
                new DisplaySetting { TargetId = 1, LastKnownHdrSupported = false });
            var draft = ProfileWith(new DisplaySetting { TargetId = 1, LastKnownHdrSupported = null });

            ProfileHardwareReconciler.MergeCanonicalHardwareSelfHealMetadata(draft, canonical);

            Assert.IsNull(draft.DisplaySettings[0].LastKnownHdrSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_MatchingPhysicalDisplay_HealsMultipleProfiles()
        {
            var firstSetting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            var secondSetting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            var first = ProfileWith(firstSetting);
            var second = ProfileWith(secondSetting);
            var live = new List<DisplayConfigInfo> { LiveCapability(11, "MAN", "1001", hdrSupported: true, wcgSupported: false) };

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [first, second],
                live);

            Assert.AreEqual(true, firstSetting.LastKnownHdrSupported);
            Assert.AreEqual(false, firstSetting.LastKnownWcgSupported);
            Assert.AreEqual(true, secondSetting.LastKnownHdrSupported);
            Assert.AreEqual(false, secondSetting.LastKnownWcgSupported);
            Assert.AreEqual(2, changed.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_DurableTrue_LaterLiveFalsePreservesSubstantiatedSupport()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            setting.LastKnownHdrSupported = true;
            setting.LastKnownWcgSupported = true;
            setting.IsHdrEnabled = true;
            setting.IsWcgEnabled = true;
            var profile = ProfileWith(setting);

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [profile],
                [LiveCapability(11, "MAN", "1001", hdrSupported: false, wcgSupported: false)]);

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.AreEqual(true, setting.LastKnownWcgSupported);
            Assert.IsTrue(setting.IsHdrEnabled);
            Assert.IsTrue(setting.IsWcgEnabled);
            Assert.AreEqual(0, changed.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_FirstKnownFalse_PropagatesWithoutChangingIntent()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            setting.IsHdrEnabled = true;
            setting.IsWcgEnabled = true;
            var profile = ProfileWith(setting);

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [profile],
                [LiveCapability(11, "MAN", "1001", hdrSupported: false, wcgSupported: false)]);

            Assert.AreEqual(false, setting.LastKnownHdrSupported);
            Assert.AreEqual(false, setting.LastKnownWcgSupported);
            Assert.IsTrue(setting.IsHdrEnabled);
            Assert.IsTrue(setting.IsWcgEnabled);
            CollectionAssert.Contains(changed, profile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_FeaturesPreserveIndependentDurableProvenance()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            setting.LastKnownHdrSupported = true;
            setting.LastKnownWcgSupported = false;
            var profile = ProfileWith(setting);

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [profile],
                [LiveCapability(11, "MAN", "1001", hdrSupported: false, wcgSupported: true)]);

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.AreEqual(true, setting.LastKnownWcgSupported);
            CollectionAssert.Contains(changed, profile);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_UnavailableObservation_PreservesEvidenceAndIntent()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            setting.LastKnownHdrSupported = true;
            setting.LastKnownWcgSupported = null;
            setting.IsHdrEnabled = true;
            var profile = ProfileWith(setting);

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [profile],
                [LiveCapability(11, "MAN", "1001", hdrSupported: false, wcgSupported: false, available: false)]);

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.IsNull(setting.LastKnownWcgSupported);
            Assert.IsTrue(setting.IsHdrEnabled);
            Assert.AreEqual(0, changed.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_NonmatchingDisplay_IsUntouched()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(33).WithEdid("DEV", "9999").Build();
            var profile = ProfileWith(setting);

            var changed = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(
                [profile],
                [LiveCapability(11, "MAN", "1001", hdrSupported: true, wcgSupported: true)]);

            Assert.IsNull(setting.LastKnownHdrSupported);
            Assert.IsNull(setting.LastKnownWcgSupported);
            Assert.AreEqual(0, changed.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_IdentityMovedPorts_FollowsPhysicalDisplay()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            var profile = ProfileWith(setting);
            var live = new List<DisplayConfigInfo>
            {
                LiveCapability(11, "DEV", "9999", hdrSupported: false, wcgSupported: false),
                LiveCapability(22, "MAN", "1001", hdrSupported: true, wcgSupported: true)
            };

            ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles([profile], live);

            Assert.AreEqual(true, setting.LastKnownHdrSupported);
            Assert.AreEqual(true, setting.LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_CloneMembers_HealIndependently()
        {
            var first = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            var second = new DisplaySettingBuilder().WithTargetId(22).WithEdid("DEV", "1002").Build();
            first.CloneGroupId = "clone";
            second.CloneGroupId = "clone";
            var profile = ProfileWith(first, second);
            var live = new List<DisplayConfigInfo>
            {
                LiveCapability(11, "MAN", "1001", hdrSupported: true, wcgSupported: false),
                LiveCapability(22, "DEV", "1002", hdrSupported: false, wcgSupported: true)
            };

            ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles([profile], live);

            Assert.AreEqual(true, first.LastKnownHdrSupported);
            Assert.AreEqual(false, first.LastKnownWcgSupported);
            Assert.AreEqual(false, second.LastKnownHdrSupported);
            Assert.AreEqual(true, second.LastKnownWcgSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void HealAdvancedColorCapabilitiesAcrossProfiles_RepeatedObservation_IsIdempotent()
        {
            var setting = new DisplaySettingBuilder().WithTargetId(11).WithEdid("MAN", "1001").Build();
            var profile = ProfileWith(setting);
            var live = new List<DisplayConfigInfo> { LiveCapability(11, "MAN", "1001", hdrSupported: true, wcgSupported: false) };

            var first = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles([profile], live);
            var second = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles([profile], live);

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(0, second.Count);
        }
    }
}
