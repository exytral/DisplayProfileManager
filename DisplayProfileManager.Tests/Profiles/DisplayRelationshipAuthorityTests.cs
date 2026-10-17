using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class DisplayRelationshipAuthorityTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [TestCategory("Unit")]
        public void MergeCanonicalHardwareSelfHealMetadata_TwoIncomingRowsCompeteForOneEdid_FailsWithoutMutation()
        {
            var canonical = ProfileWith(Setting(11, "MAN", "1001", nativeWidth: 3840, hdr: true));
            var first = Setting(11, "MAN", "1001");
            var second = Setting(22, "MAN", "1001");
            var draft = ProfileWith(first, second);

            bool merged = ProfileHardwareReconciler.MergeCanonicalHardwareSelfHealMetadata(draft, canonical);

            Assert.IsFalse(merged);
            Assert.AreEqual(0, first.NativeWidth);
            Assert.AreEqual(0, second.NativeWidth);
            Assert.IsNull(first.LastKnownHdrSupported);
            Assert.IsNull(second.LastKnownHdrSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MergeHardwareSelfHealMetadata_TwoCanonicalRowsCompeteForOneEdid_FailsWithoutMutation()
        {
            var first = Setting(11, "MAN", "1001");
            var second = Setting(22, "MAN", "1001");
            var canonical = ProfileWith(first, second);
            var metadata = ProfileWith(Setting(11, "MAN", "1001", nativeWidth: 3840, hdr: true));

            bool merged = ProfileHardwareReconciler.MergeHardwareSelfHealMetadata(canonical, metadata);

            Assert.IsFalse(merged);
            Assert.AreEqual(0, first.NativeWidth);
            Assert.AreEqual(0, second.NativeWidth);
            Assert.IsNull(first.LastKnownHdrSupported);
            Assert.IsNull(second.LastKnownHdrSupported);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task UpdateProfileAsync_CompetingRowsForCanonicalRelationship_LeavesMemoryAndBytesUnchanged()
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmRelationshipAuthority-" + Guid.NewGuid().ToString("N"));
            var service = CreateProfileService(appDataFolder);
            var canonical = ProfileWith(Setting(11, "MAN", "1001", nativeWidth: 3840, hdr: true));
            canonical.Name = "Canonical";
            canonical.SchemaVersion = ProfileSerializer.CurrentSchemaVersion;

            try
            {
                Assert.IsTrue(await service.AddProfileAsync(canonical));
                string path = Path.Combine(appDataFolder, "Profiles", canonical.Id + ".dpm");
                string before = await File.ReadAllTextAsync(path, TestContext.CancellationToken);
                var draft = ProfileDraftFactory.CreateWorkingCopy(canonical);
                draft.Name = "Stale edit";
                draft.DisplaySettings.Add(Setting(22, "MAN", "1001"));

                bool saved = await service.UpdateProfileAsync(draft);
                string after = await File.ReadAllTextAsync(path, TestContext.CancellationToken);

                Assert.IsFalse(saved);
                Assert.AreSame(canonical, service.GetProfile(canonical.Id));
                Assert.AreEqual("Canonical", service.GetProfile(canonical.Id).Name);
                Assert.AreEqual(1, service.GetProfile(canonical.Id).DisplaySettings.Count);
                Assert.AreEqual(before, after);
            }
            finally
            {
                if (Directory.Exists(appDataFolder))
                    Directory.Delete(appDataFolder, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_DuplicateResolvedTargetWithAbortDisabled_MutatesNoDisplayState()
        {
            var live = Display(1, 11, 4, "MAN", "1001");
            var first = Setting(11, "MAN", "1001");
            var second = Setting(11, "MAN", "1001");
            first.AdapterLuid = Luid(1);
            second.AdapterLuid = Luid(1);
            var probe = new ApplyProbe();
            var adapter = new DuplicateTargetApplyAdapter(live, probe);

            var result = await ApplyAsync(ProfileWith(first, second), adapter);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, probe.TopologyCalls);
            Assert.AreEqual(0, probe.ConfigCalls);
            Assert.AreEqual(0, probe.DpiCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyDpiSettings_DuplicateAuthorizedTarget_RejectsBeforeSetter()
        {
            var live = Display(1, 11, 4, "MAN", "1001");
            var first = Setting(11, "MAN", "1001");
            var second = Setting(11, "MAN", "1001");
            var target = CcdAddress.Target(live);
            var authorizations = new[]
            {
                new DisplayApplyAddressAuthorization(first, DisplayAddressResolver.CurrentAddressResolutionStatus.Resolved, target),
                new DisplayApplyAddressAuthorization(second, DisplayAddressResolver.CurrentAddressResolutionStatus.Resolved, target)
            };
            var probe = new ApplyProbe();
            var adapter = new DuplicateTargetApplyAdapter(live, probe);

            bool changed = DisplayApplyPolicy.ApplyDpiSettings(authorizations, [live], adapter);

            Assert.IsFalse(changed);
            Assert.AreEqual(0, probe.DpiCalls);
        }

        private static Profile ProfileWith(params DisplaySetting[] settings)
        {
            var profile = new Profile("Profile");
            profile.DisplaySettings.AddRange(settings);
            return profile;
        }

        private static DisplaySetting Setting(uint targetId, string manufacturer, string product, int nativeWidth = 0, bool? hdr = null)
        {
            var setting = new DisplaySettingBuilder()
                .WithTargetId(targetId)
                .WithEdid(manufacturer, product)
                .WithNativeResolution(nativeWidth, nativeWidth == 0 ? 0 : 2160)
                .Build();
            setting.LastKnownHdrSupported = hdr;
            return setting;
        }

        private static DisplayConfigInfo Display(uint adapter, uint targetId, uint sourceId, string manufacturer, string product)
        {
            var display = new DisplayConfigInfoBuilder()
                .WithDeviceName("DISPLAY1")
                .WithEdid(manufacturer, product)
                .WithTargetId(targetId)
                .WithRawTargetId(targetId)
                .Build();
            display.AdapterId = Luid(adapter);
            display.SourceId = sourceId;
            return display;
        }

        private static AdapterLuid Luid(uint lowPart) =>
            new()
            { LowPart = lowPart, HighPart = 0 };

        private static ProfileService CreateProfileService(string appDataFolder)
        {
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            return new ProfileService(
                new ProfileStore(appDataFolder),
                new ProfileMigrationService(settings),
                new ProfileDraftFactory(),
                new ProfileCaptureService(),
                settings,
                () => [],
                () => []);
        }

        private static async Task<ProfileApplyResult> ApplyAsync(Profile profile, IDisplayApplyAdapter adapter)
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDuplicateTarget-" + Guid.NewGuid().ToString("N"));
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            settings.Settings.AbortOnApplyFailure = false;
            settings.Settings.RollbackAfterApplyFailure = false;

            try
            {
                var profiles = CreateProfileService(appDataFolder);
                var scripts = new ScriptLibrary(appDataFolder);
                var apply = new ProfileApplyService(
                    new ProfileApplyQueue(),
                    adapter,
                    new WallpaperService(),
                    new AudioDeviceService(),
                    new ScriptRunner(scripts),
                    settings,
                    profiles.GetProfile,
                    profiles.GetAllProfiles,
                    profiles.SaveProfileAsync,
                    profiles.SetCurrentProfileId,
                    () => profiles.CurrentProfileId);
                profiles.AttachApplyService(apply);
                return await apply.ApplyProfileAsync(profile);
            }
            finally
            {
                if (Directory.Exists(appDataFolder))
                    Directory.Delete(appDataFolder, true);
            }
        }

        private sealed class DuplicateTargetApplyAdapter(DisplayConfigInfo live, ApplyProbe probe) : IDisplayApplyAdapter
        {
            private readonly DisplayConfigInfo _live = live;
            private readonly ApplyProbe _probe = probe;

            public List<DisplayConfigInfo> GetActiveDisplays() => [_live];
            public List<DisplayConfigInfo> GetDisplayAddresses() => [_live];

            public bool ApplyTopology(List<DisplayConfigInfo> displays)
            {
                _probe.TopologyCalls++;
                return true;
            }

            public Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays)
            {
                _probe.ConfigCalls++;
                return Task.FromResult(new DisplayConfigService.DisplayConfigApplyResult { Success = true });
            }

            public bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay)
            {
                _probe.DpiCalls++;
                return true;
            }

            public bool ApplyAdvancedColor(List<DisplayConfigInfo> displays) => true;
            public bool ApplyColorProfiles(List<DisplayConfigInfo> displays) => true;
        }

        private sealed class ApplyProbe
        {
            public int TopologyCalls { get; set; }
            public int ConfigCalls { get; set; }
            public int DpiCalls { get; set; }
        }
    }
}
