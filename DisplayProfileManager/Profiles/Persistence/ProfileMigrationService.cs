using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NLog;
using Newtonsoft.Json.Linq;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.ColorProfiles;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Profiles.Persistence
{
    internal sealed class ProfileMigrationService(SettingsService settingsService)
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly SettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        public async Task<bool> MigrateProfileAsync(
            Profile profile,
            List<DisplayConfigInfo> liveConfigs,
            List<DisplayConfigInfo> addressConfigs,
            string rawJson = null)
        {
            bool changed = false;

            // Backfill display name
            if (profile.SchemaVersion < 1)
            {
                foreach (var setting in profile.DisplaySettings)
                {
                    var match = ProfileHardwareReconciler.ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (match != null)
                    {
                        if (!string.IsNullOrEmpty(match.FriendlyName))
                        {
                            setting.ReadableDeviceName = match.FriendlyName;
                            changed = true;
                        }
                    }
                    else
                        _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping backfill");
                }

                profile.SchemaVersion = 1;
                changed = true;
            }

            // Add icon
            if (profile.SchemaVersion < 2)
            {
                profile.SchemaVersion = 2;
                changed = true;
            }

            // Backfill color profile
            if (profile.SchemaVersion < 3)
            {
                foreach (var setting in profile.DisplaySettings)
                {
                    if (string.IsNullOrEmpty(setting.ColorProfile))
                    {
                        var match = ProfileHardwareReconciler.ResolveHardwareBackfillDisplay(setting, liveConfigs);
                        if (match != null)
                        {
                            try
                            {
                                setting.ColorProfile = ColorProfileAdapter.GetDisplayDefaultColorProfile(
                                    match.AdapterId, match.SourceId);
                                if (setting.ColorProfile != null)
                                    changed = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, $"Migration: failed to get color profile for {setting.ReadableDeviceName}");
                            }
                        }
                        else
                            _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping color profile backfill");
                    }
                }

                profile.SchemaVersion = 3;
                changed = true;
            }

            // Migrate default profile and EDID identity
            if (profile.SchemaVersion < 4)
            {
                if (rawJson != null)
                {
                    try
                    {
                        if (JObject.Parse(rawJson)["isDefault"]?.Value<bool>() == true)
                        {
                            var existing = _settingsService.GetDefaultProfileId();
                            if (!string.IsNullOrEmpty(existing) && existing != profile.Id)
                                _logger.Warn($"Migration: '{profile.Name}' also claims default -> keeping {existing}");
                            else
                                await _settingsService.SetDefaultProfileIdAsync(profile.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not read isDefault flag");
                    }
                }

                foreach (var setting in profile.DisplaySettings)
                {
                    var match = ProfileHardwareReconciler.ResolveHardwareBackfillDisplay(setting, liveConfigs);
                    if (match == null)
                    {
                        _logger.Info($"Migration: {setting.ReadableDeviceName} (TargetId {setting.TargetId}) not connected, skipping identity backfill");
                        continue;
                    }

                    setting.ManufacturerName = match.ManufacturerName;
                    setting.ProductCodeID = match.ProductCodeID;
                    changed = true;
                }

                profile.SchemaVersion = 4;
                changed = true;
            }

            // Consolidate subsystem enablement into the settings objects
            if (profile.SchemaVersion < 5)
            {
                profile.SchemaVersion = 5;
                changed = true;
            }

            // Schema 6 resets legacy native dimensions and wallpaper state because their prior meanings/ownership are incompatible; resolved addresses backfill preferred/native size, unresolved displays remain 0x0 for self-heal, and HDR/WCG stays exclusive after Advanced Color normalization.
            if (profile.SchemaVersion < 6)
            {
                var currentAddresses = addressConfigs ?? [];
                foreach (var setting in profile.DisplaySettings ?? [])
                {
                    if (setting.IsHdrEnabled)
                        setting.IsWcgEnabled = false;

                    setting.NativeWidth = 0;
                    setting.NativeHeight = 0;

                    var match = ProfileHardwareReconciler.ResolveHardwareBackfillDisplay(setting, currentAddresses);
                    if (match?.NativeWidth > 0 && match.NativeHeight > 0)
                    {
                        setting.NativeWidth = match.NativeWidth;
                        setting.NativeHeight = match.NativeHeight;
                    }
                }

                profile.WallpaperSettings = new WallpaperSettings();
                profile.SchemaVersion = 6;
                changed = true;
            }

            return changed;
        }
    }
}
