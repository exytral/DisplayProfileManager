using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileApplyEvidenceScopeTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_SuccessFailureAndException_AllDisarmEvidenceScope()
        {
            var evidence = new FakeEvidenceScopeProvider();

            var success = await CreateHarness(evidence, new FakeDisplayApplyAdapter(true)).ApplyAsync(new Profile("Success"));
            Assert.IsTrue(success.Success);
            Assert.AreEqual(0, evidence.ActiveCount);

            var failureAdapter = new FakeDisplayApplyAdapter(false);
            var failureHarness = CreateHarness(evidence, failureAdapter);
            await failureHarness.Settings.LoadSettingsAsync();
            await failureHarness.Settings.SetRollbackAfterApplyFailureAsync(false);
            var failure = await failureHarness.Service.ApplyProfileAsync(new Profile("Failure"));
            Assert.IsFalse(failure.Success);
            Assert.AreEqual(0, evidence.ActiveCount);

            var throwing = new FakeDisplayApplyAdapter(true) { ThrowOnTopology = true };
            var thrown = await CreateHarness(evidence, throwing).ApplyAsync(new Profile("Thrown"));
            Assert.IsFalse(thrown.Success);
            Assert.AreEqual(0, evidence.ActiveCount);

            Assert.AreEqual(3, evidence.BeginCount);
            Assert.AreEqual(3, evidence.DisposeCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_QueuedSecondRequest_DoesNotArmBeforeQueueEntry()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(true) { BlockFirstConfig = true };
            var harness = CreateHarness(evidence, adapter);
            await harness.Settings.LoadSettingsAsync();

            Task<ProfileApplyResult> first = harness.Service.ApplyProfileAsync(new Profile("A"));
            Assert.IsTrue(adapter.FirstConfigEntered.Wait(2000, TestContext.CancellationToken));

            Task<ProfileApplyResult> second = harness.Service.ApplyProfileAsync(new Profile("B"));
            Thread.Sleep(50);

            Assert.AreEqual(1, evidence.BeginCount);
            Assert.AreEqual(1, evidence.ActiveCount);

            adapter.ReleaseFirstConfig.TrySetResult(true);
            await Task.WhenAll(first, second);

            Assert.AreEqual(2, evidence.BeginCount);
            Assert.AreEqual(2, evidence.DisposeCount);
            Assert.AreEqual(1, evidence.MaxActiveCount);
            Assert.AreEqual(0, evidence.ActiveCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_PreviousProfileRollback_ReusesOuterEvidenceScope()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(false, true);
            var previous = new Profile("Previous");
            var failed = new Profile("Failed");
            var harness = CreateHarness(evidence, adapter, previous, previous.Id);
            await harness.Settings.LoadSettingsAsync();

            var result = await harness.Service.ApplyProfileAsync(failed);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, evidence.BeginCount);
            Assert.AreEqual(1, evidence.DisposeCount);
            Assert.AreEqual(1, evidence.MaxActiveCount);
            Assert.AreEqual(0, evidence.ActiveCount);
            Assert.AreEqual(2, adapter.TopologyCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_ForcedTopologyStage_FailsOtherwiseSuccessfulTopology()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(true);
            var harness = CreateHarness(
                evidence,
                adapter,
                forceApplyFailureStage: ProfileApplyFailureStage.Topology);
            await harness.Settings.LoadSettingsAsync();
            await harness.Settings.SetRollbackAfterApplyFailureAsync(false);

            ProfileApplyResult result = await harness.Service.ApplyProfileAsync(new Profile("ForcedTopology"));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, adapter.TopologyCalls);
            Assert.AreEqual(0, adapter.ConfigCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_ForcedDisplayConfigurationStage_FailsOtherwiseSuccessfulConfiguration()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(true);
            var harness = CreateHarness(
                evidence,
                adapter,
                forceApplyFailureStage: ProfileApplyFailureStage.DisplayConfiguration);
            await harness.Settings.LoadSettingsAsync();
            await harness.Settings.SetRollbackAfterApplyFailureAsync(false);

            ProfileApplyResult result = await harness.Service.ApplyProfileAsync(new Profile("ForcedConfig"));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, adapter.TopologyCalls);
            Assert.AreEqual(1, adapter.ConfigCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_NullEvidenceProvider_PreservesOrdinarySuccess()
        {
            var adapter = new FakeDisplayApplyAdapter(true);
            var harness = CreateHarness(null, adapter);

            ProfileApplyResult result = await harness.ApplyAsync(new Profile("NoEvidence"));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, adapter.TopologyCalls);
            Assert.AreEqual(1, adapter.ConfigCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_NoForcedStage_PreservesOrdinarySuccess()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(true);
            var harness = CreateHarness(evidence, adapter);

            ProfileApplyResult result = await harness.ApplyAsync(new Profile("Ordinary"));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, adapter.TopologyCalls);
            Assert.AreEqual(1, adapter.ConfigCalls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_Rollback_DoesNotReinjectForcedTopologyFailure()
        {
            var evidence = new FakeEvidenceScopeProvider();
            var adapter = new FakeDisplayApplyAdapter(true, true);
            var previous = new Profile("Previous");
            var failed = new Profile("Failed");
            var harness = CreateHarness(
                evidence,
                adapter,
                previous,
                previous.Id,
                ProfileApplyFailureStage.Topology);
            await harness.Settings.LoadSettingsAsync();

            ProfileApplyResult result = await harness.Service.ApplyProfileAsync(failed);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(2, adapter.TopologyCalls);
            Assert.AreEqual(1, adapter.ConfigCalls);
        }

        private static ApplyHarness CreateHarness(
            FakeEvidenceScopeProvider evidence,
            FakeDisplayApplyAdapter adapter,
            Profile knownProfile = null,
            string currentProfileId = null,
            ProfileApplyFailureStage? forceApplyFailureStage = null)
        {
            var settings = new SettingsService(new MemorySettingsStore(new AppSettings
            {
                CurrentProfileId = currentProfileId ?? string.Empty
            }));
            string current = currentProfileId;
            List<Profile> profiles = knownProfile == null ? [] : [knownProfile];
            var service = new ProfileApplyService(
                new ProfileApplyQueue(),
                adapter,
                new FakeWallpaperService(),
                new FakeAudioDeviceService(),
                new FakeScriptRunner(),
                settings,
                id => profiles.Find(p => p.Id == id),
                () => profiles,
                _ => Task.FromResult(true),
                id => current = id,
                () => current,
                evidence,
                forceApplyFailureStage);

            return new ApplyHarness(service, settings);
        }

        private sealed class ApplyHarness
        {
            internal ProfileApplyService Service { get; }
            internal SettingsService Settings { get; }

            internal ApplyHarness(ProfileApplyService service, SettingsService settings)
            {
                Service = service;
                Settings = settings;
            }

            internal async Task<ProfileApplyResult> ApplyAsync(Profile profile)
            {
                await Settings.LoadSettingsAsync();
                return await Service.ApplyProfileAsync(profile);
            }
        }

        private sealed class FakeEvidenceScopeProvider : IDisplayEventEvidenceScopeProvider
        {
            private int _active;

            internal int BeginCount { get; private set; }
            internal int DisposeCount { get; private set; }
            internal int ActiveCount => Volatile.Read(ref _active);
            internal int MaxActiveCount { get; private set; }

            public Task<IAsyncDisposable> BeginApplyEvidenceScopeAsync()
            {
                BeginCount++;
                int active = Interlocked.Increment(ref _active);
                MaxActiveCount = Math.Max(MaxActiveCount, active);
                return Task.FromResult<IAsyncDisposable>(new Lease(this));
            }

            private sealed class Lease : IAsyncDisposable
            {
                private FakeEvidenceScopeProvider _owner;

                internal Lease(FakeEvidenceScopeProvider owner) => _owner = owner;

                public ValueTask DisposeAsync()
                {
                    var owner = Interlocked.Exchange(ref _owner, null);
                    if (owner != null)
                    {
                        owner.DisposeCount++;
                        Interlocked.Decrement(ref owner._active);
                    }

                    return ValueTask.CompletedTask;
                }
            }
        }

        private sealed class FakeDisplayApplyAdapter : IDisplayApplyAdapter
        {
            private readonly Queue<bool> _topologyResults;

            internal ManualResetEventSlim FirstConfigEntered { get; } = new();
            internal TaskCompletionSource<bool> ReleaseFirstConfig { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool BlockFirstConfig { get; set; }
            internal bool ThrowOnTopology { get; set; }
            internal int TopologyCalls { get; private set; }
            private int _configCalls;
            internal int ConfigCalls => _configCalls;

            internal FakeDisplayApplyAdapter(params bool[] topologyResults) =>
                _topologyResults = new Queue<bool>(topologyResults);

            public List<DisplayConfigInfo> GetActiveDisplays() => [];
            public List<DisplayConfigInfo> GetDisplayAddresses() => [];

            public bool ApplyTopology(List<DisplayConfigInfo> displays)
            {
                TopologyCalls++;

                if (ThrowOnTopology) throw new InvalidOperationException("synthetic");

                return _topologyResults.Count == 0 || _topologyResults.Dequeue();
            }

            public async Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays)
            {
                int call = Interlocked.Increment(ref _configCalls);
                if (call == 1 && BlockFirstConfig)
                {
                    FirstConfigEntered.Set();
                    await ReleaseFirstConfig.Task;
                }

                return new DisplayConfigService.DisplayConfigApplyResult { Success = true };
            }

            public bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay) => true;
            public bool ApplyAdvancedColor(List<DisplayConfigInfo> displays) => true;
            public bool ApplyColorProfiles(List<DisplayConfigInfo> displays) => true;
        }

        private sealed class FakeWallpaperService : IWallpaperService
        {
            public WallpaperSettings Capture() => new();
            public bool Apply(WallpaperSettings snapshot) => true;
        }

        private sealed class FakeAudioDeviceService : IAudioDeviceService
        {
            public List<AudioDeviceInfo> GetPlaybackDevices() => [];
            public List<AudioDeviceInfo> GetCaptureDevices() => [];
            public AudioDeviceInfo GetDefaultPlaybackDevice() => null;
            public AudioDeviceInfo GetDefaultCaptureDevice() => null;
            public bool SetDefaultPlaybackDevice(string deviceId) => true;
            public bool SetDefaultCaptureDevice(string deviceId) => true;
            public bool ApplyAudioSettings(AudioSetting settings) => true;
        }

        private sealed class FakeScriptRunner : IScriptRunner
        {
            public void ExecuteScript(Script script)
            {
            }
        }

        private sealed class MemorySettingsStore : ISettingsStore
        {
            private AppSettings _settings;

            public string SettingsFilePath => "Settings.json";
            public string AppDataFolder => ".";

            internal MemorySettingsStore(AppSettings settings) => _settings = settings;

            public Task<AppSettings> LoadAsync() => Task.FromResult(_settings);
            public Task<bool> SaveAsync(AppSettings settings)
            {
                _settings = settings;
                return Task.FromResult(true);
            }
        }
    }
}
