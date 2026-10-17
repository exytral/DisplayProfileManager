using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Audio;
using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.Wallpapers;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Profiles
{
    internal enum ProfileApplyFailureStage
    {
        Topology = 1,
        DisplayConfiguration = 2
    }

    internal sealed class ProfileApplyService(
        ProfileApplyQueue queue,
        IDisplayApplyAdapter displayApply,
        IWallpaperService wallpaperService,
        IAudioDeviceService audioService,
        IScriptRunner scriptRunner,
        SettingsService settingsService,
        Func<string, Profile> getProfile,
        Func<List<Profile>> getAllProfiles,
        Func<Profile, Task<bool>> saveProfileAsync,
        Action<string> setCurrentProfileId,
        Func<string> getCurrentProfileId,
        IDisplayEventEvidenceScopeProvider displayEventEvidence = null,
        ProfileApplyFailureStage? forceApplyFailureStage = null)
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly ProfileApplyQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        private readonly IDisplayApplyAdapter _displayApply = displayApply ?? throw new ArgumentNullException(nameof(displayApply));
        private readonly IDisplayEventEvidenceScopeProvider _displayEventEvidence = displayEventEvidence;
        private readonly IWallpaperService _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        private readonly IAudioDeviceService _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        private readonly IScriptRunner _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
        private readonly SettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        private readonly Func<string, Profile> _getProfile = getProfile ?? throw new ArgumentNullException(nameof(getProfile));
        private readonly Func<List<Profile>> _getAllProfiles = getAllProfiles ?? throw new ArgumentNullException(nameof(getAllProfiles));
        private readonly Func<Profile, Task<bool>> _saveProfileAsync = saveProfileAsync ?? throw new ArgumentNullException(nameof(saveProfileAsync));
        private readonly Action<string> _setCurrentProfileId = setCurrentProfileId ?? throw new ArgumentNullException(nameof(setCurrentProfileId));
        private readonly Func<string> _getCurrentProfileId = getCurrentProfileId ?? throw new ArgumentNullException(nameof(getCurrentProfileId));
        private readonly ProfileApplyFailureStage? _forceApplyFailureStage = forceApplyFailureStage;
        private bool _rollingBack;

        public event EventHandler<ProfileAppliedEventArgs> ProfileApplied;

        public Task<ProfileApplyResult> ApplyProfileAsync(Profile profile, ApplySource source = ApplySource.Unknown) => _queue.EnqueueAsync(() => ApplyProfileCoreAsync(profile, source));

        internal static Task<bool> ApplyWallpaperOffUiAsync(
            WallpaperSettings settings,
            Func<WallpaperSettings, bool> apply = null) =>
            Task.Run(() => (apply ?? WallpaperApply.Apply)(settings));

        private async Task<ProfileApplyResult> ApplyProfileCoreAsync(Profile profile, ApplySource source)
        {
            IAsyncDisposable evidenceScope = null;
            try
            {
                if (!_rollingBack && _displayEventEvidence != null)
                    evidenceScope = await _displayEventEvidence.BeginApplyEvidenceScopeAsync().ConfigureAwait(false);

                var totalWatch = Stopwatch.StartNew();
                var previousProfileId = _getCurrentProfileId();
                _logger.Info($"Applying profile '{profile.Name}' [source={source}]...");

                // Capture Pre-Apply Snapshot
                List<DisplayConfigInfo> preApplySnapshot = null;
                if (!_rollingBack && _settingsService.ShouldRollbackAfterApplyFailure())
                    preApplySnapshot = _displayApply.GetActiveDisplays();

                // Map Display Configurations
                ProfileApplyResult result = new() { AudioSuccess = true, DpiChanged = true };
                var mapWatch = Stopwatch.StartNew();
                var displayConfigs = new List<DisplayConfigInfo>();
                var displayAddressAuthorizations = new List<DisplayApplyAddressAuthorization>();
                bool displayAddressResolutionSafe = true;
                if (profile.DisplaySettings.Count > 0)
                {
                    var activeDisplayConfigs = _displayApply.GetActiveDisplays();
                    var addressDisplayConfigs = _displayApply.GetDisplayAddresses();
                    displayAddressResolutionSafe = DisplayApplyPolicy.TryMapDisplaySettingsForApply(
                        profile.DisplaySettings,
                        activeDisplayConfigs,
                        addressDisplayConfigs,
                        out displayConfigs,
                        out displayAddressAuthorizations,
                        (setting, resolutionStatus) => _logger.Error(
                            $"Cannot safely resolve current display address for '{setting.ReadableDeviceName}' ({resolutionStatus}); refusing topology mutation."));
                }
                mapWatch.Stop();

                // Apply Display Topology
                var topologyWatch = Stopwatch.StartNew();
                bool topologyApplied = displayAddressResolutionSafe && _displayApply.ApplyTopology(displayConfigs);
                if (!displayAddressResolutionSafe)
                    _logger.Warn($"Topology apply blocked for '{profile.Name}' because one or more display addresses were unsafe to resolve.");
                if (ShouldForceApplyFailureAt(ProfileApplyFailureStage.Topology))
                    topologyApplied = false;
                if (!topologyApplied)
                    _logger.Warn($"Topology apply failed for '{profile.Name}'");
                topologyWatch.Stop();

                // Apply Display Configuration
                var configWatch = Stopwatch.StartNew();
                if (topologyApplied)
                {
                    var displayResult = await _displayApply.ApplyDisplayConfigDetailed(displayConfigs);
                    result.DisplayConfigApplied = displayResult.Success;
                    result.AdvancedColorSuccess = displayResult.AdvancedColorSuccess;
                    result.ColorProfileSuccess = displayResult.ColorProfileSuccess;
                }
                configWatch.Stop();

                if (topologyApplied && ShouldForceApplyFailureAt(ProfileApplyFailureStage.DisplayConfiguration))
                    result.DisplayConfigApplied = false;

                // Handle Display-Stage Failure
                if (!result.DisplayConfigApplied && !_rollingBack && _settingsService.ShouldAbortOnApplyFailure())
                {
                    _logger.Warn($"Aborting apply of '{profile.Name}' — display configuration failed");
                    result.Success = false;

                    if (_settingsService.ShouldRollbackAfterApplyFailure())
                        await RollbackFailedApplyAsync(previousProfileId, preApplySnapshot, profile.Name);

                    return result;
                }

                // Apply DPI Settings
                var dpiWatch = Stopwatch.StartNew();
                var dpiLiveConfigs = _displayApply.GetActiveDisplays();
                result.DpiChanged = displayAddressResolutionSafe &&
                    DisplayApplyPolicy.ApplyDpiSettings(displayAddressAuthorizations, dpiLiveConfigs, _displayApply);
                dpiWatch.Stop();

                // Apply Wallpaper Settings
                var wallpaperWatch = Stopwatch.StartNew();
                if (profile.WallpaperSettings?.Enabled == true)
                {
                    try
                    {
                        result.WallpaperSuccess = await ApplyWallpaperOffUiAsync(profile.WallpaperSettings, _wallpaperService.Apply);
                    }
                    catch (Exception ex)
                    {
                        result.WallpaperSuccess = false;
                        _logger.Warn(ex, "Wallpaper apply failed");
                    }
                }
                wallpaperWatch.Stop();

                // Apply Audio Settings
                var audioWatch = Stopwatch.StartNew();
                if (profile.AudioSettings?.Enabled == true)
                    result.AudioSuccess = _audioService.ApplyAudioSettings(profile.AudioSettings);
                audioWatch.Stop();

                // Finalize Result
                var finalizeWatch = new Stopwatch();
                var scriptWatch = new Stopwatch();

                result.Success = result.DisplayConfigApplied;

                // Execute Scripts
                scriptWatch.Start();
                if (profile.ScriptSettings?.Enabled == true && profile.ScriptSettings.Scripts?.Any() == true)
                {
                    var enabledScripts = profile.ScriptSettings.Scripts.Count(s => s.IsEnabled);
                    var disabledNote = enabledScripts == profile.ScriptSettings.Scripts.Count
                        ? ""
                        : $" ({profile.ScriptSettings.Scripts.Count - enabledScripts} disabled)";

                    _logger.Info($"Executing {TextFormat.Plural(enabledScripts, "script")}{disabledNote}...");
                    foreach (var command in profile.ScriptSettings.Scripts)
                        _scriptRunner.ExecuteScript(command);
                }
                else if (profile.ScriptSettings?.Enabled != true && profile.ScriptSettings?.Scripts?.Any() == true)
                    _logger.Debug("Scripts disabled, skipping execution");
                scriptWatch.Stop();

                if (result.Success)
                {
                    // Log Result and Persist Success
                    var cloneGroupCount = profile.DisplaySettings
                        .Where(s => s.IsPartOfCloneGroup())
                        .GroupBy(s => s.CloneGroupId)
                        .Count();

                    var activeCount = profile.DisplaySettings.Count(d => d.IsEnabled);
                    var sb = new StringBuilder();
                    sb.Append($"Applied profile '{profile.Name}' [source={source}] -> ({TextFormat.Plural(activeCount, "active display")})");
                    if (cloneGroupCount > 0)
                        sb.Append($" | ({TextFormat.Plural(cloneGroupCount, "clone group")})");

                    _logger.Info(sb.ToString());

                    finalizeWatch.Start();
                    _setCurrentProfileId(profile.Id);
                    result.CurrentProfilePersisted = await _settingsService.SetCurrentProfileIdAsync(profile.Id);
                    if (!result.CurrentProfilePersisted)
                        _logger.Warn($"Profile '{profile.Name}' applied, but the current-profile marker could not be persisted");
                    finalizeWatch.Stop();

                    // Self-Heal Display Metadata
                    var postApplyLiveConfigs = _displayApply.GetActiveDisplays();
                    var allProfiles = _getAllProfiles();
                    var profilesToPersist = ProfileHardwareReconciler.BackfillHardwareInfoAcrossProfiles(profile, allProfiles, postApplyLiveConfigs);
                    foreach (var capabilityProfile in ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(allProfiles, postApplyLiveConfigs))
                    {
                        if (!profilesToPersist.Any(candidate => candidate.Id == capabilityProfile.Id))
                            profilesToPersist.Add(capabilityProfile);
                    }

                    foreach (var changedProfile in profilesToPersist)
                        await _saveProfileAsync(changedProfile);
                }

                totalWatch.Stop();

                if (result.Success)
                {
                    string warningSummary = ProfileApplyFormatter.GetApplyWarningSummary(result);
                    if (!string.IsNullOrEmpty(warningSummary))
                        _logger.Warn($"Profile '{profile.Name}' applied with warnings: {warningSummary}");

                    ProfileApplied?.Invoke(this, new ProfileAppliedEventArgs(profile, source, totalWatch.ElapsedMilliseconds, warningSummary));
                }

                // Timing Summary
                _logger.Info($"[PERF] Map: {mapWatch.ElapsedMilliseconds} ms | Topology: {topologyWatch.ElapsedMilliseconds} ms | Config: {configWatch.ElapsedMilliseconds} ms");
                _logger.Info($"[PERF] DPI: {dpiWatch.ElapsedMilliseconds} ms | Wallpaper: {wallpaperWatch.ElapsedMilliseconds} ms | Audio: {audioWatch.ElapsedMilliseconds} ms | Scripts: {scriptWatch.ElapsedMilliseconds} ms");
                _logger.Info($"[PERF] Finalize: {finalizeWatch.ElapsedMilliseconds} ms | TOTAL: {totalWatch.ElapsedMilliseconds} ms");

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying profile");
                return new ProfileApplyResult { Success = false };
            }
            finally
            {
                if (evidenceScope != null)
                    await evidenceScope.DisposeAsync().ConfigureAwait(false);
            }
        }

        private bool ShouldForceApplyFailureAt(ProfileApplyFailureStage stage)
        {
            if (_rollingBack || _forceApplyFailureStage != stage)
            {
                return false;
            }

            _logger.Warn($"event=EngineeringForcedApplyFailure stage={stage}");
            return true;
        }

        private async Task RollbackFailedApplyAsync(string previousProfileId, List<DisplayConfigInfo> preApplySnapshot, string failedProfileName)
        {
            if (_rollingBack)
            {
                _logger.Error("Rollback skipped while rollback already in progress");
                return;
            }

            var hasPreviousProfile = !string.IsNullOrEmpty(previousProfileId) && _getProfile(previousProfileId) != null;
            var rollbackTarget = ProfileRollbackPolicy.SelectRollbackTarget(_settingsService.ShouldRollbackAfterApplyFailure(), _settingsService.ShouldRollbackToPreviousProfile(), hasPreviousProfile);
            var previous = rollbackTarget == ProfileRollbackPolicy.RollbackTarget.PreviousProfile ? _getProfile(previousProfileId) : null;

            try
            {
                _rollingBack = true;

                if (previous != null)
                {
                    _logger.Info($"Rolling back to '{previous.Name}' after '{failedProfileName}' apply failed...");

                    var rollbackResult = await ApplyProfileCoreAsync(previous, ApplySource.Unknown);
                    if (!rollbackResult.Success)
                        _logger.Error($"Rollback to '{previous.Name}' failed; leaving current desktop state as is");

                    return;
                }

                _logger.Info($"Previous profile unavailable after '{failedProfileName}' failed; rolling back to pre-apply display snapshot");
                await RollbackToSnapshotAsync(preApplySnapshot, failedProfileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Rollback after '{failedProfileName}' threw; leaving current desktop state as is");
            }
            finally
            {
                _rollingBack = false;
            }
        }

        private async Task RollbackToSnapshotAsync(List<DisplayConfigInfo> snapshot, string failedProfileName)
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                _logger.Warn($"Cannot roll back after '{failedProfileName}' apply failed — no pre-apply snapshot was captured");
                return;
            }

            _logger.Info($"Rolling back display state of ({TextFormat.Plural(snapshot.Count, "display")}) after '{failedProfileName}' apply failed");

            if (!_displayApply.ApplyTopology(snapshot))
            {
                _logger.Error("Rollback failed at topology -> desktop may be in mixed state");
                return;
            }

            if (!(await _displayApply.ApplyDisplayConfigDetailed(snapshot)).Success)
            {
                _logger.Error("Rollback failed at layout -> desktop may be in mixed state");
                return;
            }

            _setCurrentProfileId(null);
            await _settingsService.SetCurrentProfileIdAsync(string.Empty);
            _logger.Info("Display state rolled back -> no profile is marked active");
        }
    }
}
