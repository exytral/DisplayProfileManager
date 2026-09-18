using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
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
                new List<DisplayConfigHelper.DisplayConfigInfo> { current },
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                new List<DisplayConfigHelper.DisplayConfigInfo> { current },
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
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { first, second };
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
            var live = new List<DisplayConfigHelper.DisplayConfigInfo> { current };
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
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                new List<DisplayConfigHelper.DisplayConfigInfo> { address },
                new List<DisplayConfigHelper.DisplayConfigInfo> { fresh },
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
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
                new List<DisplayConfigHelper.DisplayConfigInfo> { address },
                new List<DisplayConfigHelper.DisplayConfigInfo>(),
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
                new List<DisplayConfigHelper.DisplayConfigInfo> { unrelated },
                new List<DisplayConfigHelper.DisplayConfigInfo> { unrelated },
                new List<DisplayConfigHelper.DisplayConfigInfo> { appearing },
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
                new List<DisplayConfigHelper.DisplayConfigInfo> { savedPort, moved },
                new List<DisplayConfigHelper.DisplayConfigInfo> { savedPort, moved },
                new List<DisplayConfigHelper.DisplayConfigInfo> { freshSavedPort, freshMoved },
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
                new List<DisplayConfigHelper.DisplayConfigInfo> { savedPort },
                new List<DisplayConfigHelper.DisplayConfigInfo> { savedPort },
                new List<DisplayConfigHelper.DisplayConfigInfo> { fresh },
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

        private static ProfileApplyRuntime Runtime(
            List<DisplayConfigHelper.DisplayConfigInfo> activeBefore,
            List<DisplayConfigHelper.DisplayConfigInfo> allPathsBefore,
            List<DisplayConfigHelper.DisplayConfigInfo> activeAfter,
            ApplyProbe probe,
            bool topologyResult)
        {
            int displayRead = 0;
            return new ProfileApplyRuntime(
                () => ++displayRead == 1 ? activeBefore : activeAfter,
                () => allPathsBefore,
                configs =>
                {
                    probe.TopologyCalls++;
                    probe.TopologyConfigs = configs;
                    return topologyResult;
                },
                _ => Task.FromResult(new DisplayConfigHelper.DisplayConfigApplyResult { Success = false }),
                (_, scale, live) =>
                {
                    probe.DpiCalls++;
                    probe.DpiScale = scale;
                    probe.DpiLive = live;
                    return true;
                },
                () => false,
                () => false);
        }

        private static async Task<ProfileManager.ProfileApplyResult> ApplyAsync(Profile profile, ProfileApplyRuntime runtime)
        {
            string appDataFolder = Path.Combine(Path.GetTempPath(), "DpmDpiAuthority-" + Guid.NewGuid().ToString("N"));
            try
            {
                var manager = new ProfileManager(
                    appDataFolder,
                    () => new List<DisplayConfigHelper.DisplayConfigInfo>(),
                    runtime);
                return await manager.ApplyProfileAsync(profile);
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
            DisplayConfigHelper.LUID adapterId,
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

        private static DisplayConfigHelper.DisplayConfigInfo Display(
            DisplayConfigHelper.LUID adapterId,
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

        private static DisplayConfigHelper.LUID Luid(uint lowPart) =>
            new DisplayConfigHelper.LUID { LowPart = lowPart, HighPart = 0 };

        private sealed class ApplyProbe
        {
            public int TopologyCalls { get; set; }
            public List<DisplayConfigHelper.DisplayConfigInfo> TopologyConfigs { get; set; } = new List<DisplayConfigHelper.DisplayConfigInfo>();
            public int DpiCalls { get; set; }
            public uint DpiScale { get; set; }
            public DisplayConfigHelper.DisplayConfigInfo DpiLive { get; set; }
        }
    }
}
