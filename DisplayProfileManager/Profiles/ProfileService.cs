using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Profiles.Policy;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Profiles
{
    internal sealed class ProfileService
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly IProfileStore _store;
        private readonly ProfileMigrationService _migrationService;
        private readonly ProfileDraftFactory _draftFactory;
        private readonly ProfileCaptureService _captureService;
        private readonly SettingsService _settingsService;
        private readonly Func<List<DisplayConfigInfo>> _getMigrationDisplayConfigs;
        private readonly Func<List<DisplayConfigInfo>> _getMigrationDisplayAddresses;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _profilePersistenceLocks =
            new(StringComparer.OrdinalIgnoreCase);

        private List<Profile> _profiles = [];
        private string _currentProfileId;
        private ProfileApplyService _applyService;

        public event EventHandler<Profile> ProfileAdded;
        public event EventHandler<Profile> ProfileUpdated;
        public event EventHandler<string> ProfileDeleted;
        public event EventHandler<ProfileAppliedEventArgs> ProfileApplied;
        public event EventHandler ProfilesLoaded;

        public string CurrentProfileId => _currentProfileId;

        public ProfileService(
            IProfileStore store,
            ProfileMigrationService migrationService,
            ProfileDraftFactory draftFactory,
            ProfileCaptureService captureService,
            SettingsService settingsService,
            Func<List<DisplayConfigInfo>> getMigrationDisplayConfigs,
            Func<List<DisplayConfigInfo>> getMigrationDisplayAddresses)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
            _draftFactory = draftFactory ?? throw new ArgumentNullException(nameof(draftFactory));
            _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _getMigrationDisplayConfigs = getMigrationDisplayConfigs ?? throw new ArgumentNullException(nameof(getMigrationDisplayConfigs));
            _getMigrationDisplayAddresses = getMigrationDisplayAddresses ?? _getMigrationDisplayConfigs;
        }

        public void AttachApplyService(ProfileApplyService applyService)
        {
            _applyService?.ProfileApplied -= OnProfileApplied;

            _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
            _applyService.ProfileApplied += OnProfileApplied;
        }

        private void OnProfileApplied(object sender, ProfileAppliedEventArgs e) =>
            ProfileApplied?.Invoke(this, e);

        internal void SetCurrentProfileId(string profileId) => _currentProfileId = profileId;

        private SemaphoreSlim GetProfilePersistenceLock(string profileId) =>
            _profilePersistenceLocks.GetOrAdd(profileId ?? string.Empty, _ => new SemaphoreSlim(1, 1));

        private async Task<bool> WriteProfileAsync(Profile profile)
        {
            try
            {
                if (!ProfileSerializer.CanPersistProfileSchema(profile))
                {
                    string schema = profile == null ? "<null>" : profile.SchemaVersion.ToString();
                    _logger.Warn($"Refusing to save profile schema {schema}; expected {ProfileSerializer.CurrentSchemaVersion}");
                    return false;
                }

                return await _store.WriteProfileTextAsync(profile.Id, ProfileSerializer.SerializeProfile(profile));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving profile");
                return false;
            }
        }

        public async Task<bool> SaveProfileAsync(Profile profile)
        {
            if (profile == null)
            {
                return false;
            }

            var persistenceLock = GetProfilePersistenceLock(profile.Id);
            await persistenceLock.WaitAsync();
            try
            {
                return await WriteProfileAsync(profile);
            }
            finally
            {
                persistenceLock.Release();
            }
        }

        internal async Task<bool> PersistHardwareSelfHealMetadataAsync(Profile metadataProfile)
        {
            if (metadataProfile == null)
            {
                return false;
            }

            var persistenceLock = GetProfilePersistenceLock(metadataProfile.Id);
            await persistenceLock.WaitAsync();
            try
            {
                var canonical = GetProfile(metadataProfile.Id);
                if (canonical == null)
                {
                    return false;
                }

                if (!ReferenceEquals(canonical, metadataProfile) &&
                    !ProfileHardwareReconciler.MergeHardwareSelfHealMetadata(canonical, metadataProfile))
                {
                    _logger.Warn($"Refusing hardware metadata persistence for '{canonical.Name}' -> display relationships are not one-to-one");
                    return false;
                }

                return await WriteProfileAsync(canonical);
            }
            finally
            {
                persistenceLock.Release();
            }
        }

        public async Task<bool> LoadProfilesAsync()
        {
            var previousProfiles = _profiles;

            try
            {
                var loadedProfiles = new List<Profile>();
                var acceptedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var profileFiles = _store.EnumerateProfileFiles();
                List<DisplayConfigInfo> liveConfigs = null;
                List<DisplayConfigInfo> addressConfigs = null;

                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = await _store.ReadProfileTextAsync(file);
                        var profile = ProfileSerializer.DeserializeProfile(json);

                        if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.DisplaySettings == null)
                        {
                            _logger.Warn($"Skipping profile load -> invalid file: {Path.GetFileName(file)}");
                            continue;
                        }

                        if (!ProfileStore.TryValidateManagedProfileIdentity(file, profile, acceptedProfileIds, out string identityFailure))
                        {
                            _logger.Warn($"Skipping profile load -> managed file '{file}' cannot be modified: {identityFailure}");
                            continue;
                        }

                        if (ProfileSerializer.NeedsProfileMigration(profile))
                        {
                            liveConfigs ??= _getMigrationDisplayConfigs();
                            addressConfigs ??= _getMigrationDisplayAddresses();

                            bool migrated = await _migrationService.MigrateProfileAsync(profile, liveConfigs, addressConfigs, json);
                            if (migrated)
                            {
                                var savedDate = profile.LastModifiedDate;
                                await SaveProfileAsync(profile);
                                profile.LastModifiedDate = savedDate;
                                _logger.Info($"Migrated profile '{profile.Name}' to schema {ProfileSerializer.CurrentSchemaVersion}");
                            }
                        }

                        loadedProfiles.Add(profile);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error loading profile from {file}");
                    }
                }

                if (HasTotalProfileLoadFailure(profileFiles.Length, loadedProfiles.Count))
                {
                    _logger.Error($"Failed to load any of {profileFiles.Length} existing profile files -> keeping the previous in-memory profiles");
                    return false;
                }

                _profiles = loadedProfiles;
                if (_profiles.Count == 0 && await CreateDefaultProfileAsync() == null)
                {
                    _profiles = previousProfiles;
                    return false;
                }

                try
                {
                    liveConfigs ??= _getMigrationDisplayConfigs();
                    await HealAdvancedColorCapabilitiesAsync(liveConfigs);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Skipping Advanced Color capability self-heal during profile load");
                }

                _currentProfileId = _settingsService.GetCurrentProfileId();
                ProfilesLoaded?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading profiles");
                _profiles = previousProfiles;
                return false;
            }
        }

        internal static bool HasTotalProfileLoadFailure(int discoveredProfileFileCount, int loadedProfileCount) =>
            discoveredProfileFileCount > 0 && loadedProfileCount == 0;

        public Profile DuplicateProfile(string profileId) =>
            _draftFactory.DuplicateProfile(GetProfile(profileId), _profiles);

        public async Task<Profile> DuplicateProfileAsync(string profileId)
        {
            var duplicatedProfile = DuplicateProfile(profileId);
            if (duplicatedProfile == null)
            {
                return null;
            }
            return await AddProfileAsync(duplicatedProfile) ? duplicatedProfile : null;
        }

        public Task<Profile> CreateDefaultProfileAsync() =>
            _draftFactory.CreateDefaultProfileAsync(
                _captureService,
                AddProfileAsync,
                _settingsService,
                SetCurrentProfileId);

        public Task<List<DisplaySetting>> GetCurrentDisplaySettingsAsync() =>
            _captureService.GetCurrentDisplaySettingsAsync();

        public Task<ProfileApplyResult> ApplyProfileAsync(Profile profile, ApplySource source = ApplySource.Unknown)
        {
            if (_applyService == null) throw new InvalidOperationException("Profile apply service has not been composed.");
            return _applyService.ApplyProfileAsync(profile, source);
        }

        internal async Task<List<Profile>> HealAdvancedColorCapabilitiesAsync(
            List<DisplayConfigInfo> liveConfigs,
            Func<Profile, Task<bool>> saveProfile = null)
        {
            var changedProfiles = ProfileHardwareReconciler.HealAdvancedColorCapabilitiesAcrossProfiles(_profiles, liveConfigs);
            var persist = saveProfile ?? PersistHardwareSelfHealMetadataAsync;

            foreach (var profile in changedProfiles)
            {
                if (!await persist(profile))
                    _logger.Warn($"Advanced Color capability evidence for '{profile.Name}' could not be persisted");
            }

            return changedProfiles;
        }

        internal void ObserveAdvancedColorCapabilities(List<DisplayConfigInfo> liveConfigs) =>
            _ = HealAdvancedColorCapabilitiesAsync(liveConfigs);

        public Profile GetProfile(string profileId) => _profiles.FirstOrDefault(p => p.Id == profileId);

        public Profile GetProfileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            string cleanName = name.Trim();
            return _profiles.FirstOrDefault(p => p.Name.Trim().Equals(cleanName, StringComparison.OrdinalIgnoreCase));
        }

        internal Profile ResolveProfileNameOrId(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId))
            {
                return null;
            }
            return GetProfile(nameOrId) ?? GetProfileByName(nameOrId);
        }

        public Profile GetCurrentProfile() =>
            string.IsNullOrEmpty(_currentProfileId) ? null : GetProfile(_currentProfileId);

        public List<Profile> GetAllProfiles() => [.. _profiles];

        public Profile GetDefaultProfile()
        {
            var id = _settingsService.GetDefaultProfileId();
            return string.IsNullOrEmpty(id) ? null : _profiles.FirstOrDefault(p => p.Id == id);
        }

        public void AddProfile(Profile profile)
        {
            _profiles.Add(profile);
            ProfileAdded?.Invoke(this, profile);
        }

        public Task<bool> AddProfileAsync(Profile profile) => AddProfileAsync(profile, SaveProfileAsync);

        internal async Task<bool> AddProfileAsync(Profile profile, Func<Profile, Task<bool>> saveProfile)
        {
            if (!await saveProfile(profile))
            {
                return false;
            }
            AddProfile(profile);
            return true;
        }

        public void UpdateProfile(Profile profile)
        {
            var existingProfile = GetProfile(profile.Id);
            if (existingProfile == null) return;

            if (!ProfileHardwareReconciler.MergeCanonicalHardwareSelfHealMetadata(profile, existingProfile))
            {
                _logger.Warn($"Refusing profile update for '{existingProfile.Name}' -> display relationships are not one-to-one");
                return;
            }

            var index = _profiles.IndexOf(existingProfile);
            profile.UpdateLastModified();
            _profiles[index] = profile;
            ProfileUpdated?.Invoke(this, profile);
        }

        public Task<bool> UpdateProfileAsync(Profile profile) =>
            UpdateProfileAsync(profile, null, WriteProfileAsync);

        internal Task<bool> UpdateProfileAsync(Profile profile, IReadOnlyList<DisplaySetting> displayBaseline) =>
            UpdateProfileAsync(profile, displayBaseline, WriteProfileAsync);

        internal Task<bool> UpdateProfileAsync(Profile profile, Func<Profile, Task<bool>> saveProfile) =>
            UpdateProfileAsync(profile, null, saveProfile);

        private async Task<bool> UpdateProfileAsync(
            Profile profile,
            IReadOnlyList<DisplaySetting> displayBaseline,
            Func<Profile, Task<bool>> saveProfile)
        {
            if (profile == null)
            {
                return false;
            }

            var persistenceLock = GetProfilePersistenceLock(profile.Id);
            await persistenceLock.WaitAsync();
            try
            {
                var existingProfile = GetProfile(profile.Id);
                if (existingProfile == null)
                {
                    return false;
                }

                if (displayBaseline != null &&
                    !TryReconcileEditorDisplayDomain(profile, existingProfile, displayBaseline))
                {
                    _logger.Warn($"Refusing profile update for '{existingProfile.Name}' -> display domain changed concurrently");
                    return false;
                }

                if (!ProfileHardwareReconciler.MergeCanonicalHardwareSelfHealMetadata(profile, existingProfile))
                {
                    _logger.Warn($"Refusing profile update for '{existingProfile.Name}' -> display relationships are not one-to-one");
                    return false;
                }

                profile.UpdateLastModified();
                if (!await saveProfile(profile))
                {
                    return false;
                }

                var index = _profiles.IndexOf(existingProfile);
                _profiles[index] = profile;
                ProfileUpdated?.Invoke(this, profile);
                return true;
            }
            finally
            {
                persistenceLock.Release();
            }
        }

        private static bool TryReconcileEditorDisplayDomain(
            Profile draft,
            Profile canonical,
            IReadOnlyList<DisplaySetting> baseline)
        {
            bool editorChanged = !ProfileHardwareReconciler.DisplayDomainEquals(baseline, draft.DisplaySettings);
            bool canonicalChanged = !ProfileHardwareReconciler.DisplayDomainEquals(baseline, canonical.DisplaySettings);

            if (editorChanged && canonicalChanged &&
                !ProfileHardwareReconciler.DisplayDomainEquals(draft.DisplaySettings, canonical.DisplaySettings))
            {
                return false;
            }

            if (!editorChanged && canonicalChanged)
                draft.DisplaySettings = ProfileDraftFactory.CreateWorkingCopy(canonical).DisplaySettings;

            return true;
        }

        public void DeleteProfile(string profileId)
        {
            _profiles.RemoveAll(p => p.Id == profileId);
            ProfileDeleted?.Invoke(this, profileId);
        }

        public Task<bool> DeleteProfileAsync(string profileId) =>
            DeleteProfileAsync(profileId, _store.DeleteProfileAsync);

        internal async Task<bool> DeleteProfileAsync(string profileId, Func<string, Task<bool>> deleteProfileFile)
        {
            try
            {
                if (!await deleteProfileFile(profileId))
                {
                    return false;
                }
                DeleteProfile(profileId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error deleting profile");
                return false;
            }
        }

        public Task<bool> SetDefaultProfileAsync(string profileId) =>
            _settingsService.SetDefaultProfileIdAsync(profileId);

        public bool HasProfile(string name) => ProfileNamingPolicy.HasProfile(_profiles, name);
        public string GetDuplicateProfileName(string baseName) => ProfileNamingPolicy.GetDuplicateProfileName(_profiles, baseName);
        public string GetUniqueProfileName(string baseName) => ProfileNamingPolicy.GetUniqueProfileName(_profiles, baseName);
        public int GetProfileCount() => _profiles.Count;
        public string GetAppDataFolder() => _store.AppDataFolder;

        public List<Profile> GetProfilesWithHotkeys() =>
            [.. _profiles.Where(p => p.HotkeyConfig != null && p.HotkeyConfig.Key != System.Windows.Input.Key.None)];

        public List<Profile> GetProfilesWithActiveHotkeys() =>
            [.. _profiles.Where(p => p.HotkeyConfig != null && p.HotkeyConfig.IsEnabled && p.HotkeyConfig.Key != System.Windows.Input.Key.None)];

        public Dictionary<string, HotkeyConfig> GetAllHotkeys()
        {
            var hotkeys = new Dictionary<string, HotkeyConfig>();
            foreach (var profile in GetProfilesWithActiveHotkeys())
                hotkeys[profile.Id] = profile.HotkeyConfig;
            return hotkeys;
        }
    }
}
