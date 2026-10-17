using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json;

using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Profiles
{
    internal sealed class ProfileDraftFactory
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public static Profile CreateNewProfile(string name = "", string description = "") =>
            new(name, description)
            {
                SchemaVersion = ProfileSerializer.CurrentSchemaVersion
            };

        internal static Profile CreateDefaultProfile() => CreateNewProfile("Default", "Default system profile created automatically");

        public static Profile CreateWorkingCopy(Profile source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var clone = JsonConvert.DeserializeObject<Profile>(JsonConvert.SerializeObject(source)) ?? throw new InvalidOperationException("Could not create profile working copy.");

            int displayCount = Math.Min(source.DisplaySettings?.Count ?? 0, clone.DisplaySettings?.Count ?? 0);
            for (int i = 0; i < displayCount; i++)
            {
                var sourceSetting = source.DisplaySettings[i];
                var target = clone.DisplaySettings[i];

                target.AdapterLuid = sourceSetting.AdapterLuid;
                target.OriginalSettings = sourceSetting.OriginalSettings;
                target.OriginalPositionX = sourceSetting.OriginalPositionX;
                target.OriginalPositionY = sourceSetting.OriginalPositionY;
                target.OriginalIsPrimary = sourceSetting.OriginalIsPrimary;
                target.OriginalWidth = sourceSetting.OriginalWidth;
                target.OriginalHeight = sourceSetting.OriginalHeight;
                target.OriginalFrequency = sourceSetting.OriginalFrequency;
                target.OriginalRotation = sourceSetting.OriginalRotation;
                target.OriginalDpiScaling = sourceSetting.OriginalDpiScaling;
                target.IsHdrSupported = sourceSetting.IsHdrSupported;
                target.IsWcgSupported = sourceSetting.IsWcgSupported;
                target.OriginalIsHdrEnabled = sourceSetting.OriginalIsHdrEnabled;
                target.OriginalIsWcgEnabled = sourceSetting.OriginalIsWcgEnabled;
                target.OriginalColorProfile = sourceSetting.OriginalColorProfile;
            }

            return clone;
        }

        public Profile DuplicateProfile(Profile sourceProfile, IEnumerable<Profile> profiles)
        {
            if (sourceProfile == null)
            {
                return null;
            }

            return new Profile
            {
                Id = Guid.NewGuid().ToString(),
                Name = ProfileNamingPolicy.GetDuplicateProfileName(profiles, sourceProfile.Name),
                Description = sourceProfile.Description,
                Icon = sourceProfile.Icon,
                CreatedDate = DateTime.Now,
                LastModifiedDate = DateTime.Now,
                SchemaVersion = ProfileSerializer.CurrentSchemaVersion,
                DisplaySettings = [.. sourceProfile.DisplaySettings.Select(ds => new DisplaySetting
                {
                    DeviceName = ds.DeviceName,
                    DeviceString = ds.DeviceString,
                    ReadableDeviceName = ds.ReadableDeviceName,
                    ManufacturerName = ds.ManufacturerName,
                    ProductCodeID = ds.ProductCodeID,
                    TargetId = ds.TargetId,
                    CloneGroupId = ds.CloneGroupId,
                    IsCloneSource = ds.IsCloneSource,
                    IsEnabled = ds.IsEnabled,
                    IsPrimary = ds.IsPrimary,
                    DisplayPositionX = ds.DisplayPositionX,
                    DisplayPositionY = ds.DisplayPositionY,
                    Width = ds.Width,
                    Height = ds.Height,
                    Frequency = ds.Frequency,
                    Rotation = ds.Rotation,
                    DpiScaling = ds.DpiScaling,
                    IsHdrSupported = ds.IsHdrSupported,
                    IsWcgSupported = ds.IsWcgSupported,
                    LastKnownHdrSupported = ds.LastKnownHdrSupported,
                    LastKnownWcgSupported = ds.LastKnownWcgSupported,
                    IsHdrEnabled = ds.IsHdrEnabled,
                    IsWcgEnabled = ds.IsWcgEnabled,
                    ColorProfile = ds.ColorProfile,
                    NativeWidth = ds.NativeWidth,
                    NativeHeight = ds.NativeHeight,
                    AvailableResolutions = ds.AvailableResolutions ?? [],
                    AvailableRefreshRates = ds.AvailableRefreshRates != null
                        ? new Dictionary<string, List<int>>(ds.AvailableRefreshRates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
                        : [],
                    AvailableDpiScaling = ds.AvailableDpiScaling ?? []
                })],
                AudioSettings = sourceProfile.AudioSettings != null ? new AudioSetting
                {
                    Enabled = sourceProfile.AudioSettings.Enabled,
                    DefaultPlaybackDeviceId = sourceProfile.AudioSettings.DefaultPlaybackDeviceId,
                    PlaybackDeviceName = sourceProfile.AudioSettings.PlaybackDeviceName,
                    DefaultCaptureDeviceId = sourceProfile.AudioSettings.DefaultCaptureDeviceId,
                    CaptureDeviceName = sourceProfile.AudioSettings.CaptureDeviceName,
                    ApplyPlaybackDevice = sourceProfile.AudioSettings.ApplyPlaybackDevice,
                    ApplyCaptureDevice = sourceProfile.AudioSettings.ApplyCaptureDevice
                } : new AudioSetting(),
                WallpaperSettings = sourceProfile.WallpaperSettings != null ? new WallpaperSettings
                {
                    Enabled = sourceProfile.WallpaperSettings.Enabled,
                    Mode = sourceProfile.WallpaperSettings.Mode,
                    SolidColorArgb = sourceProfile.WallpaperSettings.SolidColorArgb,
                    Position = sourceProfile.WallpaperSettings.Position,
                    PerMonitor = new Dictionary<string, MonitorWallpaper>(
                        sourceProfile.WallpaperSettings.PerMonitor.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new MonitorWallpaper { Path = kvp.Value.Path, MonitorId = kvp.Value.MonitorId })),
                    SlideshowConfig = sourceProfile.WallpaperSettings.SlideshowConfig != null ? new SlideshowConfig
                    {
                        IntervalSeconds = sourceProfile.WallpaperSettings.SlideshowConfig.IntervalSeconds,
                        SourcePaths = sourceProfile.WallpaperSettings.SlideshowConfig.SourcePaths,
                        Shuffle = sourceProfile.WallpaperSettings.SlideshowConfig.Shuffle
                    } : null
                } : null,
                ScriptSettings = sourceProfile.ScriptSettings != null ? new ScriptSettings
                {
                    Enabled = sourceProfile.ScriptSettings.Enabled,
                    Scripts = sourceProfile.ScriptSettings.Scripts?
                        .Select(s => new Script
                        {
                            FileName = s.FileName,
                            Arguments = s.Arguments,
                            IsEnabled = s.IsEnabled
                        })
                        .ToList() ?? []
                } : new ScriptSettings(),
                HotkeyConfig = new HotkeyConfig()
            };
        }

        public async Task<Profile> CreateDefaultProfileAsync(
            ProfileCaptureService captureService,
            Func<Profile, Task<bool>> addProfileAsync,
            SettingsService settingsService,
            Action<string> setCurrentProfileId)
        {
            var defaultProfile = CreateDefaultProfile();
            try
            {
                defaultProfile.DisplaySettings.AddRange(await captureService.GetCurrentDisplaySettingsAsync());

                if (!await addProfileAsync(defaultProfile))
                {
                    _logger.Error("Failed to persist default profile");
                    return null;
                }

                setCurrentProfileId(defaultProfile.Id);
                await settingsService.SetCurrentProfileIdAsync(defaultProfile.Id);
                await settingsService.SetDefaultProfileIdAsync(defaultProfile.Id);
                return defaultProfile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating default profile");
                return null;
            }
        }
    }
}
