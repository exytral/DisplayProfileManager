using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    internal sealed class ProfileApplyRuntime
    {
        public Func<List<DisplayConfigHelper.DisplayConfigInfo>> GetDisplayConfigs { get; }
        public Func<List<DisplayConfigHelper.DisplayConfigInfo>> GetAllPathDisplayAddresses { get; }
        public Func<List<DisplayConfigHelper.DisplayConfigInfo>, bool> ApplyDisplayTopology { get; }
        public Func<List<DisplayConfigHelper.DisplayConfigInfo>, Task<DisplayConfigHelper.DisplayConfigApplyResult>> ApplyDisplayConfigDetailed { get; }
        public Func<string, uint, DisplayConfigHelper.DisplayConfigInfo, bool> SetDpiScaling { get; }
        public Func<bool> ShouldAbortOnApplyFailure { get; }
        public Func<bool> ShouldRollbackAfterApplyFailure { get; }

        public ProfileApplyRuntime(
            Func<List<DisplayConfigHelper.DisplayConfigInfo>> getDisplayConfigs,
            Func<List<DisplayConfigHelper.DisplayConfigInfo>> getAllPathDisplayAddresses,
            Func<List<DisplayConfigHelper.DisplayConfigInfo>, bool> applyDisplayTopology,
            Func<List<DisplayConfigHelper.DisplayConfigInfo>, Task<DisplayConfigHelper.DisplayConfigApplyResult>> applyDisplayConfigDetailed,
            Func<string, uint, DisplayConfigHelper.DisplayConfigInfo, bool> setDpiScaling,
            Func<bool> shouldAbortOnApplyFailure,
            Func<bool> shouldRollbackAfterApplyFailure)
        {
            GetDisplayConfigs = getDisplayConfigs ?? throw new ArgumentNullException(nameof(getDisplayConfigs));
            GetAllPathDisplayAddresses = getAllPathDisplayAddresses ?? throw new ArgumentNullException(nameof(getAllPathDisplayAddresses));
            ApplyDisplayTopology = applyDisplayTopology ?? throw new ArgumentNullException(nameof(applyDisplayTopology));
            ApplyDisplayConfigDetailed = applyDisplayConfigDetailed ?? throw new ArgumentNullException(nameof(applyDisplayConfigDetailed));
            SetDpiScaling = setDpiScaling ?? throw new ArgumentNullException(nameof(setDpiScaling));
            ShouldAbortOnApplyFailure = shouldAbortOnApplyFailure ?? throw new ArgumentNullException(nameof(shouldAbortOnApplyFailure));
            ShouldRollbackAfterApplyFailure = shouldRollbackAfterApplyFailure ?? throw new ArgumentNullException(nameof(shouldRollbackAfterApplyFailure));
        }

        public static ProfileApplyRuntime CreateDefault(SettingsManager settingsManager)
        {
            if (settingsManager == null) throw new ArgumentNullException(nameof(settingsManager));

            return new ProfileApplyRuntime(
                DisplayConfigHelper.GetDisplayConfigs,
                DisplayConfigHelper.GetAllPathDisplayAddresses,
                DisplayConfigHelper.ApplyDisplayTopology,
                DisplayConfigHelper.ApplyDisplayConfigDetailed,
                DpiHelper.SetDPIScaling,
                settingsManager.ShouldAbortOnApplyFailure,
                settingsManager.ShouldRollbackAfterApplyFailure);
        }
    }

    internal sealed class DisplayApplyAddressAuthorization
    {
        public DisplaySetting Setting { get; }
        public DisplayConfigHelper.CurrentAddressResolutionStatus Status { get; }
        public CcdTargetKey? AuthorizedTarget { get; }

        public DisplayApplyAddressAuthorization(
            DisplaySetting setting,
            DisplayConfigHelper.CurrentAddressResolutionStatus status,
            CcdTargetKey? authorizedTarget)
        {
            Setting = setting ?? throw new ArgumentNullException(nameof(setting));
            Status = status;
            AuthorizedTarget = authorizedTarget;
        }
    }

    internal static class DisplayApplyAuthorization
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        public static bool TryMapDisplaySettingsForApply(
            IEnumerable<DisplaySetting> settings,
            List<DisplayConfigHelper.DisplayConfigInfo> activeDisplayConfigs,
            List<DisplayConfigHelper.DisplayConfigInfo> addressDisplayConfigs,
            out List<DisplayConfigHelper.DisplayConfigInfo> mappedDisplayConfigs,
            out List<DisplayApplyAddressAuthorization> authorizations,
            Action<DisplaySetting, DisplayConfigHelper.CurrentAddressResolutionStatus> unsafeResolution = null)
        {
            mappedDisplayConfigs = new List<DisplayConfigHelper.DisplayConfigInfo>();
            authorizations = new List<DisplayApplyAddressAuthorization>();
            var pendingSettings = (settings ?? Enumerable.Empty<DisplaySetting>()).ToList();

            if (pendingSettings.Count > 0 && (addressDisplayConfigs == null || addressDisplayConfigs.Count == 0))
            {
                foreach (var setting in pendingSettings)
                {
                    authorizations.Add(new DisplayApplyAddressAuthorization(
                        setting,
                        DisplayConfigHelper.CurrentAddressResolutionStatus.InsufficientEvidence,
                        null));
                    unsafeResolution?.Invoke(setting, DisplayConfigHelper.CurrentAddressResolutionStatus.InsufficientEvidence);
                }

                return false;
            }

            bool allSafe = true;
            var mappedPairs = new List<(DisplaySetting Setting, DisplayConfigHelper.DisplayConfigInfo Mapped)>();
            foreach (var setting in pendingSettings)
            {
                bool mappedSafely = ProfileManager.TryMapDisplaySettingForApply(
                    setting,
                    activeDisplayConfigs,
                    addressDisplayConfigs,
                    out var mapped,
                    out var resolutionStatus);
                CcdTargetKey? authorizedTarget = resolutionStatus == DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved && mapped != null
                    ? CcdAddress.Target(mapped)
                    : (CcdTargetKey?)null;

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

            ProfileManager.AssignDesiredSourceGroups(mappedPairs);
            return allSafe;
        }

        public static bool ApplyDpiSettings(
            IEnumerable<DisplayApplyAddressAuthorization> authorizations,
            List<DisplayConfigHelper.DisplayConfigInfo> liveDisplayConfigs,
            Func<string, uint, DisplayConfigHelper.DisplayConfigInfo, bool> setDpiScaling)
        {
            if (setDpiScaling == null) throw new ArgumentNullException(nameof(setDpiScaling));

            var currentEndpoints = DisplayConfigHelper.CanonicalizeDisplayEndpoints(liveDisplayConfigs ?? new List<DisplayConfigHelper.DisplayConfigInfo>());
            var dpiTargets = new List<(DisplaySetting setting, DisplayConfigHelper.DisplayConfigInfo live)>();
            bool allDpiChanged = true;

            foreach (var authorization in authorizations ?? Enumerable.Empty<DisplayApplyAddressAuthorization>())
            {
                if (!authorization.Setting.IsEnabled) continue;

                if (authorization.Status != DisplayConfigHelper.CurrentAddressResolutionStatus.Resolved ||
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

            foreach (var target in dpiTargets.GroupBy(x => CcdAddress.Target(x.live)).Select(g => g.First()))
            {
                if (!setDpiScaling(target.live.DeviceName, target.setting.DpiScaling, target.live))
                {
                    _logger.Warn($"Failed to set DPI scaling for {target.live.DeviceName}");
                    allDpiChanged = false;
                }
            }

            return allDpiChanged;
        }
    }
}
