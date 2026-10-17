using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Audio;
using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Events;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Interop;
using DisplayProfileManager.Integration;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Profiles.Persistence;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Persistence;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.UI.Themes;
using DisplayProfileManager.UI.Tray;
using DisplayProfileManager.UI.Windows;
using DisplayProfileManager.Wallpapers;

namespace DisplayProfileManager
{
    public partial class App : System.Windows.Application
    {
        internal const string AppUserModelId = "exytral.DisplayProfileManager";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private TrayIcon _trayIcon;
        private MainWindow _mainWindow;
        private QuickSwitchOverlayHost _quickSwitchOverlayHost;
        private string _pendingQuickSwitchWarning;
        private Task<UpdateCheckResult> _updateCheckTask;
        private UpdateCheckResult _latestUpdateCheckResult;

        private SettingsService _settingsService;
        private SettingsCoordinator _settingsCoordinator;
        private ProfileService _profileService;
        private ProfileApplyService _profileApplyService;
        private ProfileEditorService _profileEditorService;
        private ScriptLibrary _scriptLibrary;
        private IScriptRunner _scriptRunner;
        private IWallpaperService _wallpaperService;
        private IAudioDeviceService _audioService;
        private IDisplayApplyAdapter _displayApplyAdapter;
        private DisplayEditorService _displayEditorService;
        private DisplayEventMonitor _displayEventMonitor;
        private IThemeService _themeService;
        private IUpdateChecker _updateChecker;
        private IShellExtensionRegistrar _shellExtensionRegistrar;
        private StartMenuProfileShortcutManager _profileShortcutManager;
        private HotkeyCoordinator _hotkeyCoordinator;
        private SingleInstanceCoordinator _singleInstanceCoordinator;
        private ApplicationCommandRouter _commandRouter;
        private TrayMenuController _trayMenuController;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            int appUserModelIdResult = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            if (appUserModelIdResult < 0)
                _logger.Warn($"SetCurrentProcessExplicitAppUserModelID failed with HRESULT 0x{appUserModelIdResult:X8}");

            GenericFocusVisualPolicy.Install();
            ComposeApplication();
            _commandRouter.ProfileApplyFailed += OnCommandProfileApplyFailed;

            _logger.Info($"Display Profile Manager Starting | Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

            var cli = CliParser.Parse(e.Args);

            if (cli.ShellAction != ShellAction.None)
            {
                var shellCoordinator = new ShellCommandCoordinator(_settingsService, _shellExtensionRegistrar);
                int exitCode = await shellCoordinator.ExecuteAsync(cli.ShellAction, message => _logger.Warn(message));

                bool registeringShell = cli.ShellAction == ShellAction.Register || cli.ShellAction == ShellAction.RegisterDeveloper;
                string shellFlag = cli.ShellAction == ShellAction.RegisterDeveloper ? "--shell-dev" : "--shell";

                if (exitCode == 1)
                    _logger.Error(registeringShell ? $"{shellFlag} operation failed. Exiting." : "--unshell operation failed. Exiting.");
                else if (registeringShell)
                    _logger.Info(exitCode == 0
                        ? $"{shellFlag}: shell extension registered. Exiting."
                        : $"{shellFlag}: shell extension was already registered. Exiting.");
                else
                    _logger.Info(exitCode == 0 ? "--unshell: shell extension removed and Explorer restarted. Exiting." : "--unshell: shell extension was not registered. Exiting.");

                Shutdown(exitCode);
                return;
            }

            bool startInTray = cli.StartInTray, devMode = cli.DevMode;
            bool isRefresh = cli.IsRefresh, isTheme = cli.IsTheme, isProfile = cli.IsProfile, isHeadless = cli.IsHeadless, isExit = cli.IsExit;
            string profile = cli.Profile, theme = cli.Theme;
            ApplySource profileSource = cli.ProfileSource;
            var commandQueue = cli.CommandQueue;
            _singleInstanceCoordinator.ConfigureAuthority(devMode);

            if (isProfile || isHeadless)
            {
                string executablePath = Environment.ProcessPath ?? "<unknown>";
                string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "<unknown>";
                string commandKind = isHeadless ? "HeadlessProfile" : "Profile";
                _logger.Info(
                    $"External invocation | " +
                    $"pid={Environment.ProcessId} | " +
                    $"exe='{executablePath}' | " +
                    $"version={version} | " +
                    $"DevMode={devMode} | " +
                    $"IsHeadless={isHeadless} | " +
                    $"authority={(devMode ? "Dev" : "Normal")} | " +
                    $"source={profileSource} | " +
                    $"command={commandKind} | " +
                    $"profile='{profile ?? "<current>"}'");
            }

            bool isAuthoritative = true;
            if (!devMode)
                isAuthoritative = CheckSingleInstance();

            if (isExit)
            {
                if (devMode)
                {
                    bool devSent = await _singleInstanceCoordinator.SendAsync("CMD:EXIT", IpcAuthorityKind.Dev);
                    _logger.Info(devSent ? "--dev --exit command sent. Exiting." : "--dev --exit: no active dev instance found. Exiting.");
                    Shutdown(devSent ? 0 : 2);
                    return;
                }

                if (!isAuthoritative)
                {
                    bool normalSent = await _singleInstanceCoordinator.SendAsync("CMD:EXIT", IpcAuthorityKind.Normal, SingleInstanceCoordinator.IpcAuthorityReadyTimeoutMs);
                    if (normalSent)
                    {
                        _logger.Info("--exit command sent to normal instance. Exiting.");
                        Shutdown(0);
                    }
                    else
                    {
                        _logger.Error("Normal instance owns the single-instance mutex, but its IPC pipe did not become ready. Exit command not redirected to dev authority.");
                        Shutdown(1);
                    }

                    return;
                }

                bool sent = await _singleInstanceCoordinator.SendAsync("CMD:EXIT", IpcAuthorityKind.Normal);
                if (!sent)
                    sent = await _singleInstanceCoordinator.SendAsync("CMD:EXIT", IpcAuthorityKind.Dev);

                if (sent)
                {
                    _logger.Info("--exit command sent. Exiting.");
                    Shutdown(0);
                }
                else
                {
                    _logger.Info("--exit: no active normal or dev instance found. Exiting.");
                    Shutdown(2);
                }

                return;
            }

            if (!devMode && !isAuthoritative)
            {
                if (commandQueue.Count > 0)
                {
                    bool allSent = true;
                    foreach (var cmd in commandQueue)
                    {
                        if (!await _singleInstanceCoordinator.SendAsync(cmd, IpcAuthorityKind.Normal, SingleInstanceCoordinator.IpcAuthorityReadyTimeoutMs))
                        {
                            allSent = false;
                            break;
                        }
                    }

                    if (allSent)
                    {
                        _logger.Info("All commands passed to active instance. Exiting.");
                        Shutdown();
                        return;
                    }

                    _logger.Error("Active instance owns the single-instance mutex, but its IPC pipe did not become ready. Command not executed locally.");
                    Shutdown(1);
                    return;
                }

                _singleInstanceCoordinator.BringExistingInstanceToFront();
                Shutdown();
                return;
            }
            if (devMode && commandQueue.Count > 0)
            {
                bool sentAny = false;
                bool allSent = true;
                foreach (var cmd in commandQueue)
                {
                    if (!await _singleInstanceCoordinator.SendAsync(cmd, IpcAuthorityKind.Dev))
                    {
                        if (!sentAny)
                        {
                            string commandKind = cmd.StartsWith("PROFILE", StringComparison.Ordinal) ? "Profile" : "Command";
                            _logger.Warn($"Dev IPC first send failed; falling through to local execution. command={commandKind}");
                        }

                        allSent = false;
                        break;
                    }

                    sentAny = true;
                }

                if (allSent)
                {
                    _logger.Info("All commands passed to active dev instance. Exiting.");
                    Shutdown();
                    return;
                }

                if (sentAny)
                {
                    _logger.Error("Dev IPC authority stopped accepting commands after partial dispatch. Remaining commands will not execute locally.");
                    Shutdown(1);
                    return;
                }
            }
            if (!devMode && commandQueue.Count > 0)
            {
                if (isRefresh || (isTheme && string.IsNullOrEmpty(theme)))
                {
                    _logger.Info("Target maintenance command requires active instance. Exiting.");
                    Shutdown();
                    return;
                }

                // Persistent theme update if no instance is found
                if (isTheme && !string.IsNullOrEmpty(theme))
                {
                    bool saved = false;

                    if (!await _settingsService.LoadSettingsAsync())
                        _logger.Warn("--theme: settings failed to load -> not saved");
                    else if (!_themeService.ThemeExists(theme))
                        _logger.Error($"--theme: '{theme}' is not available theme -> not saved");
                    else
                    {
                        await _settingsService.UpdateSettingAsync("Theme", theme);
                        saved = true;
                    }

                    if (!isProfile)
                    {
                        if (saved)
                            _logger.Info($"Theme '{theme}' saved. Exiting.");
                        else
                            _logger.Info("Exiting without theme change.");

                        Shutdown();
                        return;
                    }
                }
            }

            if (_displayEventMonitor != null)
            {
                try
                {
                    await _displayEventMonitor.StartAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Display event instrumentation failed to start");
                }
            }

            if ((isProfile || isHeadless) && string.IsNullOrEmpty(profile))
            {
                await _settingsService.LoadSettingsAsync();
                profile = _settingsService.GetCurrentProfileId();
            }

            if (!string.IsNullOrEmpty(profile))
            {
                ProfileApplyResult result = await ApplyProfileFromExternalInvocationAsync(profile, profileSource);
                if (isHeadless)
                {
                    HeadlessApplyOutcome outcome = HeadlessApplyPolicy.Evaluate(result);
                    if (outcome == HeadlessApplyOutcome.CurrentProfilePersistenceFailed)
                    {
                        _logger.Warn($"Headless apply succeeded for profile '{profile}', but current-profile marker persistence failed. Exiting with failure.");
                        Shutdown(1);
                        return;
                    }

                    _logger.Info(outcome == HeadlessApplyOutcome.Complete
                        ? "Headless apply complete. Exiting."
                        : $"Headless apply failed for profile '{profile}'. Exiting.");
                    Shutdown(outcome == HeadlessApplyOutcome.Complete ? 0 : 1);
                    return;
                }
                else if (!result.Success)
                    _logger.Warn($"CLI apply failed for profile '{profile}'. Continuing to main window.");
            }

            try
            {
                await InitializeApplicationAsync();

                EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel));

                SetupTrayIcon();

                if (string.IsNullOrEmpty(profile))
                    await HandleStartupProfileAsync();
                _ = CheckForUpdatesAndNotifyAsync();
                if (!startInTray)
                    ShowMainWindow();
                if (_settingsService.IsFirstRun())
                    await _settingsService.CompleteFirstRunAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Application initialization failed");
                Shutdown();
            }
        }

        private void ComposeApplication()
        {
            string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayProfileManager");

            var engineeringOverrides = EngineeringOverrides.Resolve(Environment.GetEnvironmentVariable, warning => _logger.Warn(warning));
            if (engineeringOverrides.AnyActive)
            {
                _logger.Warn(
                    $"event=EngineeringOverrideActive" +
                    $"{(engineeringOverrides.ForceApplyFailureStage.HasValue ? $" forceApplyFailure={engineeringOverrides.ForceApplyFailureStage.Value}" : string.Empty)}" +
                    $"{(engineeringOverrides.ForceTopologyRecovery ? " forceTopologyRecovery=true" : string.Empty)}");
            }

            _settingsService = new SettingsService(new SettingsStore(appData));
            _shellExtensionRegistrar = new ShellExtensionRegistrar();
            _themeService = new ThemeService(_settingsService);
            _updateChecker = new GitHubUpdateChecker(_settingsService);

            var displayEventOptions = DisplayEventEngineeringOptions.Resolve(Environment.GetEnvironmentVariable, warning => _logger.Warn(warning));

            _scriptLibrary = new ScriptLibrary(appData);
            _scriptRunner = new ScriptRunner(_scriptLibrary);
            _wallpaperService = new WallpaperService();
            _audioService = new AudioDeviceService();
            _displayApplyAdapter = new DisplayApplyAdapter(() => engineeringOverrides.ForceTopologyRecovery, displayEventOptions.TraceEnabled);
            _displayEditorService = new DisplayEditorService();
            if (displayEventOptions.EvidenceMode != DisplayEventEvidenceMode.Disabled)
            {
                _logger.Info($"Display event evidence mode: {displayEventOptions.EvidenceMode}");
                _displayEventMonitor = new DisplayEventMonitor(
                    DisplayConfigService.CaptureDisplayEventSnapshot,
                    displayEventOptions.EvidenceMode,
                    displayEventOptions.TraceEnabled);
            }

            var profileStore = new ProfileStore(appData);
            var profileMigration = new ProfileMigrationService(_settingsService);
            var profileDraftFactory = new ProfileDraftFactory();
            var profileCaptureService = new ProfileCaptureService();
            _profileService = new ProfileService(
                profileStore,
                profileMigration,
                profileDraftFactory,
                profileCaptureService,
                _settingsService,
                DisplayConfigService.GetDisplayConfigs,
                DisplayConfigService.GetAllPathDisplayAddresses);

            _profileApplyService = new ProfileApplyService(
                new ProfileApplyQueue(),
                _displayApplyAdapter,
                _wallpaperService,
                _audioService,
                _scriptRunner,
                _settingsService,
                _profileService.GetProfile,
                _profileService.GetAllProfiles,
                _profileService.PersistHardwareSelfHealMetadataAsync,
                _profileService.SetCurrentProfileId,
                () => _profileService.CurrentProfileId,
                _displayEventMonitor,
                engineeringOverrides.ForceApplyFailureStage);
            _profileService.AttachApplyService(_profileApplyService);

            _profileShortcutManager = new StartMenuProfileShortcutManager(
                _profileService,
                _settingsService,
                new StartMenuProfileShortcutStore(),
                () => Environment.ProcessPath ?? throw new InvalidOperationException("Current executable path is unavailable."));
            _settingsCoordinator = new SettingsCoordinator(
                _settingsService,
                new AutoStartService(),
                _shellExtensionRegistrar,
                _profileShortcutManager);

            _profileEditorService = new ProfileEditorService(
                _profileService,
                _displayEditorService,
                _scriptLibrary,
                _audioService,
                _wallpaperService);
            _trayMenuController = new TrayMenuController(_profileService, _profileApplyService);
            _singleInstanceCoordinator = new SingleInstanceCoordinator();
            _commandRouter = new ApplicationCommandRouter(_profileService, _profileApplyService, _settingsService, _themeService);
        }

        private bool CheckSingleInstance() => _singleInstanceCoordinator.AcquireNormalAuthority(() => Dispatcher.InvokeAsync(ShowMainWindow).Task);

        private async Task InitializeApplicationAsync()
        {
            await _settingsService.LoadSettingsAsync();
            await _profileService.LoadProfilesAsync();

            _themeService.InitializeTheme();
            InitializeGlobalHotkeys();

            // Later authoritative profile reloads must reconcile RegisterHotKey state before UI consumers observe them
            _profileService.ProfileApplied += OnProfileApplied;

            // Expose IPC only after command handlers can observe fully initialized authoritative state
            StartIPCPipeListener();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new TrayIcon(_trayMenuController);
            _trayIcon.ShowMainWindow += OnShowMainWindow;
            _trayIcon.ShowSettingsWindow += OnShowSettingsWindow;
            _trayIcon.ExitApplication += OnExitApplication;

            if (!string.IsNullOrEmpty(_pendingQuickSwitchWarning))
            {
                _trayIcon.ShowNotification("Quick Switch", _pendingQuickSwitchWarning, TrayNotificationIcon.Info);
                _pendingQuickSwitchWarning = null;
            }
        }

        private async Task HandleStartupProfileAsync()
        {
            try
            {
                if (_settingsService.ShouldApplyStartupProfile())
                {
                    var startupProfileId = _settingsService.GetStartupProfileId();
                    var startupProfile = _profileService.GetProfile(startupProfileId);

                    if (startupProfile != null)
                    {
                        var applyResult = await _profileService.ApplyProfileAsync(startupProfile, ApplySource.Startup);

                        if (!applyResult.Success)
                        {
                            string errorDetails = ProfileApplyFormatter.GetApplyResultErrorMessage(startupProfile.Name, applyResult);
                            _logger.Warn(errorDetails);
                            _trayIcon?.ShowNotification("Startup profile", errorDetails, TrayNotificationIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying startup profile");
            }
        }

        private void StartIPCPipeListener()
        {
            _singleInstanceCoordinator.StartIpcListener(async received =>
            {
                await Dispatcher.InvokeAsync(async () => await HandleIpcMessageAsync(received)).Task.Unwrap();
            });
        }

        private async Task HandleIpcMessageAsync(string receivedValue)
        {
            await _commandRouter.HandleIpcMessageAsync(receivedValue, Shutdown);
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow(
                    _profileService,
                    _settingsService,
                    _scriptLibrary,
                    _themeService,
                    _audioService,
                    _profileEditorService,
                    _settingsCoordinator,
                    () => _hotkeyCoordinator?.SuspendForHotkeyCapture());
                _mainWindow.Closed += OnMainWindowClosed;
            }

            _mainWindow.Show();

            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            if (_latestUpdateCheckResult != null)
                _mainWindow.ShowUpdateAvailableNotice(_latestUpdateCheckResult);

            _mainWindow.Topmost = true;
            _mainWindow.Activate();
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }

        internal void ShowMainWindowFromQuickSwitch() => ShowMainWindow();

        public async Task<UpdateCheckResult> CheckForUpdatesAndNotifyAsync(bool notify = true, bool force = false)
        {
            if (!_settingsService.ShouldCheckForUpdates())
            {
                return null;
            }

            if (force || _updateCheckTask == null)
                _updateCheckTask = _updateChecker.CheckAsync();

            var result = await _updateCheckTask;
            _latestUpdateCheckResult = result;

            if (result == null || !result.UpdateAvailable)
            {
                return result;
            }

            _mainWindow?.ShowUpdateAvailableNotice(result);

            if (notify)
            {
                _trayIcon?.ShowUpdateNotification(
                    "Update available",
                    $"Display Profile Manager {result.LatestVersion} is available.",
                    result.ReleaseUrl);
            }

            return result;
        }

        private void OnShowMainWindow(object sender, EventArgs e) => ShowMainWindow();

        private void OnShowSettingsWindow(object sender, EventArgs e)
        {
            ShowMainWindow();

            _mainWindow?.OpenSettingsWindow();
        }

        private void InitializeGlobalHotkeys()
        {
            try
            {
                _quickSwitchOverlayHost ??= new QuickSwitchOverlayHost(() =>
                    new QuickSwitchOverlayWindow(
                        _profileService,
                        _profileApplyService,
                        _settingsService,
                        _themeService,
                        OnHotkeyApplyCompleted));

                _hotkeyCoordinator = new HotkeyCoordinator(
                    new GlobalHotkeyRegistrar(),
                    _profileService,
                    _profileApplyService,
                    _settingsService,
                    ShowQuickSwitchOverlay,
                    ShowQuickSwitchWarning,
                    OnHotkeyApplyCompleted);
                _hotkeyCoordinator.Start();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing global hotkeys");
            }
        }

        public void RegisterAllProfileHotkeys() => _hotkeyCoordinator?.RegisterAllProfileHotkeys();

        private async Task<ProfileApplyResult> ApplyProfileFromExternalInvocationAsync(string profileNameOrId, ApplySource source)
        {
            await _settingsService.LoadSettingsAsync();
            await _profileService.LoadProfilesAsync();
            return await _commandRouter.ApplyProfileAsync(profileNameOrId, source);
        }

        private void OnCommandProfileApplyFailed(Profile profile, ProfileApplyResult result)
        {
            _trayIcon?.ShowNotification(
                "Apply failed",
                ProfileApplyFormatter.GetApplyResultErrorMessage(profile.Name, result),
                TrayNotificationIcon.Error);
        }

        private void OnHotkeyApplyCompleted(Profile profile, ProfileApplyResult result)
        {
            if (result.Success) return;
            OnCommandProfileApplyFailed(profile, result);
        }

        private void ShowQuickSwitchOverlay() => Dispatcher.Invoke(() => _quickSwitchOverlayHost.OpenOrActivate());

        private void ShowQuickSwitchWarning(string message)
        {
            _logger.Warn(message);
            if (_trayIcon != null)
                _trayIcon.ShowNotification("Quick Switch", message, TrayNotificationIcon.Info);
            else
                _pendingQuickSwitchWarning = message;
        }

        private void OnProfileApplied(object sender, ProfileAppliedEventArgs e)
        {
            if (!_settingsService.ShouldShowNotifications()) return;

            string source = ProfileApplyFormatter.GetApplySourceLabel(e.Source);

            double seconds = Math.Ceiling(e.DurationMilliseconds / 100.0) / 10.0;
            string elapsed = $"{seconds:0.#} {(seconds == 1 ? "second" : "seconds")}";

            string message = ProfileApplyFormatter.AppendApplyWarnings($"{source} in {elapsed}", e.WarningSummary);
            var icon = string.IsNullOrEmpty(e.WarningSummary) ? TrayNotificationIcon.None : TrayNotificationIcon.Info;
            _trayIcon?.ShowNotification(e.Profile.Name, message, icon);
        }

        private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift) return;

            if (sender is not ScrollViewer scrollViewer) return;

            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        private void OnExitApplication(object sender, EventArgs e) => Shutdown();

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                try
                {
                    _displayEventMonitor?.Dispose();
                    _quickSwitchOverlayHost?.CloseCurrent();
                    _trayIcon?.Dispose();

                    _profileService?.ProfileApplied -= OnProfileApplied;

                    _profileShortcutManager?.Dispose();
                    _commandRouter.ProfileApplyFailed -= OnCommandProfileApplyFailed;
                    _themeService.Cleanup();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during application exit");
                }

                try
                {
                    _hotkeyCoordinator?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Hotkey teardown incomplete during application exit");
                }

                _logger.Info("Display Profile Manager Exited");
                base.OnExit(e);
            }
            finally
            {
                _singleInstanceCoordinator?.Dispose();
            }
        }
    }
}
