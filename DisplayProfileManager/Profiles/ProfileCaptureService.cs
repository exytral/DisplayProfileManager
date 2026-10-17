using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.ColorProfiles;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Profiles
{
    internal sealed class ProfileCaptureService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        internal sealed class AdvancedColorCaptureUnavailableException(string message) : InvalidOperationException(message) { }

        public static List<DisplayConfigInfo> CaptureDisplayConfigsForProfile(
            Func<List<DisplayConfigInfo>> getDisplayConfigs,
            bool requireKnownAdvancedColor,
            int maxAttempts = 3,
            Action<int> retryDelay = null)
        {
            if (getDisplayConfigs == null) throw new ArgumentNullException(nameof(getDisplayConfigs));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            int attempts = requireKnownAdvancedColor ? maxAttempts : 1;
            List<DisplayConfigInfo> configs = null;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                configs = getDisplayConfigs() ?? [];
                if (!requireKnownAdvancedColor || configs.All(config => config == null || config.IsAdvancedColorInfoAvailable))
                {
                    return configs;
                }

                if (attempt < attempts)
                    retryDelay?.Invoke(attempt);
            }

            var unavailableDisplays = configs
                .Where(config => config != null && !config.IsAdvancedColorInfoAvailable)
                .Select(config => !string.IsNullOrWhiteSpace(config.FriendlyName) ? config.FriendlyName : config.DeviceName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            string suffix = unavailableDisplays.Count > 0 ? $" for {string.Join(", ", unavailableDisplays)}" : string.Empty;
            throw new AdvancedColorCaptureUnavailableException($"Advanced Color state is temporarily unavailable{suffix}; current display capture was deferred.");
        }

        public static void ProjectAdvancedColorForCapture(
            DisplaySetting setting,
            DisplayConfigInfo live,
            bool requireKnownAdvancedColor)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            if (live == null) throw new ArgumentNullException(nameof(live));

            if (!live.IsAdvancedColorInfoAvailable)
            {
                if (requireKnownAdvancedColor) throw new AdvancedColorCaptureUnavailableException("Advanced Color state became unavailable while current display settings were being captured.");

                return;
            }

            setting.IsHdrSupported = live.IsHdrSupported;
            setting.IsWcgSupported = live.IsWcgSupported;
            setting.LastKnownHdrSupported = live.IsHdrSupported;
            setting.LastKnownWcgSupported = live.IsWcgSupported;
            setting.IsHdrEnabled = live.IsHdrEnabled;
            setting.IsWcgEnabled = live.IsWcgEnabled;
        }

        public async Task<List<DisplaySetting>> GetCurrentDisplaySettingsAsync()
        {
            return await Task.Run(() =>
            {
                var settings = new List<DisplaySetting>();

                try
                {
                    _logger.Debug("Reading current display settings...");

                    List<DisplayInfo> displays = GdiDisplayAdapter.GetDisplays();

                    bool requireKnownAdvancedColor = AdvancedColorService.IsWindows24H2OrGreater();
                    List<DisplayConfigInfo> displayConfigs = CaptureDisplayConfigsForProfile(
                        DisplayConfigService.GetDisplayConfigs,
                        requireKnownAdvancedColor,
                        maxAttempts: 3,
                        retryDelay: attempt => System.Threading.Thread.Sleep(100 * attempt));

                    if (displayConfigs.Count > 0)
                    {
                        for (int i = 0; i < displayConfigs.Count; i++)
                        {
                            var foundConfig = displayConfigs[i];
                            var foundDisplay = displays.Find(x => x.DeviceName == foundConfig.DeviceName);

                            DpiScalingInfo dpiInfo = DpiScalingAdapter.GetDPIScalingInfo(foundConfig.DeviceName, foundConfig);

                            DisplaySetting setting = new()
                            {
                                // Identity
                                DeviceName = foundConfig.DeviceName,
                                DeviceString = foundDisplay?.DeviceString ?? foundConfig.DeviceName,
                                ReadableDeviceName = !string.IsNullOrEmpty(foundConfig.FriendlyName) ? foundConfig.FriendlyName : foundConfig.DeviceName,
                                ManufacturerName = foundConfig.ManufacturerName,
                                ProductCodeID = foundConfig.ProductCodeID,
                                AdapterLuid = foundConfig.AdapterId,
                                TargetId = foundConfig.TargetId,
                                // State
                                IsEnabled = foundConfig.IsEnabled,
                                IsPrimary = foundDisplay?.IsPrimary ?? foundConfig.IsPrimary,
                                // Layout
                                DisplayPositionX = foundConfig.DisplayPositionX,
                                DisplayPositionY = foundConfig.DisplayPositionY,
                                // Configuration
                                Width = foundConfig.Width,
                                Height = foundConfig.Height,
                                Frequency = foundDisplay?.Frequency ?? (int)foundConfig.RefreshRate,
                                Rotation = (int)foundConfig.Rotation,
                                DpiScaling = dpiInfo.Current
                            };
                            ProjectAdvancedColorForCapture(setting, foundConfig, requireKnownAdvancedColor);
                            setting.ColorProfile = ColorProfileAdapter.GetDisplayDefaultColorProfile(foundConfig.AdapterId, foundConfig.SourceId);
                            // Native
                            setting.NativeWidth = foundConfig.NativeWidth;
                            setting.NativeHeight = foundConfig.NativeHeight;

                            try
                            {
                                var capabilities = GdiDisplayAdapter.GetDisplayCapabilities(setting.DeviceName);
                                setting.AvailableResolutions = capabilities.Resolutions;
                                setting.AvailableRefreshRates = capabilities.RefreshRates
                                    .Where(kv => kv.Value.Count > 0)
                                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                                setting.AvailableDpiScaling = [.. DpiScalingAdapter.GetSupportedDpiScalingOnly(setting.DeviceName, foundConfig)];

                                _logger.Debug($"Captured options for {setting.DeviceName}: " +
                                    $"{setting.AvailableResolutions.Count} resolutions, " +
                                    $"{setting.AvailableRefreshRates.Count} refresh-rate mappings, " +
                                    $"{setting.AvailableDpiScaling.Count} DPI values");
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Error capturing available options for {setting.DeviceName}");
                            }

                            settings.Add(setting);
                        }

                        _logger.Info($"Created {settings.Count} display settings from {displayConfigs.Count} configs");

                        // Detect clone groups from live CCD source relationships
                        var cloneGroups = displayConfigs
                            .Select((config, index) => new { config, index })
                            .Where(item => item.config.IsEnabled)
                            .GroupBy(item => new { item.config.AdapterId.HighPart, item.config.AdapterId.LowPart, item.config.SourceId })
                            .Where(group => group.Count() > 1)
                            .ToList();
                        if (cloneGroups.Any())
                        {
                            int cloneGroupIndex = 1;
                            foreach (var group in cloneGroups)
                            {
                                string cloneGroupId = $"clone-group-{cloneGroupIndex}";
                                bool first = true;
                                foreach (var item in group)
                                {
                                    var setting = settings[item.index];
                                    setting.CloneGroupId = cloneGroupId;
                                    setting.IsCloneSource = first;
                                    first = false;
                                    _logger.Info($"Detected clone group '{cloneGroupId}': " + $"{setting.ReadableDeviceName} (TargetId: {setting.TargetId})");
                                }
                                cloneGroupIndex++;
                            }
                            _logger.Info($"Detected {TextFormat.Plural(cloneGroups.Count, "clone group")} with {cloneGroups.Sum(g => g.Count())} total displays");
                        }
                    }
                }
                catch (AdvancedColorCaptureUnavailableException ex)
                {
                    _logger.Warn(ex.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting current display settings");
                }

                return settings;
            });
        }
    }
}
