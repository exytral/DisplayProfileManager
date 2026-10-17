using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays.Policy
{
    public class DisplayAddressResolver
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public static bool MatchesEdid(DisplaySetting setting, DisplayConfigInfo config)
        {
            if (setting == null || config == null || !setting.HasEdidIdentity)
            {
                return false;
            }

            if (string.IsNullOrEmpty(config.ManufacturerName) || string.IsNullOrEmpty(config.ProductCodeID))
            {
                return false;
            }

            return setting.ManufacturerName.Equals(config.ManufacturerName, StringComparison.OrdinalIgnoreCase) && setting.ProductCodeID.Equals(config.ProductCodeID, StringComparison.OrdinalIgnoreCase);
        }

        public static void ResolveDeviceName(DisplaySetting setting, List<DisplayConfigInfo> displayConfigs = null)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));

            var configs = displayConfigs ?? DisplayConfigService.GetDisplayConfigs();
            var match = ResolveLiveDisplay(setting, configs);

            if (match != null && !string.IsNullOrEmpty(match.DeviceName))
                setting.DeviceName = match.DeviceName;
        }

        internal enum CurrentAddressResolutionStatus
        {
            Resolved,
            Absent,
            Ambiguous,
            Conflicting,
            InsufficientEvidence
        }

        internal static List<DisplayConfigInfo> CanonicalizeDisplayEndpoints(IEnumerable<DisplayConfigInfo> displayConfigs)
        {
            var endpoints = new Dictionary<CcdTargetKey, DisplayConfigInfo>();
            if (displayConfigs == null)
            {
                return [.. endpoints.Values];
            }

            foreach (var display in displayConfigs)
            {
                if (display == null) continue;

                var key = CcdAddress.Target(display);
                if (!endpoints.TryGetValue(key, out var existing) || (!existing.IsEnabled && display.IsEnabled))
                    endpoints[key] = display;
            }

            return [.. endpoints.Values];
        }

        internal static DisplayConfigInfo ResolveCurrentApplyAddress(DisplaySetting setting, List<DisplayConfigInfo> currentEndpoints) => ResolveCurrentApplyAddressDetailed(setting, currentEndpoints, out _);

        internal static DisplayConfigInfo ResolveCurrentApplyAddressDetailed(DisplaySetting setting, List<DisplayConfigInfo> currentEndpoints, out CurrentAddressResolutionStatus status)
        {
            status = CurrentAddressResolutionStatus.Absent;
            if (setting == null || currentEndpoints == null || currentEndpoints.Count == 0)
            {
                return null;
            }

            var endpoints = CanonicalizeDisplayEndpoints(currentEndpoints);
            uint maskedTargetId = setting.TargetId & 0xFFFF;
            var targetMatches = endpoints.Where(c => (c.TargetId & 0xFFFF) == maskedTargetId).ToList();
            var savedPort = targetMatches.Count == 1 ? targetMatches[0] : null;

            if (savedPort != null && (!setting.HasEdidIdentity || MatchesEdid(setting, savedPort)))
            {
                status = CurrentAddressResolutionStatus.Resolved;
                return savedPort;
            }

            var edidMatches = setting.HasEdidIdentity
                ? [.. endpoints.Where(c => MatchesEdid(setting, c))]
                : new List<DisplayConfigInfo>();

            if (edidMatches.Count == 1)
            {
                var byEdid = edidMatches[0];
                if (savedPort == null)
                    _logger.Warn($"'{setting.ReadableDeviceName}' moved from saved target {maskedTargetId} to {CcdAddress.Target(byEdid)} -> following unique EDID match");
                else
                    _logger.Warn($"Saved target {maskedTargetId} now holds {savedPort.ManufacturerName}{savedPort.ProductCodeID}; '{setting.ReadableDeviceName}' is on {CcdAddress.Target(byEdid)} -> following unique EDID match");

                status = CurrentAddressResolutionStatus.Resolved;
                return byEdid;
            }

            if (savedPort != null)
            {
                _logger.Warn($"Saved target {maskedTargetId} does not uniquely match captured EDID {setting.ManufacturerName}{setting.ProductCodeID}; retaining the unambiguous current port as a location-role fallback");
                status = CurrentAddressResolutionStatus.Resolved;
                return savedPort;
            }

            if (targetMatches.Count > 1 || edidMatches.Count > 1)
            {
                _logger.Warn($"Current display evidence for '{setting.ReadableDeviceName}' is ambiguous across adapter-qualified endpoints -> refusing address selection");
                status = CurrentAddressResolutionStatus.Ambiguous;
                return null;
            }

            status = CurrentAddressResolutionStatus.Absent;
            return null;
        }

        public static DisplayConfigInfo ResolveLiveDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs) => ResolveCurrentApplyAddressDetailed(setting, liveConfigs, out _);
    }
}
