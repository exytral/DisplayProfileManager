using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;

namespace DisplayProfileManager.Profiles.Policy
{
    internal sealed class DisplayApplyAddressAuthorization(
        DisplaySetting setting,
        DisplayAddressResolver.CurrentAddressResolutionStatus status,
        CcdTargetKey? authorizedTarget)
    {
        public DisplaySetting Setting { get; } = setting ?? throw new ArgumentNullException(nameof(setting));
        public DisplayAddressResolver.CurrentAddressResolutionStatus Status { get; } = status;
        public CcdTargetKey? AuthorizedTarget { get; } = authorizedTarget;
    }

    internal static class DisplayApplyPolicy
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        internal static bool TryMapDisplaySettingForApply(
            DisplaySetting setting,
            List<DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigInfo> addressDisplayConfigs,
            out DisplayConfigInfo mapped,
            out DisplayAddressResolver.CurrentAddressResolutionStatus resolutionStatus)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));

            var currentAddress = DisplayAddressResolver.ResolveCurrentApplyAddressDetailed(setting, addressDisplayConfigs, out resolutionStatus);
            if (currentAddress == null && resolutionStatus != DisplayAddressResolver.CurrentAddressResolutionStatus.Absent)
            {
                mapped = null;
                return false;
            }

            var active = currentAddress != null
                ? activeDisplayConfigs?.FirstOrDefault(c => CcdAddress.Target(c).Equals(CcdAddress.Target(currentAddress)))
                : null;

            mapped = new DisplayConfigInfo
            {
                // Current hardware address
                DeviceName = !string.IsNullOrEmpty(currentAddress?.DeviceName) ? currentAddress.DeviceName : setting.DeviceName,
                FriendlyName = setting.ReadableDeviceName,
                ManufacturerName = !string.IsNullOrEmpty(currentAddress?.ManufacturerName) ? currentAddress.ManufacturerName : setting.ManufacturerName,
                ProductCodeID = !string.IsNullOrEmpty(currentAddress?.ProductCodeID) ? currentAddress.ProductCodeID : setting.ProductCodeID,
                MonitorDevicePath = currentAddress?.MonitorDevicePath ?? string.Empty,
                AdapterId = currentAddress?.AdapterId ?? default,
                TargetId = currentAddress?.TargetId ?? setting.TargetId,
                RawTargetId = currentAddress?.RawTargetId ?? setting.TargetId,
                PathIndex = currentAddress?.PathIndex ?? 0,
                OutputTechnology = currentAddress?.OutputTechnology ?? default,
                // Desired topology state
                SourceId = 0,
                IsEnabled = setting.IsEnabled,
                IsPrimary = setting.IsPrimary,
                DisplayPositionX = setting.DisplayPositionX,
                DisplayPositionY = setting.DisplayPositionY,
                Width = setting.Width,
                Height = setting.Height,
                RefreshRate = setting.Frequency,
                Rotation = (DisplayConfigNative.DisplayConfigRotation)setting.Rotation,
                IsHdrSupported = active?.IsHdrSupported ?? setting.IsHdrSupported,
                IsWcgSupported = active?.IsWcgSupported ?? setting.IsWcgSupported,
                IsHdrEnabled = setting.IsHdrEnabled,
                IsWcgEnabled = setting.IsWcgEnabled,
                ColorProfile = setting.ColorProfile
            };
            return true;
        }

        internal static void AssignDesiredSourceGroups(IEnumerable<(DisplaySetting Setting, DisplayConfigInfo Mapped)> mappings)
        {
            var sourceByGroup = new Dictionary<string, uint>(StringComparer.Ordinal);
            var nextSourceByAdapter = new Dictionary<string, uint>(StringComparer.Ordinal);
            int independentOrdinal = 0;

            foreach (var (setting, mapped) in mappings ?? [])
            {
                if (setting == null || mapped == null) continue;

                string adapterKey = CcdAddress.FormatLuid(mapped.AdapterId);
                string semanticGroup = setting.IsPartOfCloneGroup()
                    ? $"clone:{setting.CloneGroupId}"
                    : $"display:{independentOrdinal++}";
                string key = $"{adapterKey}|{semanticGroup}";

                if (!sourceByGroup.TryGetValue(key, out uint sourceId))
                {
                    nextSourceByAdapter.TryGetValue(adapterKey, out sourceId);
                    sourceByGroup[key] = sourceId;
                    nextSourceByAdapter[adapterKey] = sourceId + 1;
                }

                mapped.SourceId = sourceId;
            }
        }

        internal static bool TryMapDisplaySettingsForApply(
            IEnumerable<DisplaySetting> settings,
            List<DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigInfo> addressDisplayConfigs,
            out List<DisplayConfigInfo> mappedDisplayConfigs,
            Action<DisplaySetting, DisplayAddressResolver.CurrentAddressResolutionStatus> unsafeResolution = null)
        {
            mappedDisplayConfigs = [];
            var pendingSettings = (settings ?? []).ToList();
            if (pendingSettings.Count > 0 && (addressDisplayConfigs == null || addressDisplayConfigs.Count == 0))
            {
                foreach (var setting in pendingSettings)
                    unsafeResolution?.Invoke(setting, DisplayAddressResolver.CurrentAddressResolutionStatus.InsufficientEvidence);
                return false;
            }

            bool allSafe = true;
            var mappedPairs = new List<(DisplaySetting Setting, DisplayConfigInfo Mapped)>();
            foreach (var setting in pendingSettings)
            {
                if (!TryMapDisplaySettingForApply(setting, activeDisplayConfigs, addressDisplayConfigs, out var mapped, out var resolutionStatus))
                {
                    allSafe = false;
                    unsafeResolution?.Invoke(setting, resolutionStatus);
                    continue;
                }

                mappedDisplayConfigs.Add(mapped);
                mappedPairs.Add((setting, mapped));
            }

            AssignDesiredSourceGroups(mappedPairs);
            return allSafe;
        }

        internal static DisplayConfigInfo MapDisplaySettingForApply(
            DisplaySetting setting,
            List<DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigInfo> addressDisplayConfigs)
        {
            TryMapDisplaySettingForApply(setting, activeDisplayConfigs, addressDisplayConfigs, out var mapped, out _);
            if (mapped != null)
                AssignDesiredSourceGroups([(setting, mapped)]);
            return mapped;
        }

        public static bool TryMapDisplaySettingsForApply(
            IEnumerable<DisplaySetting> settings,
            List<DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigInfo> addressDisplayConfigs,
            out List<DisplayConfigInfo> mappedDisplayConfigs,
            out List<DisplayApplyAddressAuthorization> authorizations,
            Action<DisplaySetting, DisplayAddressResolver.CurrentAddressResolutionStatus> unsafeResolution = null)
        {
            mappedDisplayConfigs = [];
            authorizations = [];
            var pendingSettings = (settings ?? []).ToList();

            if (pendingSettings.Count > 0 && (addressDisplayConfigs == null || addressDisplayConfigs.Count == 0))
            {
                foreach (var setting in pendingSettings)
                {
                    authorizations.Add(new DisplayApplyAddressAuthorization(
                        setting,
                        DisplayAddressResolver.CurrentAddressResolutionStatus.InsufficientEvidence,
                        null));
                    unsafeResolution?.Invoke(setting, DisplayAddressResolver.CurrentAddressResolutionStatus.InsufficientEvidence);
                }
                return false;
            }

            bool allSafe = true;
            var mappedPairs = new List<(DisplaySetting Setting, DisplayConfigInfo Mapped)>();
            foreach (var setting in pendingSettings)
            {
                bool mappedSafely = TryMapDisplaySettingForApply(
                    setting, activeDisplayConfigs, addressDisplayConfigs, out var mapped, out var resolutionStatus);
                CcdTargetKey? authorizedTarget =
                    resolutionStatus == DisplayAddressResolver.CurrentAddressResolutionStatus.Resolved && mapped != null
                        ? CcdAddress.Target(mapped)
                        : null;

                authorizations.Add(new DisplayApplyAddressAuthorization(setting, resolutionStatus, authorizedTarget));
                if (!mappedSafely)
                {
                    allSafe = false;
                    unsafeResolution?.Invoke(setting, resolutionStatus);
                    continue;
                }

                mappedDisplayConfigs.Add(mapped);
                mappedPairs.Add((setting, mapped));
            }

            var duplicateTargets = authorizations
                .Where(authorization => authorization.Setting.IsEnabled && authorization.AuthorizedTarget.HasValue)
                .GroupBy(authorization => authorization.AuthorizedTarget.Value)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();

            if (duplicateTargets.Count > 0)
            {
                allSafe = false;
                for (int i = 0; i < authorizations.Count; i++)
                {
                    var authorization = authorizations[i];
                    if (!authorization.AuthorizedTarget.HasValue || !duplicateTargets.Contains(authorization.AuthorizedTarget.Value)) continue;

                    authorizations[i] = new DisplayApplyAddressAuthorization(authorization.Setting, DisplayAddressResolver.CurrentAddressResolutionStatus.Ambiguous, null);
                    unsafeResolution?.Invoke(authorization.Setting, DisplayAddressResolver.CurrentAddressResolutionStatus.Ambiguous);
                }
            }

            AssignDesiredSourceGroups(mappedPairs);
            return allSafe;
        }

        public static bool ApplyDpiSettings(
            IEnumerable<DisplayApplyAddressAuthorization> authorizations,
            List<DisplayConfigInfo> liveDisplayConfigs,
            IDisplayApplyAdapter displayAdapter)
        {
            if (displayAdapter == null) throw new ArgumentNullException(nameof(displayAdapter));

            var enabledAuthorizations = (authorizations ?? [])
                .Where(authorization =>
                    authorization.Setting.IsEnabled &&
                    authorization.Status == DisplayAddressResolver.CurrentAddressResolutionStatus.Resolved &&
                    authorization.AuthorizedTarget.HasValue)
                .ToList();
            if (enabledAuthorizations
                .GroupBy(authorization => authorization.AuthorizedTarget.Value)
                .Any(group => group.Count() > 1))
            {
                _logger.Warn("Skipping DPI -> multiple saved display relationships claim one authorized target");
                return false;
            }

            var currentEndpoints = DisplayAddressResolver.CanonicalizeDisplayEndpoints(liveDisplayConfigs ?? []);
            var dpiTargets = new List<(DisplaySetting setting, DisplayConfigInfo live)>();
            bool allDpiChanged = true;

            foreach (var authorization in enabledAuthorizations)
            {
                if (!authorization.Setting.IsEnabled) continue;

                if (authorization.Status != DisplayAddressResolver.CurrentAddressResolutionStatus.Resolved ||
                    !authorization.AuthorizedTarget.HasValue)
                {
                    allDpiChanged = false;
                    continue;
                }

                var live = currentEndpoints.FirstOrDefault(config =>
                    CcdAddress.Target(config).Equals(authorization.AuthorizedTarget.Value));
                if (live == null)
                {
                    _logger.Warn($"Skipping DPI for '{authorization.Setting.ReadableDeviceName}' -> authorized display endpoint is no longer active");
                    allDpiChanged = false;
                    continue;
                }

                dpiTargets.Add((authorization.Setting, live));
            }

            foreach (var (setting, live) in dpiTargets)
            {
                if (!displayAdapter.ApplyDpi(setting, live))
                {
                    _logger.Warn($"Failed to set DPI scaling for {live.DeviceName}");
                    allDpiChanged = false;
                }
            }

            return allDpiChanged;
        }
    }
}
