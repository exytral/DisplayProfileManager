using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI;
using DisplayProfileManager.UI.Windows;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
        private ProfileManager _profileManager;
        private SettingsManager _settingsManager;
        private Mutex _instanceMutex;
        private EventWaitHandle _showWindowEvent;
        private CancellationTokenSource _cancellationTokenSource;
        private GlobalHotkeyHelper _globalHotkeyHelper;
        private int _profileEditWindowCount = 0;
        private bool _hotkeysDisabledForEditing = false;


        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int SW_RESTORE = 9;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private const string MUTEX_NAME = "DisplayProfileManager_SingleInstance";
        private const string SHOW_WINDOW_EVENT_NAME = "DisplayProfileManager_ShowWindow";


        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _settingsManager = SettingsManager.Instance;
            _profileManager = ProfileManager.Instance;

            logger.Info($"Display Profile Manager Starting | Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

            bool startInTray = false, devMode = false, isRefresh = false, isTheme = false, isProfile = false, isHeadless = false;
            string profile = null, theme = null;

            // Track commands to execute in order
            var commandQueue = new List<string>();

            if (e.Args?.Length > 0)
            {
                for (int i = 0; i < e.Args.Length; i++)
                {
                    string arg = e.Args[i].ToLower().TrimStart('-');

                    // Check for core behavior flags first
                    if (arg == "dev") { devMode = true; continue; }
                    if (arg == "tray") { startInTray = true; continue; }

                    if (arg.StartsWith("ref") || arg.StartsWith("rel") || arg == "r")
                    {
                        isRefresh = true;
                        commandQueue.Add("CMD:REFRESH");
                    }
                    else if (arg.StartsWith("t") && "theme".StartsWith(arg))
                    {
                        isTheme = true;
                        if (i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("-"))
                            theme = e.Args[++i];
                        commandQueue.Add($"THEME:{theme ?? ""}");
                    }
                    else if (arg.StartsWith("p") && "profile".StartsWith(arg))
                    {
                        isProfile = true;
                        if (i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("-"))
                            profile = e.Args[++i];
                        commandQueue.Add($"PROFILE:{profile ?? ""}");
                    }
                    else if (arg.StartsWith("h") && "headless".StartsWith(arg))
                    {
                        isHeadless = true;
                        if (i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("-"))
                            profile = e.Args[++i];
                        // Headless implies a profile application
                        if (!commandQueue.Any(c => c.StartsWith("PROFILE:")))
                            commandQueue.Add($"PROFILE:{profile ?? ""}");
                    }
                }
            }

            // Attempt to pass queued requests to an active instance
            if (!devMode && commandQueue.Count > 0)
            {
                bool allSent = true;
                foreach (var cmd in commandQueue)
                {
                    if (!await SendIPCMessageAsync(cmd))
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

                // IPC failed; handle local fallbacks for IPC-only commands
                if (isRefresh || (isTheme && string.IsNullOrEmpty(theme)))
                {
                    logger.Info("Target maintenance command requires an active instance. Exiting.");
                    Shutdown();
                    return;
                }

                // Persistent theme update if no instance is found
                if (isTheme && !string.IsNullOrEmpty(theme))
                {
                    await _settingsManager.LoadSettingsAsync();
                    await _settingsManager.UpdateSettingAsync("Theme", theme);
                    if (!isProfile) // If only theme was requested, exit now
                    {
                        logger.Info($"Theme '{theme}' saved locally. Exiting.");
                        Shutdown();
                        return;
                    }
                }
            }

            // Resolve current profile if reapply was requested locally
            if (isProfile && string.IsNullOrEmpty(profile))
            {
                await _settingsManager.LoadSettingsAsync();
                profile = _settingsManager.GetCurrentProfileId();
            }

            // Execute local profile application
            if (!string.IsNullOrEmpty(profile))
            {
                await ApplyProfileAsync(profile);
                if (isHeadless)
                {
                    logger.Info("Headless application complete. Exiting.");
                    Shutdown();
                    return;
                }
            }

            if (devMode) _cancellationTokenSource = new CancellationTokenSource();
            else if (!CheckSingleInstance()) { Shutdown(); return; }

            try
            {
                await InitializeApplicationAsync();

                if (string.IsNullOrEmpty(profile))
                    await HandleStartupProfileAsync();

                EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
                    new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel));

                SetupTrayIcon();
                if (!startInTray) ShowMainWindow();
                if (_settingsManager.IsFirstRun()) await _settingsManager.CompleteFirstRunAsync();
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
            _instanceMutex = new Mutex(true, MUTEX_NAME, out isNewInstance);

            if (!isNewInstance)
            {
                BringExistingInstanceToFront();
                return false;
            }

            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.ManualReset, SHOW_WINDOW_EVENT_NAME);
                _cancellationTokenSource = new CancellationTokenSource();
                StartShowWindowListener();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error setting up show window event");
            }

            return true;
        }

        private async Task ApplyProfileAsync(string profileNameOrId)
        {
            try
            {
                logger.Info($"CLI/Startup: Attempting to apply profile '{profileNameOrId}'");

                // Ensure core services are ready for CLI-first entry
                _profileManager = ProfileManager.Instance;
                await SettingsManager.Instance.LoadSettingsAsync();
                await _profileManager.LoadProfilesAsync();

                var profile = _profileManager.GetProfileByName(profileNameOrId)
                           ?? _profileManager.GetProfile(profileNameOrId);

                if (profile != null)
                {
                    var result = await _profileManager.ApplyProfileAsync(profile);
                    // ApplyProfileAsync already logs its result
                    //if (result.Success)
                    //    logger.Info($"Successfully applied '{profile.Name}'");
                    //else
                    //    logger.Error($"Failed to apply '{profile.Name}'");
                }
                else
                {
                    logger.Warn($"Profile '{profileNameOrId}' not found.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during ApplyProfileAsync execution");
            }
        }

        private async Task<bool> SendIPCMessageAsync(string message)
        {
            System.IO.Pipes.NamedPipeClientStream client = null;
            try
            {
                client = new System.IO.Pipes.NamedPipeClientStream(".", "DPM_ProfilePipe", System.IO.Pipes.PipeDirection.Out);

                await client.ConnectAsync(100);
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(client))
                {
                    await writer.WriteAsync(message);
                    await writer.FlushAsync();
                }
                return true;
            }
            catch
            {
                client?.Dispose();
                return false;
            }
        }

        private void StartIPCPipeListener()
        {
            Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    System.IO.Pipes.NamedPipeServerStream server = null;
                    try
                    {
                        server = new System.IO.Pipes.NamedPipeServerStream("DPM_ProfilePipe", System.IO.Pipes.PipeDirection.In);
                        await server.WaitForConnectionAsync(_cancellationTokenSource.Token);

                        using (System.IO.StreamReader reader = new System.IO.StreamReader(server))
                        {
                            string receivedValue = await reader.ReadToEndAsync();
                            if (!string.IsNullOrEmpty(receivedValue))
                            {
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    if (receivedValue == "CMD:REFRESH")
                                    {
                                        await _profileManager.LoadProfilesAsync();
                                        ThemeHelper.RefreshThemes();
                                        ThemeHelper.ApplyTheme(_settingsManager.Settings.Theme);
                                    }
                                    else if (receivedValue.StartsWith("THEME:"))
                                    {
                                        string targetTheme = receivedValue.Substring(6);

                                        if (string.IsNullOrEmpty(targetTheme))
                                            targetTheme = _settingsManager.Settings.Theme;
                                        else
                                            await _settingsManager.UpdateSettingAsync("Theme", targetTheme);

                                        ThemeHelper.RefreshThemes();
                                        ThemeHelper.ApplyTheme(targetTheme);
                                    }
                                    else if (receivedValue.StartsWith("PROFILE:"))
                                    {
                                        string targetProfile = receivedValue.Substring(8);

                                        if (string.IsNullOrEmpty(targetProfile))
                                            targetProfile = _settingsManager.GetCurrentProfileId();

                                        var profile = _profileManager.GetProfileByName(targetProfile)
                                                   ?? _profileManager.GetProfile(targetProfile);

                                        if (profile != null)
                                            await _profileManager.ApplyProfileAsync(profile);
                                        else
                                            logger.Warn($"IPC: Profile '{targetProfile}' not found.");
                                    }
                                });
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "IPC pipe listener error");
                    }
                    finally
                    {
                        server?.Dispose();
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private void StartShowWindowListener()
        {
            Task.Run(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        if (_showWindowEvent == null)
                        {
                            await Task.Delay(1000);
                        }
                        else if (_showWindowEvent.WaitOne(1000))
                        {
                            _showWindowEvent.Reset();

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
                catch (Exception ex)
                {
                    logger.Error(ex, "Error in show window listener");
                }
            }, _cancellationTokenSource.Token);
        }

        private void BringExistingInstanceToFront()
        {
            try
            {
                // Try to find the window first
                IntPtr hWnd = FindWindow(null, "Display Profile Manager");

                if (hWnd != IntPtr.Zero)
                {
                    // Window found, try to activate it
                    ActivateWindow(hWnd);
                }

                // Always try to signal the event (even if window was found)
                // This ensures the app shows even if it's in the tray
                try
                {
                    // Wait a moment to ensure the first instance has set up the listener
                    System.Threading.Thread.Sleep(100);

                    using (var showEvent = EventWaitHandle.OpenExisting(SHOW_WINDOW_EVENT_NAME))
                    {
                        showEvent.Set();
                    }
                }
                catch (Exception eventEx)
                {
                    logger.Error(eventEx, "Error signaling show window event");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error bringing existing instance to front");
            }
        }

        private void ActivateWindow(IntPtr hWnd)
        {
            try
            {
                // Get thread IDs
                uint currentThreadId = GetCurrentThreadId();
                uint windowThreadId = GetWindowThreadProcessId(hWnd, out _);

                // Attach thread input to bypass focus stealing prevention
                bool attached = false;
                if (currentThreadId != windowThreadId)
                {
                    attached = AttachThreadInput(currentThreadId, windowThreadId, true);
                }

                try
                {
                    // Restore if minimized
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                    }

                    // Bring to top
                    SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(hWnd);
                }
                finally
                {
                    // Detach thread input
                    if (attached)
                    {
                        AttachThreadInput(currentThreadId, windowThreadId, false);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error activating window");
            }
        }

        private async Task InitializeApplicationAsync()
        {
            StartIPCPipeListener();

            _settingsManager = SettingsManager.Instance;
            _profileManager = ProfileManager.Instance;

            await _settingsManager.LoadSettingsAsync();
            await _profileManager.LoadProfilesAsync();

            // Initialize theme system
            ThemeHelper.InitializeTheme();

            // Initialize audio system
            AudioHelper.InitializeAudio();

            // Initialize global hotkeys
            InitializeGlobalHotkeys();

            // Subscribe to profile events to keep hotkeys updated
            _profileManager.ProfileAdded += OnProfileChanged;
            _profileManager.ProfileUpdated += OnProfileChanged;
            _profileManager.ProfileDeleted += OnProfileDeleted;
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
                        var applyResult = await _profileManager.ApplyProfileAsync(startupProfile);

                        if (applyResult.Success)
                        {
                            string message = $"Startup profile '{startupProfile.Name}' successfully applied.";
                            logger.Info(message);

                            _trayIcon?.ShowNotification("Display Profile Manager", message, System.Windows.Forms.ToolTipIcon.Info);
                        }
                        else
                        {
                            string errorDetails = _profileManager.GetApplyResultErrorMessage(startupProfile.Name, applyResult);
                            logger.Warn(errorDetails);

                            _trayIcon?.ShowNotification("Display Profile Manager", $"Startup profile: {errorDetails}", System.Windows.Forms.ToolTipIcon.Info);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error applying startup profile");
            }
        }

        private void OnShowMainWindow(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void OnShowSettingsWindow(object sender, EventArgs e)
        {
            ShowMainWindow();

            // Open settings after showing main window
            if (_mainWindow != null)
            {
                _mainWindow.OpenSettingsWindow();
            }
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += OnMainWindowClosed;
            }

            // Ensure window is shown even if it was hidden
            _mainWindow.Show();

            // Restore window state if minimized
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            // Bring window to foreground
            _mainWindow.Topmost = true;
            _mainWindow.Activate();
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }

        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        private void OnExitApplication(object sender, EventArgs e)
        {
            Shutdown();
        }

        private void InitializeGlobalHotkeys()
        {
            try
            {
                _globalHotkeyHelper = new GlobalHotkeyHelper();

                // Register all profile hotkeys
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
                if (_globalHotkeyHelper == null || _profileManager == null || _settingsManager == null)
                    return;

                var profileHotkeys = _profileManager.GetAllHotkeys();
                if (profileHotkeys.Count > 0)
                {
                    _globalHotkeyHelper.RegisterAllProfileHotkeys(profileHotkeys, CreateProfileHotkeyCallback);
                    logger.Info($"Registered {profileHotkeys.Count} profile hotkeys");
                }
                else
                {
                    // No enabled hotkeys, unregister all
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
                // Clamp at 0 — if Window_Loaded incremented but the constructor failed before
                // the Closed handler was hooked, the count drifts and hotkeys stop working permanently.
                _profileEditWindowCount = Math.Max(0, _profileEditWindowCount - 1);
                logger.Debug($"ProfileEditWindow closed. Count: {_profileEditWindowCount}");

                // Only re-enable when all ProfileEditWindows are closed
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

                    var applyResult = await _profileManager.ApplyProfileAsync(profile);
                    if (applyResult.Success)
                    {
                        string message = $"Profile '{profile.Name}' applied";
                        logger.Info(message);

                        _trayIcon?.ShowNotification("Display Profile Manager", message, System.Windows.Forms.ToolTipIcon.Info);
                    }
                    else
                    {
                        string errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, applyResult);
                        logger.Warn(errorDetails);

                        _trayIcon?.ShowNotification("Display Profile Manager", errorDetails, System.Windows.Forms.ToolTipIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error applying profile {profileId} via hotkey");
                try
                {
                    _trayIcon?.ShowNotification("Display Profile Manager",
                        "Error applying profile via hotkey",
                        System.Windows.Forms.ToolTipIcon.Error);
                }
                catch { /* swallow: tray icon disposed or unavailable */ }
            }
        }

        private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift) return;

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void OnProfileChanged(object sender, Profile profile)
        {
            // Refresh all profile hotkeys when any profile is added or updated
            RegisterAllProfileHotkeys();
        }

        private void OnProfileDeleted(object sender, string profileId)
        {
            try
            {
                // Unregister the specific profile's hotkey
                _globalHotkeyHelper?.UnregisterProfileHotkey(profileId);
                logger.Info($"Unregistered hotkey for deleted profile: {profileId}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error unregistering hotkey for deleted profile {profileId}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();

                _showWindowEvent?.Dispose();

                _instanceMutex?.ReleaseMutex();
                _instanceMutex?.Dispose();

                _trayIcon?.Dispose();

                // Unsubscribe from profile events
                if (_profileManager != null)
                {
                    _profileManager.ProfileAdded -= OnProfileChanged;
                    _profileManager.ProfileUpdated -= OnProfileChanged;
                    _profileManager.ProfileDeleted -= OnProfileDeleted;
                }

                // Cleanup global hotkeys
                if (_globalHotkeyHelper != null)
                {
                    _globalHotkeyHelper.UnregisterAllProfileHotkeys();
                    _globalHotkeyHelper.Dispose();
                }

                // Cleanup theme system
                ThemeHelper.Cleanup();

                // Profiles are now saved individually when modified, no need to save all on exit

                if (_settingsManager != null)
                {
                    try
                    {
                        _settingsManager.SaveSettingsAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error saving settings on exit");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error during application exit");
            }

            logger.Info("========== Display Profile Manager Exiting ==========");


            base.OnExit(e);
        }
    }
}