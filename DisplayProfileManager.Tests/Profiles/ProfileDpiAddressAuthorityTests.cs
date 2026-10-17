using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileDpiAddressAuthorityTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_InsufficientEvidenceWithAbortDisabled_DoesNotMutateDpi()
        {
            var storedAdapter = Luid(1);
            var currentAdapter = Luid(2);
            var setting = Setting(storedAdapter, 33, 7);
            var current = Display(currentAdapter, 33, 9);
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [current],
                [],
                [current],
                probe,
                topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, probe.TopologyCalls);
            Assert.AreEqual(0, probe.DpiCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_AmbiguousIdentityWithAbortDisabled_DoesNotMutateDpi()
        {
            var storedAdapter = Luid(1);
            var first = Display(storedAdapter, 33, 3, "MAN", "1234");
            var second = Display(Luid(2), 44, 4, "MAN", "1234");
            var setting = Setting(storedAdapter, 999, 7, "MAN", "1234");
            var live = new List<DisplayConfigInfo> { first, second };
            var probe = new ApplyProbe();
            var runtime = Runtime(live, live, live, probe, topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, probe.TopologyCalls);
            Assert.AreEqual(0, probe.DpiCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_UniquePortEdidMismatch_UsesGenericFallbackAndFreshDpiAddress()
        {
            var storedAdapter = Luid(1);
            var current = Display(Luid(2), 33, 3, "DEV", "9999");
            var setting = Setting(storedAdapter, 33, 7, "MAN", "1234");
            var live = new List<DisplayConfigInfo> { current };
            var probe = new ApplyProbe();
            var runtime = Runtime(live, live, live, probe, topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.DpiChanged);
            Assert.AreEqual(1, probe.TopologyCalls);
            Assert.AreEqual(1, probe.DpiCalls);
            Assert.IsTrue(CcdAddress.LuidEquals(current.AdapterId, probe.DpiLive.AdapterId));
            Assert.AreEqual(current.SourceId, probe.DpiLive.SourceId);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_ResolvedStaleLuid_UsesSemanticTopologyGroupAndFreshSourceForDpi()
        {
            var storedAdapter = Luid(1);
            var currentAdapter = Luid(2);
            var setting = Setting(storedAdapter, 33, 42, "MAN", "1234", 150);
            var address = Display(currentAdapter, 33, 9, "MAN", "1234");
            address.IsEnabled = false;
            var fresh = Display(currentAdapter, 33, 77, "MAN", "1234", "DISPLAY-FRESH");
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [],
                [address],
                [fresh],
                probe,
                topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.DpiChanged);
            Assert.AreEqual(1, probe.TopologyCalls);
            Assert.AreEqual(0u, probe.TopologyConfigs[0].SourceId);
            Assert.IsTrue(CcdAddress.LuidEquals(currentAdapter, probe.TopologyConfigs[0].AdapterId));
            Assert.AreEqual(1, probe.DpiCalls);
            Assert.AreEqual(150u, probe.DpiScale);
            Assert.AreEqual(77u, probe.DpiLive.SourceId);
            Assert.IsTrue(CcdAddress.LuidEquals(currentAdapter, probe.DpiLive.AdapterId));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_AuthorizedEndpointMissingAfterTopology_SkipsDpiSafely()
        {
            var setting = Setting(Luid(1), 33, 42, "MAN", "1234", 150);
            var address = Display(Luid(2), 33, 9, "MAN", "1234");
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [],
                [address],
                [],
                probe,
                topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.DpiChanged);
            Assert.AreEqual(1, probe.TopologyCalls);
            Assert.AreEqual(0, probe.DpiCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_AbsentBeforeTopology_DoesNotAuthorizeAppearingTargetForDpi()
        {
            var setting = Setting(Luid(1), 33, 7, "MAN", "1234");
            var unrelated = Display(Luid(3), 11, 1, "DEV", "9999");
            var appearing = Display(Luid(2), 33, 5, "MAN", "1234");
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [unrelated],
                [unrelated],
                [appearing],
                probe,
                topologyResult: false);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, probe.TopologyCalls, "Absent is safe topology input, not an unsafe resolution status.");
            Assert.AreEqual(0, probe.DpiCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_DuplicateNumericTargets_ReacquiresAuthorizedAdapterOnly()
        {
            var savedAdapter = Luid(1);
            var currentAdapter = Luid(2);
            var setting = Setting(savedAdapter, 33, 42, "MAN", "1234", 175);
            var savedPort = Display(savedAdapter, 33, 3, "DEV", "9999", "DISPLAY-A");
            var moved = Display(currentAdapter, 33, 9, "MAN", "1234", "DISPLAY-B");
            var freshSavedPort = Display(savedAdapter, 33, 10, "DEV", "9999", "DISPLAY-A");
            var freshMoved = Display(currentAdapter, 33, 20, "MAN", "1234", "DISPLAY-B");
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [savedPort, moved],
                [savedPort, moved],
                [freshSavedPort, freshMoved],
                probe,
                topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, probe.DpiCalls);
            Assert.AreEqual(175u, probe.DpiScale);
            Assert.AreEqual(20u, probe.DpiLive.SourceId);
            Assert.IsTrue(CcdAddress.LuidEquals(currentAdapter, probe.DpiLive.AdapterId));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_UniqueSavedPortFallback_RemainsAuthorizedForDpi()
        {
            var savedAdapter = Luid(1);
            var setting = Setting(savedAdapter, 33, 42, "MAN", "1234", 125);
            var savedPort = Display(savedAdapter, 33, 9, "MAN", "5678", "DISPLAY-PORT");
            var fresh = Display(savedAdapter, 33, 55, "MAN", "5678", "DISPLAY-PORT");
            var probe = new ApplyProbe();
            var runtime = Runtime(
                [savedPort],
                [savedPort],
                [fresh],
                probe,
                topologyResult: true);

            var result = await ApplyAsync(MakeProfile(setting), runtime);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, probe.TopologyCalls);
            Assert.AreEqual(1, probe.DpiCalls);
            Assert.AreEqual(125u, probe.DpiScale);
            Assert.AreEqual(55u, probe.DpiLive.SourceId);
            Assert.IsTrue(CcdAddress.LuidEquals(savedAdapter, probe.DpiLive.AdapterId));
        }

        private static IDisplayApplyAdapter Runtime(
            List<DisplayConfigInfo> activeBefore,
            List<DisplayConfigInfo> allPathsBefore,
            List<DisplayConfigInfo> activeAfter,
            ApplyProbe probe,
            bool topologyResult) =>
            new TestDisplayApplyAdapter(activeBefore, allPathsBefore, activeAfter, probe, topologyResult);

        private static async Task<ProfileApplyResult> ApplyAsync(Profile profile, IDisplayApplyAdapter runtime)
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDpiAuthority-" + Guid.NewGuid().ToString("N"));
            var settings = new SettingsService(new SettingsStore(appDataFolder));
            settings.Settings.AbortOnApplyFailure = false;
            settings.Settings.RollbackAfterApplyFailure = false;

            try
            {
                var profiles = new ProfileService(
                    new ProfileStore(appDataFolder),
                    new ProfileMigrationService(settings),
                    new ProfileDraftFactory(),
                    new ProfileCaptureService(),
                    settings,
                    () => [],
                    () => []);
                var scripts = new ScriptLibrary(appDataFolder);
                var apply = new ProfileApplyService(
                    new ProfileApplyQueue(),
                    runtime,
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

        private static Profile MakeProfile(DisplaySetting setting)
        {
            var profile = new Profile("DPI authority regression");
            profile.DisplaySettings.Add(setting);
            return profile;
        }

        private static DisplaySetting Setting(
            AdapterLuid adapterId,
            uint targetId,
            uint _sourceId,
            string manufacturer = "",
            string product = "",
            uint dpiScaling = 150)
        {
            var setting = new DisplaySettingBuilder()
                .WithName($"Target {targetId}")
                .WithEdid(manufacturer, product)
                .WithTargetId(targetId)
                .WithDpi((int)dpiScaling)
                .Build();
            setting.AdapterLuid = adapterId;
            return setting;
        }

        private static DisplayConfigInfo Display(
            AdapterLuid adapterId,
            uint targetId,
            uint sourceId,
            string manufacturer = "",
            string product = "",
            string deviceName = null)
        {
            var display = new DisplayConfigInfoBuilder()
                .WithDeviceName(deviceName ?? $"DISPLAY-{adapterId.LowPart}-{targetId}")
                .WithFriendlyName($"{CcdAddress.FormatLuid(adapterId)}:{targetId}")
                .WithEdid(manufacturer, product)
                .WithTargetId(targetId)
                .WithRawTargetId(targetId)
                .Build();
            display.AdapterId = adapterId;
            display.SourceId = sourceId;
            return display;
        }

        private static AdapterLuid Luid(uint lowPart) =>
            new()
            { LowPart = lowPart, HighPart = 0 };

        private sealed class TestDisplayApplyAdapter(
            List<DisplayConfigInfo> activeBefore,
            List<DisplayConfigInfo> allPathsBefore,
            List<DisplayConfigInfo> activeAfter,
            ApplyProbe probe,
            bool topologyResult) : IDisplayApplyAdapter
        {
            private readonly List<DisplayConfigInfo> _activeBefore = activeBefore;
            private readonly List<DisplayConfigInfo> _allPathsBefore = allPathsBefore;
            private readonly List<DisplayConfigInfo> _activeAfter = activeAfter;
            private readonly ApplyProbe _probe = probe;
            private readonly bool _topologyResult = topologyResult;
            private int _displayRead;

            public List<DisplayConfigInfo> GetActiveDisplays() =>
                ++_displayRead == 1 ? _activeBefore : _activeAfter;

            public List<DisplayConfigInfo> GetDisplayAddresses() => _allPathsBefore;

            public bool ApplyTopology(List<DisplayConfigInfo> displays)
            {
                _probe.TopologyCalls++;
                _probe.TopologyConfigs = displays;
                return _topologyResult;
            }

            public Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays) =>
                Task.FromResult(new DisplayConfigService.DisplayConfigApplyResult { Success = false });

            public bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay)
            {
                _probe.DpiCalls++;
                _probe.DpiScale = setting.DpiScaling;
                _probe.DpiLive = liveDisplay;
                return true;
            }

            public bool ApplyAdvancedColor(List<DisplayConfigInfo> displays) => true;
            public bool ApplyColorProfiles(List<DisplayConfigInfo> displays) => true;
        }

        private sealed class ApplyProbe
        {
            public int TopologyCalls { get; set; }
            public List<DisplayConfigInfo> TopologyConfigs { get; set; } = [];
            public int DpiCalls { get; set; }
            public uint DpiScale { get; set; }
            public DisplayConfigInfo DpiLive { get; set; }
        }
    }
}
