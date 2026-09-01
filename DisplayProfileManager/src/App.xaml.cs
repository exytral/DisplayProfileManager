using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI;
using DisplayProfileManager.UI.Windows;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DisplayProfileManager
{
    public partial class App : Application
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private TrayIcon _trayIcon;
        private MainWindow _mainWindow;
        private Task<UpdateCheckResult> _updateCheckTask;
        private UpdateCheckResult _latestUpdateCheckResult;

        private ProfileManager _profileManager;
        private SettingsManager _settingsManager;
        private GlobalHotkeyHelper _globalHotkeyHelper;

        private Mutex _instanceMutex;
        private EventWaitHandle _showWindowEvent;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _ownsInstanceMutex;

        private bool _hotkeysDisabledForEditing = false;
        private int _profileEditWindowCount = 0;

        private const string MutexName = "DPM_Mutex";
        private const string ShowWindowEventName = "DPM_ShowWindow";

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _settingsManager = SettingsManager.Instance;
            _profileManager = ProfileManager.Instance;

            logger.Info($"Display Profile Manager Starting | Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

            var cli = CliParser.Parse(e.Args);

            if (cli.ShellAction == ShellAction.Unregister)
            {
                bool wasRegistered = ShellContextMenuHelper.IsRegistered();
                bool ok = ShellContextMenuHelper.Unregister();

                if (!ok)
                {
                    logger.Error("--unshell: shell extension removal failed. Exiting.");
                    Shutdown(1);
                    return;
                }

                bool loaded = await SettingsManager.Instance.LoadSettingsAsync();
                if (loaded)
                    await SettingsManager.Instance.SetDesktopContextMenuAsync(false);
                else
                    logger.Warn("--unshell: settings failed to load, skipping DesktopContextMenuEnabled save");

                if (wasRegistered)
                {
                    if (!ShellContextMenuHelper.RestartExplorer())
                    {
                        logger.Error("--unshell: shell extension was removed, but Explorer restart failed. Exiting.");
                        Shutdown(1);
                        return;
                    }

                    logger.Info("--unshell: shell extension removed and Explorer restarted. Exiting.");
                    Shutdown(0);
                }
                else
                {
                    logger.Info("--unshell: shell extension was not registered. Exiting.");
                    Shutdown(2);
                }

                return;
            }

            if (cli.ShellAction == ShellAction.Register)
            {
                bool wasRegistered = ShellContextMenuHelper.IsRegistered();
                bool ok = ShellContextMenuHelper.Register();

                if (!ok)
                {
                    logger.Error("--shell: shell extension registration failed. Exiting.");
                    Shutdown(1);
                    return;
                }

                bool loaded = await SettingsManager.Instance.LoadSettingsAsync();
                if (loaded)
                    await SettingsManager.Instance.SetDesktopContextMenuAsync(true);
                else
                    logger.Warn("--shell: settings failed to load, skipping DesktopContextMenuEnabled save");

                if (wasRegistered)
                {
                    logger.Info("--shell: shell extension was already registered. Exiting.");
                    Shutdown(2);
                }
                else
                {
                    logger.Info("--shell: shell extension registered. Exiting.");
                    Shutdown(0);
                }

                return;
            }

            bool startInTray = cli.StartInTray, devMode = cli.DevMode;
            bool isRefresh = cli.IsRefresh, isTheme = cli.IsTheme, isProfile = cli.IsProfile, isHeadless = cli.IsHeadless, isExit = cli.IsExit;
            string profile = cli.Profile, theme = cli.Theme;
            var commandQueue = cli.CommandQueue;

            if (isExit)
            {
                bool sent = await IpcServer.SendAsync("CMD:EXIT");
                if (!sent)
                {
                    logger.Info("--exit: no active instance found. Exiting.");
                    Shutdown(2);
                }
                else
                {
                    logger.Info("--exit command sent. Exiting.");
                    Shutdown(0);
                }

                return;
            }

            if (!devMode && commandQueue.Count > 0)
            {
                bool allSent = true;
                foreach (var cmd in commandQueue)
                {
                    if (!await IpcServer.SendAsync(cmd))
                    {
                        allSent = false;
                        break;
                    }
                }

                if (allSent)
                {
                    logger.Info("All commands passed to active instance. Exiting.");
                    Shutdown();
                    return;
                }

                if (isRefresh || (isTheme && string.IsNullOrEmpty(theme)))
                {
                    logger.Info("Target maintenance command requires active instance. Exiting.");
                    Shutdown();
                    return;
                }

                // Persistent theme update if no instance is found
                if (isTheme && !string.IsNullOrEmpty(theme))
                {
                    bool saved = false;

                    if (!await _settingsManager.LoadSettingsAsync())
                        logger.Warn("--theme: settings failed to load -> not saved");
                    else if (!ThemeHelper.ThemeExists(theme))
                        logger.Error($"--theme: '{theme}' is not available theme -> not saved");
                    else
                    {
                        await _settingsManager.UpdateSettingAsync("Theme", theme);
                        saved = true;
                    }

                    if (!isProfile)
                    {
                        if (saved)
                            logger.Info($"Theme '{theme}' saved. Exiting.");
                        else
                            logger.Info("Exiting without theme change.");

                        Shutdown();
                        return;
                    }
                }
            }

            if ((isProfile || isHeadless) && string.IsNullOrEmpty(profile))
            {
                await _settingsManager.LoadSettingsAsync();
                if (_settingsManager.Debug.AnySet)
                    logger.Warn($"Debug flags are set: {_settingsManager.Debug}. Behavior is deliberately altered.");
                profile = _settingsManager.GetCurrentProfileId();
            }

            if (!string.IsNullOrEmpty(profile))
            {
                bool result = await ApplyProfileFromCommandLineAsync(profile);
                if (isHeadless)
                {
                    logger.Info(result
                        ? "Headless apply complete. Exiting."
                        : $"Headless apply failed for profile '{profile}'. Exiting.");
                    Shutdown(result ? 0 : 1);
                    return;
                }
                else if (!result)
                    logger.Warn($"CLI apply failed for profile '{profile}'. Continuing to main window.");
            }

            if (devMode)
                _cancellationTokenSource = new CancellationTokenSource();
            else if (!CheckSingleInstance())
            {
                Shutdown();
                return;
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
                if (_settingsManager.IsFirstRun())
                    await _settingsManager.CompleteFirstRunAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Application initialization failed");
                Shutdown();
            }
        }

        private bool CheckSingleInstance()
        {
            bool isNewInstance;
            _instanceMutex = new Mutex(true, MutexName, out isNewInstance);

            if (!isNewInstance)
            {
                WindowActivationHelper.BringExistingInstanceToFront("Display Profile Manager", ShowWindowEventName);
                return false;
            }

            _ownsInstanceMutex = true;

            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShowWindowEventName);
                _cancellationTokenSource = new CancellationTokenSource();
                StartShowWindowListener();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error setting up show window event");
            }

            return true;
        }

        private async Task InitializeApplicationAsync()
        {
            StartIPCPipeListener();

            _settingsManager = SettingsManager.Instance;
            _profileManager = ProfileManager.Instance;

            await _settingsManager.LoadSettingsAsync();
            await _profileManager.LoadProfilesAsync();

            ThemeHelper.InitializeTheme();
            InitializeGlobalHotkeys();

            _profileManager.ProfileAdded += OnProfileChanged;
            _profileManager.ProfileUpdated += OnProfileChanged;
            _profileManager.ProfileDeleted += OnProfileDeleted;
            _profileManager.ProfileApplied += OnProfileApplied;
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new TrayIcon();
            _trayIcon.ShowMainWindow += OnShowMainWindow;
            _trayIcon.ShowSettingsWindow += OnShowSettingsWindow;
            _trayIcon.ExitApplication += OnExitApplication;
        }

        private async Task HandleStartupProfileAsync()
        {
            try
            {
                if (_settingsManager.ShouldApplyStartupProfile())
                {
                    var startupProfileId = _settingsManager.GetStartupProfileId();
                    var startupProfile = _profileManager.GetProfile(startupProfileId);

                    if (startupProfile != null)
                    {
                        var applyResult = await _profileManager.ApplyProfileAsync(startupProfile, ProfileManager.ApplySource.Startup);

                        if (!applyResult.Success)
                        {
                            string errorDetails = _profileManager.GetApplyResultErrorMessage(startupProfile.Name, applyResult);
                            logger.Warn(errorDetails);
                            _trayIcon?.ShowNotification("Startup profile", errorDetails, TrayNotificationIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying startup profile");
            }
        }

        private void StartIPCPipeListener()
        {
            IpcServer.StartListening(_cancellationTokenSource.Token, async received =>
            {
                await Dispatcher.InvokeAsync(async () => await HandleIpcMessageAsync(received)).Task.Unwrap();
            });
        }

        private async Task HandleIpcMessageAsync(string receivedValue)
        {
            if (receivedValue == "CMD:EXIT")
            {
                logger.Info("--exit received. Shutting down.");
                Shutdown();
            }
            else if (receivedValue == "CMD:REFRESH")
            {
                await _profileManager.LoadProfilesAsync();
                ThemeHelper.RefreshThemes();
                ThemeHelper.ApplyTheme(_settingsManager.Settings.Theme);
            }
            else if (receivedValue.StartsWith("THEME:"))
            {
                string targetTheme = receivedValue.Substring(6);

                if (!string.IsNullOrEmpty(targetTheme))
                {
                    // Rescan so theme files added since startup is recognized
                    ThemeHelper.RefreshThemes();

                    if (!ThemeHelper.ThemeExists(targetTheme))
                    {
                        logger.Error($"IPC: '{targetTheme}' is not available theme. Ignored.");
                        return;
                    }

                    // RefreshThemes reapplies stored theme after setting changes
                    await _settingsManager.UpdateSettingAsync("Theme", targetTheme);
                }

                ThemeHelper.RefreshThemes();
            }
            else if (receivedValue.StartsWith("PROFILE:"))
            {
                string targetProfile = receivedValue.Substring(8);
                if (string.IsNullOrEmpty(targetProfile))
                    targetProfile = _settingsManager.GetCurrentProfileId();

                var profile = _profileManager.GetProfileByName(targetProfile) ?? _profileManager.GetProfile(targetProfile);
                if (profile == null)
                {
                    logger.Warn($"IPC: Profile '{targetProfile}' not found.");
                    return;
                }

                var ipcResult = await _profileManager.ApplyProfileAsync(profile, ProfileManager.ApplySource.CommandLine);
                if (!ipcResult.Success)
                    _trayIcon?.ShowNotification("Apply failed", _profileManager.GetApplyResultErrorMessage(profile.Name, ipcResult), TrayNotificationIcon.Error);
            }
        }

        private void StartShowWindowListener()
        {
            CancellationToken cancellationToken = _cancellationTokenSource.Token;
            EventWaitHandle showWindowEvent = _showWindowEvent;
            Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (showWindowEvent == null)
                            await Task.Delay(1000, cancellationToken);
                        else if (showWindowEvent.WaitOne(1000))
                        {
                            showWindowEvent.Reset();

                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    ShowMainWindow();
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, "Error showing main window from listener");
                                }
                            });
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error in show window listener");
                }
            }, cancellationToken);
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
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

        public async Task<UpdateCheckResult> CheckForUpdatesAndNotifyAsync(bool notify = true, bool force = false)
        {
            if (!_settingsManager.ShouldCheckForUpdates()) return null;

            if (force || _updateCheckTask == null)
                _updateCheckTask = UpdateHelper.CheckAsync();

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

            if (_mainWindow != null)
                _mainWindow.OpenSettingsWindow();
        }

        private void InitializeGlobalHotkeys()
        {
            try
            {
                _globalHotkeyHelper = new GlobalHotkeyHelper();
                RegisterAllProfileHotkeys();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error initializing global hotkeys");
            }
        }

        public void RegisterAllProfileHotkeys()
        {
            try
            {
                if (_globalHotkeyHelper == null || _profileManager == null || _settingsManager == null) return;

                var profileHotkeys = _profileManager.GetAllHotkeys();
                if (profileHotkeys.Count > 0)
                {
                    _globalHotkeyHelper.RegisterAllProfileHotkeys(profileHotkeys, CreateProfileHotkeyCallback);
                    logger.Info($"Registered {profileHotkeys.Count} profile hotkeys");
                }
                else
                {
                    _globalHotkeyHelper.UnregisterAllProfileHotkeys();
                    logger.Info("No enabled profile hotkeys - unregistered all");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error registering profile hotkeys");
            }
        }

        public void DisableProfileHotkeys()
        {
            try
            {
                _profileEditWindowCount++;
                logger.Debug($"ProfileEditWindow opened. Count: {_profileEditWindowCount}");

                if (!_hotkeysDisabledForEditing && _globalHotkeyHelper != null)
                {
                    _globalHotkeyHelper.UnregisterAllProfileHotkeys();
                    _hotkeysDisabledForEditing = true;
                    logger.Info("Disabled all profile hotkeys for editing");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disabling profile hotkeys");
            }
        }

        public void EnableProfileHotkeys()
        {
            try
            {
                _profileEditWindowCount = Math.Max(0, _profileEditWindowCount - 1);
                logger.Debug($"ProfileEditWindow closed. Count: {_profileEditWindowCount}");

                if (_profileEditWindowCount == 0 && _hotkeysDisabledForEditing)
                {
                    _hotkeysDisabledForEditing = false;
                    RegisterAllProfileHotkeys();
                    logger.Info("Re-enabled profile hotkeys after editing");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error re-enabling profile hotkeys");
            }
        }

        private Action CreateProfileHotkeyCallback(string profileId)
        {
            return () => ApplyProfileViaHotkey(profileId);
        }

        private async void ApplyProfileViaHotkey(string profileId)
        {
            try
            {
                var profile = _profileManager.GetProfile(profileId);
                if (profile != null)
                {
                    logger.Info($"Applying profile '{profile.Name}' via hotkey");

                    var applyResult = await _profileManager.ApplyProfileAsync(profile, ProfileManager.ApplySource.Hotkey);
                    if (!applyResult.Success)
                    {
                        string errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, applyResult);
                        logger.Warn(errorDetails);

                        _trayIcon?.ShowNotification("Apply failed", errorDetails, TrayNotificationIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error applying profile {profileId} via hotkey");
                try
                {
                    _trayIcon?.ShowNotification("Apply failed", "Error applying profile via hotkey", TrayNotificationIcon.Error);
                }
                catch { }
            }
        }

        private async Task<bool> ApplyProfileFromCommandLineAsync(string profileNameOrId)
        {
            try
            {
                logger.Info($"Applying profile '{profileNameOrId}' via CLI");

                _profileManager = ProfileManager.Instance;
                await SettingsManager.Instance.LoadSettingsAsync();
                await _profileManager.LoadProfilesAsync();

                var profile = _profileManager.GetProfileByName(profileNameOrId) ?? _profileManager.GetProfile(profileNameOrId);

                if (profile == null)
                {
                    logger.Warn($"Profile '{profileNameOrId}' not found.");
                    return false;
                }

                var result = await _profileManager.ApplyProfileAsync(profile, ProfileManager.ApplySource.CommandLine);
                if (!result.Success)
                {
                    var errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, result);
                    logger.Warn(errorDetails);
                    _trayIcon?.ShowNotification("Apply failed", errorDetails, TrayNotificationIcon.Error);
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying profile from CLI");
                return false;
            }
        }

        private void OnProfileChanged(object sender, Profile profile) => RegisterAllProfileHotkeys();

        private void OnProfileDeleted(object sender, string profileId)
        {
            try
            {
                _globalHotkeyHelper?.UnregisterProfileHotkey(profileId);
                logger.Info($"Unregistered hotkey for deleted profile: {profileId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error unregistering hotkey for deleted profile {profileId}");
            }
        }

        private void OnProfileApplied(object sender, ProfileManager.ProfileAppliedEventArgs e)
        {
            if (!_settingsManager.ShouldShowNotifications()) return;

            string source = e.Source switch
            {
                ProfileManager.ApplySource.Window => "Applied",
                ProfileManager.ApplySource.Tray => "Applied from tray",
                ProfileManager.ApplySource.Hotkey => "Applied by hotkey",
                ProfileManager.ApplySource.CommandLine => "Applied via CLI",
                ProfileManager.ApplySource.Startup => "Applied at startup",
                _ => "Applied"
            };

            double seconds = Math.Ceiling(e.DurationMilliseconds / 100.0) / 10.0;
            string elapsed = $"{seconds:0.#} {(seconds == 1 ? "second" : "seconds")}";

            _trayIcon?.ShowNotification(e.Profile.Name, $"{source} in {elapsed}", TrayNotificationIcon.None);
        }

        private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift) return;

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

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
                _cancellationTokenSource?.Cancel();
                _showWindowEvent?.Dispose();

                if (_ownsInstanceMutex)
                    _instanceMutex?.ReleaseMutex();

                _instanceMutex?.Dispose();

                _trayIcon?.Dispose();

                if (_profileManager != null)
                {
                    _profileManager.ProfileAdded -= OnProfileChanged;
                    _profileManager.ProfileUpdated -= OnProfileChanged;
                    _profileManager.ProfileDeleted -= OnProfileDeleted;
                    _profileManager.ProfileApplied -= OnProfileApplied;
                }

                if (_globalHotkeyHelper != null)
                {
                    _globalHotkeyHelper.UnregisterAllProfileHotkeys();
                    _globalHotkeyHelper.Dispose();
                }

                ThemeHelper.Cleanup();

            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during application exit");
            }

            logger.Info("Display Profile Manager Exited");
            base.OnExit(e);
        }
    }
}