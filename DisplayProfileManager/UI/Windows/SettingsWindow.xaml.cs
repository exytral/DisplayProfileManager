using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Common;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Integration;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.UI.Windows
{
    public partial class SettingsWindow : Window
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly SettingsService _settingsService;
        private readonly SettingsCoordinator _settingsCoordinator;
        private readonly IThemeService _themeService;
        private readonly ProfileService _profileService;
        private readonly Func<IDisposable> _suspendHotkeysForCapture;
        private bool _isLoadingSettings;
        private bool _quickSwitchMutationPending;
        private bool _closeAfterQuickSwitchMutation;

        internal SettingsWindow(
            SettingsService settingsService,
            SettingsCoordinator settingsCoordinator,
            IThemeService themeService,
            ProfileService profileService,
            Func<IDisposable> suspendHotkeysForCapture)
        {
            InitializeComponent();
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _settingsCoordinator = settingsCoordinator ?? throw new ArgumentNullException(nameof(settingsCoordinator));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _suspendHotkeysForCapture = suspendHotkeysForCapture ?? throw new ArgumentNullException(nameof(suspendHotkeysForCapture));
            QuickSwitchHotkeyEditor.CaptureScopeFactory = _suspendHotkeysForCapture;
            _themeService.ThemeChanged += OnThemeChanged;
            _settingsService.SettingsChanged += OnSettingsChanged;
            Closed += OnSettingsWindowClosed;
            Closing += OnSettingsWindowClosing;

            InitializeStates();
        }

        private void InitializeStates()
        {
            var settings = _settingsService.Settings;
            if (settings == null) return;

            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
                AbortOnApplyFailureCheckBox.IsChecked = settings.AbortOnApplyFailure;
                RollbackAfterApplyFailureCheckBox.IsChecked = settings.RollbackAfterApplyFailure;
                RollbackToPreviousProfileRadio.IsChecked = settings.RollbackToPreviousProfile;
                RollbackToSnapshotRadio.IsChecked = !settings.RollbackToPreviousProfile;
                StartInSystemTrayCheckBox.IsChecked = settings.StartInSystemTray;
                DesktopContextMenuCheckBox.IsChecked = settings.DesktopContextMenuEnabled;
                ManageStartMenuProfileShortcutsCheckBox.IsChecked = settings.ManageStartMenuProfileShortcuts;
                ShowNotificationsCheckBox.IsChecked = settings.ShowNotifications;
                RememberCloseChoiceCheckBox.IsChecked = settings.RememberCloseChoice;
                ApplyStartupProfileCheckBox.IsChecked = settings.ApplyStartupProfile;
                UpdateQuickSwitchControls(settings);

                if (settings.CloseToTray) CloseToTrayRadio.IsChecked = true;
                else ExitApplicationRadio.IsChecked = true;

                if (settings.AutoStartMode == AutoStartMode.Registry) RegistryModeRadio.IsChecked = true;
                else TaskSchedulerModeRadio.IsChecked = true;

                StartInSystemTrayCheckBox.IsEnabled = settings.StartWithWindows;
                StartInSystemTrayCheckBox.Opacity = settings.StartWithWindows ? 1.0 : UiOpacity.Inactive;
                AutoStartModePanel.IsEnabled = settings.StartWithWindows;
                AutoStartModePanel.Opacity = settings.StartWithWindows ? 1.0 : UiOpacity.Inactive;

                PopulateStartupProfiles();
                SelectComboBoxItemByTag(StartupProfileComboBox, settings.StartupProfileId);
                ApplyStartupProfileCheckBox.IsEnabled = !string.IsNullOrEmpty(settings.StartupProfileId);
                RefreshHotkeyList();
                BuildVersionLink();
                SettingsPathTextBlock.Text = AboutInfoProvider.GetSettingsPath();
                LoadLibraries();
                LoadContributors();
                UpdateComboBoxOpacity();

                UpdateDisplayRecoveryUiState();
            }
            finally
            {
                _isLoadingSettings = wasLoading;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoadingSettings = true;

            try
            {
                TitleBarLayout.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);

                // Match owner window size and position at open time
                if (Owner != null)
                {
                    var origin = Owner.PointToScreen(new Point(0, 0));
                    var source = PresentationSource.FromVisual(Owner);
                    var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                    Width = Owner.ActualWidth;
                    Height = Owner.ActualHeight;
                    Left = (origin.X / scale) + (Owner.ActualWidth - Width) / 2;
                    Top = (origin.Y / scale) + (Owner.ActualHeight - Height) / 2;
                }

                var settings = _settingsService.Settings;

                _themeService.RefreshThemes();
                PopulateThemeComboBox();
                string savedTheme = settings.Theme;
                ThemeComboBox.SelectedItem = _themeService.AvailableThemes.Contains(savedTheme) || savedTheme == "System"
                    ? savedTheme
                    : "System";
                SelectComboBoxItemByTag(LanguageComboBox, settings.Language);

                var liveAutoStart = _settingsCoordinator.IsAutoStartEnabled();
                StartWithWindowsCheckBox.IsChecked = liveAutoStart;
                if (liveAutoStart != settings.StartWithWindows)
                {
                    _logger.Info($"Auto-start setting was {settings.StartWithWindows} but system reports {liveAutoStart} -> trusting system");
                    _ = _settingsService.SetStartWithWindowsStateOnlyAsync(liveAutoStart);
                }
                StartInSystemTrayCheckBox.IsEnabled = liveAutoStart;
                StartInSystemTrayCheckBox.Opacity = liveAutoStart ? 1.0 : UiOpacity.Inactive;
                AutoStartModePanel.IsEnabled = liveAutoStart;
                AutoStartModePanel.Opacity = liveAutoStart ? 1.0 : UiOpacity.Inactive;

                await RefreshStartupProfilesAsync(settings.StartupProfileId);
                UpdateDeleteThemeButtonState();

                DesktopContextMenuCheckBox.IsChecked = _settingsService.IsDesktopContextMenuEnabled();

                await AppendUpdateAvailableAsync();
                LoadLibraries();
                LoadContributors();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void OnSettingsWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_quickSwitchMutationPending)
            {
                e.Cancel = true;
                _closeAfterQuickSwitchMutation = true;
                return;
            }

            QuickSwitchHotkeyEditor.CancelCapture();
        }

        private void OnSettingsWindowClosed(object sender, EventArgs e)
        {
            QuickSwitchHotkeyEditor.CancelCapture();
            _themeService.ThemeChanged -= OnThemeChanged;
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        private void FinishQuickSwitchMutation()
        {
            _quickSwitchMutationPending = false;
            if (!_closeAfterQuickSwitchMutation) return;

            _closeAfterQuickSwitchMutation = false;
            Dispatcher.BeginInvoke(new Action(Close));
        }

        private void OnSettingsChanged(object sender, AppSettings settings)
        {
            void Refresh()
            {
                UpdateQuickSwitchControls(settings);
                RefreshHotkeyList();
            }

            if (Dispatcher.CheckAccess())
                Refresh();
            else
                Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void UpdateQuickSwitchControls(AppSettings settings)
        {
            if (settings == null) return;

            bool wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                QuickSwitchEnabledCheckBox.IsChecked = settings.QuickSwitchEnabled;
                QuickSwitchUseActiveMonitorCheckBox.IsChecked = settings.QuickSwitchUseActiveMonitor;
                QuickSwitchMousePositionRadio.IsChecked =
                    settings.QuickSwitchActiveMonitorSource == QuickSwitchActiveMonitorSource.MousePosition;
                QuickSwitchWindowFocusRadio.IsChecked =
                    settings.QuickSwitchActiveMonitorSource == QuickSwitchActiveMonitorSource.WindowFocus;
                QuickSwitchActiveMonitorSourcePanel.IsEnabled = settings.QuickSwitchUseActiveMonitor;
                QuickSwitchActiveMonitorSourcePanel.Opacity =
                    settings.QuickSwitchUseActiveMonitor ? 1.0 : UiOpacity.Inactive;
                QuickSwitchHotkeyEditor.HotkeyConfig = settings.QuickSwitchHotkey?.Clone() ?? new HotkeyConfig();
                QuickSwitchHotkeyEditor.IsEnabled = true;
                QuickSwitchHotkeyEditor.Opacity = 1.0;
                QuickSwitchHotkeyEditor.ConflictingProfile = null;
                QuickSwitchConflictWarning.Visibility = Visibility.Collapsed;
                QuickSwitchClearButton.IsEnabled = settings.QuickSwitchHotkey?.Key != Key.None;
                QuickSwitchClearButton.Opacity = QuickSwitchClearButton.IsEnabled ? 1.0 : UiOpacity.Inactive;
            }
            finally
            {
                _isLoadingSettings = wasLoading;
            }
        }

        private async void QuickSwitchEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsService.SetQuickSwitchEnabledAsync(QuickSwitchEnabledCheckBox.IsChecked == true);
            UpdateQuickSwitchControls(_settingsService.Settings);
        }

        private async void QuickSwitchUseActiveMonitorCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsService.SetQuickSwitchUseActiveMonitorAsync(
                QuickSwitchUseActiveMonitorCheckBox.IsChecked == true);
            UpdateQuickSwitchControls(_settingsService.Settings);
        }

        private async void QuickSwitchActiveMonitorSourceRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || sender is not RadioButton radio || radio.IsChecked != true)
                return;

            QuickSwitchActiveMonitorSource source =
                ReferenceEquals(radio, QuickSwitchWindowFocusRadio)
                    ? QuickSwitchActiveMonitorSource.WindowFocus
                    : QuickSwitchActiveMonitorSource.MousePosition;

            await _settingsService.SetQuickSwitchActiveMonitorSourceAsync(source);
            UpdateQuickSwitchControls(_settingsService.Settings);
        }

        private async void QuickSwitchHotkeyEditor_HotkeyChanged(object sender, HotkeyConfig e)
        {
            if (_isLoadingSettings || _quickSwitchMutationPending) return;

            _quickSwitchMutationPending = true;
            try
            {
                var conflictingProfile = FindQuickSwitchHotkeyConflict(_profileService.GetProfilesWithHotkeys(), e);
                if (conflictingProfile != null)
                {
                    QuickSwitchConflictWarning.Text = $"⚠ Already assigned to '{conflictingProfile.Name}'";
                    QuickSwitchConflictWarning.Visibility = Visibility.Visible;
                    QuickSwitchHotkeyEditor.RejectPendingHotkey(
                        _settingsService.GetQuickSwitchHotkey(),
                        conflictingProfile.Name);
                    return;
                }

                QuickSwitchConflictWarning.Visibility = Visibility.Collapsed;
                QuickSwitchHotkeyEditor.ConflictingProfile = null;

                bool saved = await _settingsService.SetQuickSwitchHotkeyAsync(e);
                if (saved)
                {
                    QuickSwitchHotkeyEditor.AcceptPendingHotkey(_settingsService.GetQuickSwitchHotkey());
                    RefreshHotkeyList();
                }
                else
                {
                    QuickSwitchConflictWarning.Text = "⚠ Could not save shortcut";
                    QuickSwitchConflictWarning.Visibility = Visibility.Visible;
                    QuickSwitchHotkeyEditor.RejectPendingHotkey(_settingsService.GetQuickSwitchHotkey(), "Save failed");
                }
            }
            finally
            {
                FinishQuickSwitchMutation();
            }
        }

        private void QuickSwitchClearButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            QuickSwitchHotkeyEditor.BeginExternalCommit();
        }

        private async void QuickSwitchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _quickSwitchMutationPending) return;

            bool capturePending = QuickSwitchHotkeyEditor.IsCommitPending;
            _quickSwitchMutationPending = true;
            try
            {
                bool saved = await _settingsService.ClearQuickSwitchHotkeyAsync();
                if (saved)
                {
                    if (capturePending)
                        QuickSwitchHotkeyEditor.AcceptPendingHotkey(new HotkeyConfig());
                    UpdateQuickSwitchControls(_settingsService.Settings);
                }
                else if (capturePending)
                {
                    QuickSwitchHotkeyEditor.RejectPendingHotkey(_settingsService.GetQuickSwitchHotkey(), "Save failed");
                }
            }
            finally
            {
                FinishQuickSwitchMutation();
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                PopulateThemeComboBox();
                ThemeComboBox.SelectedItem = _settingsService.Settings.Theme;

                RefreshHotkeyList();
                BuildVersionLink();
                LoadLibraries();
                LoadContributors();
            }
            finally
            {
                _isLoadingSettings = wasLoading;
            }
        }

        private void PopulateThemeComboBox()
        {
            var selected = ThemeComboBox.SelectedItem as string;
            ThemeComboBox.ItemsSource = new[] { "System" }.Concat(_themeService.AvailableThemes);

            if (selected != null && ThemeComboBox.Items.Contains(selected))
                ThemeComboBox.SelectedItem = selected;

            UpdateDeleteThemeButtonState();
        }

        private void ThemeComboBox_DropDownOpened(object sender, EventArgs e)
        {
            var selected = ThemeComboBox.SelectedItem as string;

            _themeService.RefreshThemes();
            PopulateThemeComboBox();

            if (selected != null && ThemeComboBox.Items.Contains(selected))
                ThemeComboBox.SelectedItem = selected;
        }

        private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeleteThemeButtonState();
            if (_isLoadingSettings) return;

            if (ThemeComboBox.SelectedItem is string theme)
            {
                await _settingsService.SetThemeAsync(theme);
                _themeService.ApplyTheme(theme);
                _themeService.UpdateThemeSubscription(theme);
            }
        }

        private async void ImportThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Theme files (*.xaml)|*.xaml",
                Title = "Import theme"
            };

            if (dialog.ShowDialog() != true) return;

            var imported = await _themeService.ImportThemeAsync(dialog.FileName);
            if (imported == null)
            {
                MessageBox.Show("That file could not be imported as a theme. It must be a ResourceDictionary containing the required brush keys.", "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PopulateThemeComboBox();
            UpdateDeleteThemeButtonState();
        }

        private async void DeleteThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var theme = ThemeComboBox.SelectedItem as string;
            if (!_themeService.IsUserTheme(theme)) return;

            if (MessageBox.Show($"Delete the theme '{theme}'? The file is removed from the Themes folder.", "Delete theme", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (await _themeService.DeleteThemeAsync(theme))
            {
                PopulateThemeComboBox();

                ThemeComboBox.SelectedItem = _settingsService.Settings.Theme;
                UpdateDeleteThemeButtonState();
            }
        }

        private void UpdateDeleteThemeButtonState()
        {
            DeleteThemeButton.IsEnabled = _themeService.IsUserTheme(ThemeComboBox.SelectedItem as string);
        }

        private async void CheckForUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = CheckForUpdatesCheckBox.IsChecked ?? false;
            await _settingsService.SetCheckForUpdatesAsync(isChecked);

            if (isChecked)
                await AppendUpdateAvailableAsync(force: true, notify: true);
            else
            {
                BuildVersionLink();
                if (Owner is MainWindow main)
                    main.ClearUpdateNotice();
            }
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
                await _settingsService.UpdateSettingAsync("Language", selectedItem.Tag.ToString());
        }

        private async void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                var isChecked = StartWithWindowsCheckBox.IsChecked ?? false;
                var result = await _settingsCoordinator.SetStartWithWindowsAsync(isChecked);
                bool effectiveState = _settingsService.Settings.StartWithWindows;
                if (result != AutoStartOperationResult.Success)
                {
                    var wasLoading = _isLoadingSettings;
                    _isLoadingSettings = true;
                    try
                    {
                        StartWithWindowsCheckBox.IsChecked = effectiveState;
                    }
                    finally
                    {
                        _isLoadingSettings = wasLoading;
                    }

                    if (result == AutoStartOperationResult.Canceled)
                        MessageBox.Show("Administrator approval was canceled. The requested auto-start change was not completed.", "Auto-start", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                    {
                        MessageBox.Show(
                            $"Failed to {(isChecked ? "enable" : "disable")} auto-start. " +
                            (isChecked
                                ? "Administrator privileges may be required for setup."
                                : "Please check the logs for more details."),
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }

                StartInSystemTrayCheckBox.IsEnabled = effectiveState;
                StartInSystemTrayCheckBox.Opacity = effectiveState ? 1.0 : UiOpacity.Inactive;
                AutoStartModePanel.IsEnabled = effectiveState;
                AutoStartModePanel.Opacity = effectiveState ? 1.0 : UiOpacity.Inactive;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating startup setting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                var wasLoading = _isLoadingSettings;
                _isLoadingSettings = true;
                try
                {
                    StartWithWindowsCheckBox.IsChecked =
                        _settingsService.Settings.StartWithWindows;
                }
                finally
                {
                    _isLoadingSettings = wasLoading;
                }
            }
        }

        private async void StartInSystemTrayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                var isChecked = StartInSystemTrayCheckBox.IsChecked ?? false;
                var result = await _settingsCoordinator.SetStartInSystemTrayAsync(isChecked);

                if (result != AutoStartOperationResult.Success)
                {
                    var wasLoading = _isLoadingSettings;
                    _isLoadingSettings = true;
                    try
                    {
                        StartInSystemTrayCheckBox.IsChecked =
                            _settingsService.Settings.StartInSystemTray;
                    }
                    finally
                    {
                        _isLoadingSettings = wasLoading;
                    }

                    if (result == AutoStartOperationResult.Canceled)
                        MessageBox.Show("Administrator approval was canceled. The requested tray-at-start change was not completed.", "Auto-start", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                        MessageBox.Show($"Failed to {(isChecked ? "enable" : "disable")} start in system tray. " + "Please check the logs for more details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating system tray startup setting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                var wasLoading = _isLoadingSettings;
                _isLoadingSettings = true;
                try
                {
                    StartInSystemTrayCheckBox.IsChecked = _settingsService.Settings.StartInSystemTray;
                }
                finally
                {
                    _isLoadingSettings = wasLoading;
                }
            }
        }

        private async void AutoStartModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var previousMode = _settingsService.Settings.AutoStartMode;
            try
            {
                AutoStartMode selectedMode = RegistryModeRadio.IsChecked == true ? AutoStartMode.Registry : AutoStartMode.TaskScheduler;

                if (selectedMode == AutoStartMode.TaskScheduler)
                {
                    if (!SettingsCoordinator.IsRunningAsAdmin())
                    {
                        var result = MessageBox.Show("Windows will prompt for elevation to create the scheduled task.\n\nContinue?", "Quick Launch setup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if (result == MessageBoxResult.No)
                        {
                            _isLoadingSettings = true;
                            RegistryModeRadio.IsChecked = previousMode == AutoStartMode.Registry;
                            TaskSchedulerModeRadio.IsChecked = previousMode == AutoStartMode.TaskScheduler;
                            _isLoadingSettings = false;
                            return;
                        }
                    }
                }

                var operationResult = await _settingsCoordinator.SetAutoStartModeAsync(selectedMode);
                if (operationResult != AutoStartOperationResult.Success)
                {
                    if (operationResult == AutoStartOperationResult.Canceled)
                        MessageBox.Show("Administrator approval was canceled. Auto-start mode was not changed.", "Auto-start", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                    {
                        MessageBox.Show(
                            $"Failed to switch to {selectedMode} mode. " +
                            (selectedMode == AutoStartMode.TaskScheduler
                                ? "Administrator privileges may be required for setup."
                                : "Please check the logs for more details."),
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    _isLoadingSettings = true;
                    RegistryModeRadio.IsChecked = previousMode == AutoStartMode.Registry;
                    TaskSchedulerModeRadio.IsChecked = previousMode == AutoStartMode.TaskScheduler;
                    _isLoadingSettings = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error changing auto-start mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                _isLoadingSettings = true;
                RegistryModeRadio.IsChecked = previousMode == AutoStartMode.Registry;
                TaskSchedulerModeRadio.IsChecked = previousMode == AutoStartMode.TaskScheduler;
                _isLoadingSettings = false;
            }
        }

        private void UpdateComboBoxOpacity() => StartupProfileComboBox.Opacity = (ApplyStartupProfileCheckBox.IsChecked == true) ? 1.0 : UiOpacity.Inactive;

        private async void StartupProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;

            if (StartupProfileComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var profileId = selectedItem.Tag?.ToString() ?? "";
                var applyOnStartup = ApplyStartupProfileCheckBox.IsChecked ?? false;
                await _settingsService.SetStartupProfileAsync(profileId, applyOnStartup);

                ApplyStartupProfileCheckBox.IsEnabled = !string.IsNullOrEmpty(profileId);
                if (string.IsNullOrEmpty(profileId))
                    ApplyStartupProfileCheckBox.IsChecked = false;

                UpdateComboBoxOpacity();
            }
        }

        private async void ApplyStartupProfileCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var profileId = (StartupProfileComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            var applyOnStartup = ApplyStartupProfileCheckBox.IsChecked ?? false;
            await _settingsService.SetStartupProfileAsync(profileId, applyOnStartup);

            UpdateComboBoxOpacity();
        }

        private void BuildVersionLink()
        {
            var versionLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(AboutInfoProvider.GetInformationalVersion()))
            {
                NavigateUri = new Uri("https://github.com/exytral/DisplayProfileManager/releases"),
                Foreground = (Brush)FindResource("LinkBrush")
            };
            versionLink.RequestNavigate += Hyperlink_RequestNavigate;

            VersionTextBlock.Inlines.Clear();
            VersionTextBlock.Inlines.Add(versionLink);
        }

        private void PopulateStartupProfiles()
        {
            StartupProfileComboBox.Items.Clear();
            StartupProfileComboBox.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });

            foreach (var profile in _profileService.GetAllProfiles())
            {
                StartupProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = profile.Name,
                    Tag = profile.Id
                });
            }
        }

        private async Task RefreshStartupProfilesAsync(string selectedProfileId = null)
        {
            try
            {
                await _profileService.LoadProfilesAsync();

                PopulateStartupProfiles();
                SelectComboBoxItemByTag(StartupProfileComboBox, selectedProfileId ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading startup profiles");
            }
        }

        private async void CloseActionRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var closeToTray = CloseToTrayRadio.IsChecked ?? false;
            await _settingsService.SetCloseToTrayAsync(closeToTray);
        }

        private async void RememberCloseChoiceCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = RememberCloseChoiceCheckBox.IsChecked ?? false;
            await _settingsService.SetRememberCloseChoiceAsync(isChecked);
        }

        private async void ShowNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = ShowNotificationsCheckBox.IsChecked ?? false;
            await _settingsService.SetNotificationsAsync(isChecked);
        }
        private async void AbortOnApplyFailureCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsService.SetAbortOnApplyFailureAsync(AbortOnApplyFailureCheckBox.IsChecked ?? false);
            UpdateDisplayRecoveryUiState();
        }

        private async void RollbackAfterApplyFailureCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsService.SetRollbackAfterApplyFailureAsync(RollbackAfterApplyFailureCheckBox.IsChecked ?? false);
            UpdateDisplayRecoveryUiState();
        }

        private async void RollbackTargetRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsService.SetRollbackToPreviousProfileAsync(RollbackToPreviousProfileRadio.IsChecked == true);
        }

        private void UpdateDisplayRecoveryUiState()
        {
            bool aborting = AbortOnApplyFailureCheckBox.IsChecked == true;
            bool rollingBack = aborting && RollbackAfterApplyFailureCheckBox.IsChecked == true;

            RollbackAfterApplyFailureCheckBox.IsEnabled = aborting;
            RollbackAfterApplyFailureCheckBox.Opacity = aborting ? 1.0 : UiOpacity.Inactive;

            RollbackToPreviousProfileRadio.IsEnabled = rollingBack;
            RollbackToPreviousProfileRadio.Opacity = rollingBack ? 1.0 : UiOpacity.Inactive;

            RollbackToSnapshotRadio.IsEnabled = rollingBack;
            RollbackToSnapshotRadio.Opacity = rollingBack ? 1.0 : UiOpacity.Inactive;
        }

        private static bool ShellExtensionDllExists(out string path)
        {
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShellExt.dll");
            return File.Exists(path);
        }

        private async void DesktopContextMenuCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            bool enabled = DesktopContextMenuCheckBox.IsChecked == true;

            if (enabled && !ShellExtensionDllExists(out string dllPath))
            {
                _logger.Warn($"Desktop context menu not enabled — ShellExt.dll missing from {dllPath}");
                MessageBox.Show($"ShellExt.dll was not found next to the application.\n\nExpected at:\n{dllPath}\n\nThe desktop context menu cannot be enabled without it. Reinstalling restores the file.", "Desktop context menu unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);

                _isLoadingSettings = true;
                DesktopContextMenuCheckBox.IsChecked = false;
                _isLoadingSettings = false;
                return;
            }

            bool applied = await _settingsCoordinator.SetDesktopContextMenuAsync(enabled);
            if (applied) return;

            _logger.Warn($"Desktop context menu could not be {(enabled ? "enabled" : "disabled")}");
            _isLoadingSettings = true;
            DesktopContextMenuCheckBox.IsChecked = _settingsService.Settings.DesktopContextMenuEnabled;
            _isLoadingSettings = false;

            MessageBox.Show(
                enabled
                    ? "The desktop context menu could not be registered. Error recorded in logs."
                    : "The desktop context menu could not be unregistered. Error recorded in logs.",
                "Desktop context menu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async void ManageStartMenuProfileShortcutsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            bool enabled = ManageStartMenuProfileShortcutsCheckBox.IsChecked == true;
            bool applied = await _settingsCoordinator.SetManageStartMenuProfileShortcutsAsync(enabled);
            if (applied) return;

            _logger.Warn($"Start Menu profile shortcuts could not be {(enabled ? "enabled" : "disabled")}");
            bool wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                ManageStartMenuProfileShortcutsCheckBox.IsChecked = _settingsService.Settings.ManageStartMenuProfileShortcuts;
            }
            finally
            {
                _isLoadingSettings = wasLoading;
            }

            MessageBox.Show(
                enabled
                    ? "Profile shortcuts could not be created in the Start Menu. Error recorded in logs."
                    : "Managed profile shortcuts could not be removed from the Start Menu. Error recorded in logs.",
                "Start Menu profile shortcuts", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RefreshHotkeyList()
        {
            try
            {
                HotkeyListPanel.Children.Clear();

                var profilesWithHotkeys = _profileService.GetProfilesWithHotkeys();
                var quickSwitchHotkey = _settingsService.GetQuickSwitchHotkey();
                bool hasQuickSwitchHotkey = ShouldShowConfiguredQuickSwitch(quickSwitchHotkey);

                if (profilesWithHotkeys.Count == 0 && !hasQuickSwitchHotkey)
                {
                    var noHotkeysText = new TextBlock
                    {
                        Text = "No hotkeys configured",
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TertiaryTextBrush"),
                        FontStyle = FontStyles.Italic
                    };
                    HotkeyListPanel.Children.Add(noHotkeysText);
                }
                else
                {
                    if (hasQuickSwitchHotkey)
                        AddConfiguredHotkeyItem("Quick Switch", quickSwitchHotkey, _settingsService.IsQuickSwitchEnabled());

                    foreach (var profile in profilesWithHotkeys)
                        AddConfiguredHotkeyItem(profile.Name, profile.HotkeyConfig, profile.HotkeyConfig.IsEnabled);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing hotkey list");
            }
        }

        internal static Profile FindQuickSwitchHotkeyConflict(System.Collections.Generic.IEnumerable<Profile> profiles, HotkeyConfig requested) =>
            HotkeyConflictPolicy.FindProfileConflict(profiles, requested);

        internal static bool ShouldShowConfiguredQuickSwitch(HotkeyConfig hotkey) =>
            HotkeyConflictPolicy.IsAssigned(hotkey);

        private void AddConfiguredHotkeyItem(string name, HotkeyConfig hotkey, bool enabled)
        {
            var hotkeyItem = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            hotkeyItem.Children.Add(new TextBlock
            {
                Text = $"{name}:",
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            hotkeyItem.Children.Add(new TextBlock
            {
                Text = hotkey.ToString(),
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = enabled
                    ? (Brush)FindResource("PrimaryTextBrush")
                    : (Brush)FindResource("TertiaryTextBrush"),
                Margin = new Thickness(8, 0, 0, 0)
            });

            hotkeyItem.Children.Add(new TextBlock
            {
                Text = enabled ? "(Enabled)" : "(Disabled)",
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontSize = 11,
                FontStyle = enabled ? FontStyles.Normal : FontStyles.Italic,
                Foreground = enabled
                    ? (Brush)FindResource("SuccessButtonBackgroundBrush")
                    : (Brush)FindResource("TertiaryTextBrush"),
                Margin = new Thickness(8, 0, 0, 0)
            });

            HotkeyListPanel.Children.Add(hotkeyItem);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            TitleBarLayout.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
            base.OnStateChanged(e);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error opening URL: {Url}", e.Uri.AbsoluteUri);
                MessageBox.Show($"Could not open link: {e.Uri.AbsoluteUri}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task AppendUpdateAvailableAsync(bool force = false, bool notify = false)
        {
            var result = System.Windows.Application.Current is not App app
                ? null
                : await app.CheckForUpdatesAndNotifyAsync(notify: notify, force: force);
            if (result == null || !result.UpdateAvailable) return;

            Dispatcher.Invoke(() =>
            {
                var link = new System.Windows.Documents.Hyperlink(
                    new System.Windows.Documents.Run($"({result.LatestVersion} available)"))
                {
                    NavigateUri = new Uri("https://github.com/exytral/DisplayProfileManager/releases"),
                    Foreground = (Brush)FindResource("LinkBrush")
                };
                link.RequestNavigate += Hyperlink_RequestNavigate;

                VersionTextBlock.Inlines.Clear();
                VersionTextBlock.Inlines.Add(new System.Windows.Documents.Run(AboutInfoProvider.GetInformationalVersion() + " "));
                VersionTextBlock.Inlines.Add(link);
            });
        }

        private void LoadLibraries()
        {
            try
            {
                LibrariesPanel.Children.Clear();

                var libraries = new[]
                {
                    new { Name = AboutInfoProvider.Libraries.NewtonsoftName, Version = AboutInfoProvider.Libraries.NewtonsoftVersion, License = AboutInfoProvider.Libraries.NewtonsoftLicense, Url = AboutInfoProvider.Libraries.NewtonsoftUrl, Description = "JSON serialization" },
                    new { Name = AboutInfoProvider.Libraries.NLogName, Version = AboutInfoProvider.Libraries.NLogVersion, License = AboutInfoProvider.Libraries.NLogLicense, Url = AboutInfoProvider.Libraries.NLogUrl, Description = "Logging framework" },
                    new { Name = AboutInfoProvider.Libraries.SystemManagementName, Version = AboutInfoProvider.Libraries.SystemManagementVersion, License = AboutInfoProvider.Libraries.SystemManagementLicense, Url = AboutInfoProvider.Libraries.SystemManagementUrl, Description = "Windows system management" },
                };

                foreach (var library in libraries)
                {
                    var libraryPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                    var libraryLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(library.Name))
                    {
                        NavigateUri = new Uri(library.Url),
                        Foreground = (Brush)FindResource("LinkBrush")
                    };
                    libraryLink.RequestNavigate += Hyperlink_RequestNavigate;

                    libraryPanel.Children.Add(new TextBlock(libraryLink)
                    {
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TertiaryTextBrush")
                    });

                    libraryPanel.Children.Add(new TextBlock
                    {
                        Text = AboutInfoProvider.Libraries.FormatLibraryDetails(library.Version, library.License, library.Description),
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TertiaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap
                    });

                    LibrariesPanel.Children.Add(libraryPanel);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading libraries");
            }
        }

        private void LoadContributors()
        {
            try
            {
                ContributorsPanel.Children.Clear();

                var contributors = new[]
                {
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.ExytralName,
                        Url         = AboutInfoProvider.Contributors.ExytralUrl,
                        LinkLabel   = AboutInfoProvider.Contributors.ExytralLinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.ExytralLinkUrl,
                        Description = AboutInfoProvider.Contributors.ExytralDesc,
                        SubText     = "(community requests: custom profile icons by @ffgtthr)"
                    },
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.VivittelName,
                        Url         = AboutInfoProvider.Contributors.VivittelUrl,
                        LinkLabel   = AboutInfoProvider.Contributors.VivittelLinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.VivittelLinkUrl,
                        Description = AboutInfoProvider.Contributors.VivittelDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.Zac15987Name,
                        Url         = AboutInfoProvider.Contributors.Zac15987Url,
                        LinkLabel   = AboutInfoProvider.Contributors.Zac15987LinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.Zac15987LinkUrl,
                        Description = AboutInfoProvider.Contributors.Zac15987Desc,
                        SubText     = "(community requests: audio switching by @Catriks; hotkeys by @anodynos; monitor disable/enable by @xtrilla)"
                    },
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.JarandalName,
                        Url         = AboutInfoProvider.Contributors.JarandalUrl,
                        LinkLabel   = AboutInfoProvider.Contributors.JarandalLinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.JarandalLinkUrl,
                        Description = AboutInfoProvider.Contributors.JarandalDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.JonathanasdfName,
                        Url         = AboutInfoProvider.Contributors.JonathanasdfUrl,
                        LinkLabel   = AboutInfoProvider.Contributors.JonathanasdfLinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.JonathanasdfLinkUrl,
                        Description = AboutInfoProvider.Contributors.JonathanasdfDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutInfoProvider.Contributors.RvahilarioName,
                        Url         = AboutInfoProvider.Contributors.RvahilarioUrl,
                        LinkLabel   = AboutInfoProvider.Contributors.RvahilarioLinkLabel,
                        LinkUrl     = AboutInfoProvider.Contributors.RvahilarioLinkUrl,
                        Description = AboutInfoProvider.Contributors.RvahilarioDesc,
                        SubText     = (string)null
                    }
                };

                foreach (var contributor in contributors)
                {
                    bool isBaseAuthor = contributor.Name == AboutInfoProvider.Contributors.Zac15987Name || contributor.Name == AboutInfoProvider.Contributors.ExytralName;

                    double leftIndent = isBaseAuthor ? 0 : 8;
                    var entryPanel = new StackPanel { Margin = new Thickness(leftIndent, 2, 0, 2) };

                    var linePanel = new WrapPanel { Orientation = Orientation.Horizontal };

                    if (!isBaseAuthor)
                    {
                        linePanel.Children.Add(new TextBlock
                        {
                            Text = "•",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (Brush)FindResource("TertiaryTextBrush"),
                            Margin = new Thickness(0, 0, 6, 0)
                        });
                    }

                    var nameLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(contributor.Name))
                    {
                        NavigateUri = new Uri(contributor.Url),
                        Foreground = (Brush)FindResource("LinkBrush")
                    };
                    nameLink.RequestNavigate += Hyperlink_RequestNavigate;

                    linePanel.Children.Add(new TextBlock(nameLink)
                    {
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TertiaryTextBrush")
                    });

                    if (!string.IsNullOrEmpty(contributor.LinkLabel))
                    {
                        var refLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(contributor.LinkLabel))
                        {
                            NavigateUri = new Uri(contributor.LinkUrl),
                            Foreground = (Brush)FindResource("LinkBrush")
                        };
                        refLink.RequestNavigate += Hyperlink_RequestNavigate;

                        var refBlock = new TextBlock
                        {
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (Brush)FindResource("TertiaryTextBrush")
                        };
                        refBlock.Inlines.Add(new System.Windows.Documents.Run(" ("));
                        refBlock.Inlines.Add(refLink);
                        refBlock.Inlines.Add(new System.Windows.Documents.Run(")"));
                        linePanel.Children.Add(refBlock);
                    }

                    linePanel.Children.Add(new TextBlock
                    {
                        Text = " — " + contributor.Description,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TertiaryTextBrush"),
                        TextWrapping = TextWrapping.Wrap
                    });

                    entryPanel.Children.Add(linePanel);

                    if (!string.IsNullOrEmpty(contributor.SubText))
                    {
                        entryPanel.Children.Add(new TextBlock
                        {
                            Text = contributor.SubText,
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 11,
                            Foreground = (Brush)FindResource("TertiaryTextBrush"),
                            FontStyle = FontStyles.Italic,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 1, 0, 0)
                        });
                    }

                    ContributorsPanel.Children.Add(entryPanel);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading contributors");
            }
        }
    }
}
