using System;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Integration;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.Application
{
    internal sealed class SettingsCoordinator(
        SettingsService settings,
        IAutoStartService autoStart,
        IShellExtensionRegistrar shell,
        IProfileShortcutManager profileShortcuts)
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly SettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        private readonly IAutoStartService _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        private readonly IShellExtensionRegistrar _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        private readonly IProfileShortcutManager _profileShortcuts = profileShortcuts ?? throw new ArgumentNullException(nameof(profileShortcuts));

        public bool IsAutoStartEnabled() => _autoStart.IsAutoStartEnabled();

        public static bool IsRunningAsAdmin() => AutoStartService.IsRunningAsAdmin();

        public async Task<AutoStartOperationResult> SetStartWithWindowsAsync(bool enabled)
        {
            var result = enabled
                ? _autoStart.EnableAutoStart(_settings.Settings.AutoStartMode, _settings.Settings.StartInSystemTray)
                : _autoStart.DisableAutoStart();
            if (result != AutoStartOperationResult.Success)
            {
                return result;
            }

            if (await _settings.SetStartWithWindowsStateOnlyAsync(enabled))
            {
                return AutoStartOperationResult.Success;
            }

            var rollback = enabled
                ? _autoStart.DisableAutoStart()
                : _autoStart.EnableAutoStart(_settings.Settings.AutoStartMode, _settings.Settings.StartInSystemTray);
            if (rollback != AutoStartOperationResult.Success)
                _logger.Error("Failed to restore external auto-start state after settings save failure");
            return AutoStartOperationResult.Failed;
        }

        public async Task<AutoStartOperationResult> SetStartInSystemTrayAsync(bool enabled)
        {
            if (enabled && !_settings.Settings.StartWithWindows)
            {
                return AutoStartOperationResult.Failed;
            }

            if (!_settings.Settings.StartWithWindows)
            {
                return await _settings.SetStartInSystemTrayStateOnlyAsync(enabled)
                    ? AutoStartOperationResult.Success
                    : AutoStartOperationResult.Failed;
            }

            bool previous = _settings.Settings.StartInSystemTray;
            var result = _autoStart.EnableAutoStart(_settings.Settings.AutoStartMode, enabled);
            if (result != AutoStartOperationResult.Success)
            {
                return result;
            }

            if (await _settings.SetStartInSystemTrayStateOnlyAsync(enabled))
            {
                return AutoStartOperationResult.Success;
            }

            _autoStart.EnableAutoStart(_settings.Settings.AutoStartMode, previous);
            return AutoStartOperationResult.Failed;
        }

        public async Task<AutoStartOperationResult> SetAutoStartModeAsync(AutoStartMode mode)
        {
            var previous = _settings.Settings.AutoStartMode;
            if (!_settings.Settings.StartWithWindows)
            {
                return await _settings.SetAutoStartModeStateOnlyAsync(mode)
                    ? AutoStartOperationResult.Success
                    : AutoStartOperationResult.Failed;
            }

            if (previous == mode)
            {
                return AutoStartOperationResult.Success;
            }

            var disabled = _autoStart.DisableAutoStart();
            if (disabled != AutoStartOperationResult.Success)
            {
                return disabled;
            }

            var enabled = _autoStart.EnableAutoStart(mode, _settings.Settings.StartInSystemTray);
            if (enabled != AutoStartOperationResult.Success)
            {
                _autoStart.EnableAutoStart(previous, _settings.Settings.StartInSystemTray);
                return enabled;
            }

            if (await _settings.SetAutoStartModeStateOnlyAsync(mode))
            {
                return AutoStartOperationResult.Success;
            }

            _autoStart.EnableAutoStart(previous, _settings.Settings.StartInSystemTray);
            return AutoStartOperationResult.Failed;
        }

        public async Task<bool> SetDesktopContextMenuAsync(bool enabled)
        {
            bool wasRegistered = _shell.IsRegistered();
            ShellRegistrationMode previousMode = wasRegistered
                ? _shell.GetRegistrationMode()
                : ShellRegistrationMode.Normal;
            bool changed = enabled ? _shell.Register() : _shell.Unregister();
            if (!changed)
            {
                return false;
            }

            if (!await _settings.SetDesktopContextMenuAsync(enabled))
            {
                if (enabled && !wasRegistered)
                    _shell.Unregister();
                else if (!enabled && wasRegistered)
                    _shell.Register(previousMode);
                return false;
            }

            if (!enabled && wasRegistered)
            {
                return _shell.RestartExplorer();
            }

            return true;
        }

        public async Task<bool> SetManageStartMenuProfileShortcutsAsync(bool enabled)
        {
            bool previous = _settings.Settings.ManageStartMenuProfileShortcuts;
            bool externalChanged = enabled
                ? await _profileShortcuts.SynchronizeAsync()
                : await _profileShortcuts.RemoveAllAsync();

            if (!externalChanged)
            {
                await RestoreProfileShortcutsAsync(previous);
                return false;
            }

            if (await _settings.SetManageStartMenuProfileShortcutsAsync(enabled))
            {
                return true;
            }

            await RestoreProfileShortcutsAsync(previous);
            return false;
        }

        public async Task ReconcileAsync()
        {
            bool externalAutoStart = _autoStart.IsAutoStartEnabled();
            if (externalAutoStart != _settings.Settings.StartWithWindows)
                await _settings.SetStartWithWindowsStateOnlyAsync(externalAutoStart);

            bool externalShell = _shell.IsRegistered();
            if (externalShell != _settings.Settings.DesktopContextMenuEnabled)
                await _settings.SetDesktopContextMenuAsync(externalShell);

            if (_settings.ShouldManageStartMenuProfileShortcuts())
                await _profileShortcuts.SynchronizeAsync();
            else
                await _profileShortcuts.RemoveAllAsync();
        }

        private async Task RestoreProfileShortcutsAsync(bool enabled)
        {
            bool restored = enabled
                ? await _profileShortcuts.SynchronizeAsync()
                : await _profileShortcuts.RemoveAllAsync();

            if (!restored)
                _logger.Error("Failed to restore managed Start Menu profile shortcuts after settings operation failure");
        }
    }
}
