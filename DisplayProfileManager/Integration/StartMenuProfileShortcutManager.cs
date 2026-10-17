using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Integration
{
    internal interface IProfileShortcutManager
    {
        Task<bool> SynchronizeAsync();
        Task<bool> RemoveAllAsync();
    }

    internal sealed class StartMenuProfileShortcutManager : IProfileShortcutManager, IDisposable
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly ProfileService _profiles;
        private readonly SettingsService _settings;
        private readonly IProfileShortcutStore _store;
        private readonly Func<string> _executablePath;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _disposed;

        public StartMenuProfileShortcutManager(
            ProfileService profiles,
            SettingsService settings,
            IProfileShortcutStore store,
            Func<string> executablePath)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));

            _profiles.ProfileAdded += OnProfileChanged;
            _profiles.ProfileUpdated += OnProfileChanged;
            _profiles.ProfileDeleted += OnProfileDeleted;
            _profiles.ProfilesLoaded += OnProfilesLoaded;
        }

        public Task<bool> SynchronizeAsync() => ReconcileAsync(_profiles.GetAllProfiles());

        public Task<bool> RemoveAllAsync() => ReconcileAsync([]);

        public void Dispose()
        {
            if (_disposed) return;

            _profiles.ProfileAdded -= OnProfileChanged;
            _profiles.ProfileUpdated -= OnProfileChanged;
            _profiles.ProfileDeleted -= OnProfileDeleted;
            _profiles.ProfilesLoaded -= OnProfilesLoaded;
            _syncLock.Dispose();
            _disposed = true;
        }

        private async Task<bool> ReconcileAsync(IEnumerable<Profile> profiles)
        {
            await _syncLock.WaitAsync();
            try
            {
                string executablePath = _executablePath();
                var desired = ProfileShortcutPlanner.BuildDesiredShortcuts(profiles, executablePath);
                var current = _store.ReadAll();
                var operations = ProfileShortcutPlanner.Plan(current, desired);
                bool success = true;

                foreach (var operation in operations)
                {
                    try
                    {
                        if (operation.Kind == ProfileShortcutOperationKind.Delete)
                            _store.Delete(operation.FileName);
                        else
                            _store.Write(operation.Shortcut);
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        _logger.Error(ex, $"Failed to {operation.Kind.ToString().ToLowerInvariant()} managed profile shortcut '{operation.FileName}'");
                    }
                }

                try
                {
                    _store.DeleteFolderIfEmpty();
                }
                catch (Exception ex)
                {
                    success = false;
                    _logger.Error(ex, "Failed to remove empty managed profile shortcut folder");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to synchronize managed Start Menu profile shortcuts");
                return false;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private void ReconcileFromProfileLifecycle()
        {
            if (_disposed || !_settings.ShouldManageStartMenuProfileShortcuts()) return;

            _ = SynchronizeAsync();
        }

        private void OnProfileChanged(object sender, Profile profile) => ReconcileFromProfileLifecycle();
        private void OnProfileDeleted(object sender, string profileId) => ReconcileFromProfileLifecycle();
        private void OnProfilesLoaded(object sender, EventArgs e) => ReconcileFromProfileLifecycle();
    }
}
