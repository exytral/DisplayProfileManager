using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Pointer;
using DisplayProfileManager.Pointer.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Tests.Profiles
{
    [TestClass]
    public class ProfileApplyDeviceSettingsTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_MouseSectionDisabled_SkipsMouse()
        {
            var harness = new ApplyHarness();
            var profile = new Profile("Profile")
            {
                MouseSettings = new MouseSetting { Enabled = false, ApplyPointerSpeed = true }
            };

            var result = await harness.ApplyAsync(profile);

            Assert.IsTrue(result.Success);
            CollectionAssert.AreEqual(new[] { "topology", "config" }, harness.Calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_MouseEnabled_AppliesAfterDisplayConfiguration()
        {
            var harness = new ApplyHarness();
            var profile = new Profile("Profile") { MouseSettings = new MouseSetting { Enabled = true, ApplyPointerSpeed = true, PointerSpeed = 2 } };

            var result = await harness.ApplyAsync(profile);

            Assert.IsTrue(result.Success);
            CollectionAssert.AreEqual(new[] { "topology", "config", "mouse:2" }, harness.Calls);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ApplyProfileAsync_MouseFailure_ReportsWarningWithoutFailingApply()
        {
            var harness = new ApplyHarness { MouseResult = false };
            var profile = new Profile("Profile")
            {
                MouseSettings = new MouseSetting { Enabled = true, ApplySwapButtons = true }
            };

            var result = await harness.ApplyAsync(profile);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.MouseSuccess);
            string warnings = ProfileApplyFormatter.GetApplyWarningSummary(result);
            StringAssert.StartsWith(warnings, "Mouse");
        }

        private sealed class ApplyHarness : IDisplayApplyAdapter, IMouseSettingsService
        {
            private readonly ProfileApplyService _service;

            internal List<string> Calls { get; } = [];
            internal bool MouseResult { get; set; } = true;

            internal ApplyHarness()
            {
                var settings = new SettingsService(new MemorySettingsStore(new AppSettings()));
                string current = null;
                _service = new ProfileApplyService(
                    new ProfileApplyQueue(),
                    this,
                    new FakeWallpaperService(),
                    new FakeAudioDeviceService(),
                    this,
                    new FakeScriptRunner(),
                    settings,
                    _ => null,
                    () => [],
                    _ => Task.FromResult(true),
                    id => current = id,
                    () => current);
            }

            internal Task<ProfileApplyResult> ApplyAsync(Profile profile) => _service.ApplyProfileAsync(profile);

            public List<DisplayConfigInfo> GetActiveDisplays() => [];
            public List<DisplayConfigInfo> GetDisplayAddresses() => [];

            public bool ApplyTopology(List<DisplayConfigInfo> displays)
            {
                Calls.Add("topology");
                return true;
            }

            public Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays)
            {
                Calls.Add("config");
                return Task.FromResult(new DisplayConfigService.DisplayConfigApplyResult { Success = true });
            }

            public bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay) => true;
            public bool ApplyAdvancedColor(List<DisplayConfigInfo> displays) => true;
            public bool ApplyColorProfiles(List<DisplayConfigInfo> displays) => true;

            public MouseSetting CaptureCurrent() => new();

            public bool ApplyMouseSettings(MouseSetting settings)
            {
                Calls.Add($"mouse:{settings.PointerSpeed}");
                return MouseResult;
            }
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

        private sealed class MemorySettingsStore(AppSettings settings) : ISettingsStore
        {
            private AppSettings _settings = settings;

            public string SettingsFilePath => "Settings.json";
            public string AppDataFolder => ".";

            public Task<AppSettings> LoadAsync() => Task.FromResult(_settings);

            public Task<bool> SaveAsync(AppSettings settings)
            {
                _settings = settings;
                return Task.FromResult(true);
            }
        }
    }
}
