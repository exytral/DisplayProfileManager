using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
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

namespace DisplayProfileManager.UI.Windows
{
    public partial class SettingsWindow : Window
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private readonly SettingsManager _settingsManager;
        private readonly ProfileManager _profileManager;
        private readonly AutoStartHelper _autoStartHelper;
        private bool _isLoadingSettings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settingsManager = SettingsManager.Instance;
            _profileManager = ProfileManager.Instance;
            _autoStartHelper = new AutoStartHelper();
            ThemeHelper.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeHelper.ThemeChanged -= OnThemeChanged;

            InitializeStates();
        }

        private void InitializeStates()
        {
            var settings = _settingsManager.Settings;
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
                ShowNotificationsCheckBox.IsChecked = settings.ShowNotifications;
                RememberCloseChoiceCheckBox.IsChecked = settings.RememberCloseChoice;
                ApplyStartupProfileCheckBox.IsChecked = settings.ApplyStartupProfile;

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
                SettingsPathTextBlock.Text = AboutHelper.GetSettingsPath();
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
                TitleBarHelper.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);

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

                var settings = _settingsManager.Settings;

                ThemeHelper.RefreshThemes();
                PopulateThemeComboBox();
                string savedTheme = settings.Theme;
                ThemeComboBox.SelectedItem = ThemeHelper.AvailableThemes.Contains(savedTheme) || savedTheme == "System"
                    ? savedTheme
                    : "System";
                SelectComboBoxItemByTag(LanguageComboBox, settings.Language);

                var liveAutoStart = _autoStartHelper.IsAutoStartEnabled();
                StartWithWindowsCheckBox.IsChecked = liveAutoStart;
                if (liveAutoStart != settings.StartWithWindows)
                {
                    logger.Info($"Auto-start setting was {settings.StartWithWindows} but system reports {liveAutoStart} -> trusting system");
                    _ = _settingsManager.SetStartWithWindowsStateOnlyAsync(liveAutoStart);
                }
                StartInSystemTrayCheckBox.IsEnabled = liveAutoStart;
                StartInSystemTrayCheckBox.Opacity = liveAutoStart ? 1.0 : UiOpacity.Inactive;
                AutoStartModePanel.IsEnabled = liveAutoStart;
                AutoStartModePanel.Opacity = liveAutoStart ? 1.0 : UiOpacity.Inactive;

                await RefreshStartupProfilesAsync(settings.StartupProfileId);
                UpdateDeleteThemeButtonState();

                DesktopContextMenuCheckBox.IsChecked = SettingsManager.Instance.IsDesktopContextMenuEnabled();

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

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            var parent = VisualTreeHelper.GetParent(scrollViewer);
            while (parent != null && !(parent is ScrollViewer))
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ScrollViewer parentScroller)
            {
                parentScroller.ScrollToVerticalOffset(parentScroller.VerticalOffset - e.Delta / 3);
                e.Handled = true;
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            var wasLoading = _isLoadingSettings;
            _isLoadingSettings = true;
            try
            {
                PopulateThemeComboBox();
                ThemeComboBox.SelectedItem = _settingsManager.Settings.Theme;

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
            ThemeComboBox.ItemsSource = new[] { "System" }.Concat(ThemeHelper.AvailableThemes);

            if (selected != null && ThemeComboBox.Items.Contains(selected))
                ThemeComboBox.SelectedItem = selected;

            UpdateDeleteThemeButtonState();
        }

        private void ThemeComboBox_DropDownOpened(object sender, EventArgs e)
        {
            var selected = ThemeComboBox.SelectedItem as string;

            ThemeHelper.RefreshThemes();
            PopulateThemeComboBox();

            if (selected != null && ThemeComboBox.Items.Contains(selected))
                ThemeComboBox.SelectedItem = selected;
        }

        private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDeleteThemeButtonState();
            if (_isLoadingSettings) return;

            var theme = ThemeComboBox.SelectedItem as string;
            if (theme != null)
            {
                await _settingsManager.SetThemeAsync(theme);
                ThemeHelper.ApplyTheme(theme);
                ThemeHelper.UpdateThemeSubscription(theme);
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

            var imported = await ThemeHelper.ImportThemeAsync(dialog.FileName);
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
            if (!ThemeHelper.IsUserTheme(theme)) return;

            if (MessageBox.Show($"Delete the theme '{theme}'? The file is removed from the Themes folder.", "Delete theme", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (await ThemeHelper.DeleteThemeAsync(theme))
            {
                PopulateThemeComboBox();

                ThemeComboBox.SelectedItem = _settingsManager.Settings.Theme;
                UpdateDeleteThemeButtonState();
            }
        }

        private void UpdateDeleteThemeButtonState()
        {
            DeleteThemeButton.IsEnabled = ThemeHelper.IsUserTheme(ThemeComboBox.SelectedItem as string);
        }

        private async void CheckForUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = CheckForUpdatesCheckBox.IsChecked ?? false;
            await _settingsManager.SetCheckForUpdatesAsync(isChecked);

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

            var selectedItem = LanguageComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
                await _settingsManager.UpdateSettingAsync("Language", selectedItem.Tag.ToString());
        }

        private async void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                var isChecked = StartWithWindowsCheckBox.IsChecked ?? false;
                var result = await _settingsManager.SetStartWithWindowsAsync(isChecked);
                bool effectiveState = _settingsManager.Settings.StartWithWindows;
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
                        _settingsManager.Settings.StartWithWindows;
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
                var result = await _settingsManager.SetStartInSystemTrayAsync(isChecked);

                if (result != AutoStartOperationResult.Success)
                {
                    var wasLoading = _isLoadingSettings;
                    _isLoadingSettings = true;
                    try
                    {
                        StartInSystemTrayCheckBox.IsChecked =
                            _settingsManager.Settings.StartInSystemTray;
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
                    StartInSystemTrayCheckBox.IsChecked = _settingsManager.Settings.StartInSystemTray;
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

            var previousMode = _settingsManager.Settings.AutoStartMode;
            try
            {
                AutoStartMode selectedMode = RegistryModeRadio.IsChecked == true ? AutoStartMode.Registry : AutoStartMode.TaskScheduler;

                if (selectedMode == AutoStartMode.TaskScheduler)
                {
                    if (!AutoStartHelper.IsRunningAsAdmin())
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

                var operationResult = await _settingsManager.SetAutoStartModeAsync(selectedMode);
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

            var selectedItem = StartupProfileComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem != null)
            {
                var profileId = selectedItem.Tag?.ToString() ?? "";
                var applyOnStartup = ApplyStartupProfileCheckBox.IsChecked ?? false;
                await _settingsManager.SetStartupProfileAsync(profileId, applyOnStartup);

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
            await _settingsManager.SetStartupProfileAsync(profileId, applyOnStartup);

            UpdateComboBoxOpacity();
        }

        private void BuildVersionLink()
        {
            var versionLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(AboutHelper.GetInformationalVersion()))
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

            foreach (var profile in _profileManager.GetAllProfiles())
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
                await _profileManager.LoadProfilesAsync();

                PopulateStartupProfiles();
                SelectComboBoxItemByTag(StartupProfileComboBox, selectedProfileId ?? string.Empty);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading startup profiles");
            }
        }

        private async void CloseActionRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var closeToTray = CloseToTrayRadio.IsChecked ?? false;
            await _settingsManager.SetCloseToTrayAsync(closeToTray);
        }

        private async void RememberCloseChoiceCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = RememberCloseChoiceCheckBox.IsChecked ?? false;
            await _settingsManager.SetRememberCloseChoiceAsync(isChecked);
        }

        private async void ShowNotificationsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var isChecked = ShowNotificationsCheckBox.IsChecked ?? false;
            await _settingsManager.SetNotificationsAsync(isChecked);
        }
        private async void AbortOnApplyFailureCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsManager.SetAbortOnApplyFailureAsync(AbortOnApplyFailureCheckBox.IsChecked ?? false);
            UpdateDisplayRecoveryUiState();
        }

        private async void RollbackAfterApplyFailureCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsManager.SetRollbackAfterApplyFailureAsync(RollbackAfterApplyFailureCheckBox.IsChecked ?? false);
            UpdateDisplayRecoveryUiState();
        }

        private async void RollbackTargetRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            await _settingsManager.SetRollbackToPreviousProfileAsync(RollbackToPreviousProfileRadio.IsChecked == true);
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
                logger.Warn($"Desktop context menu not enabled — ShellExt.dll missing from {dllPath}");
                MessageBox.Show($"ShellExt.dll was not found next to the application.\n\nExpected at:\n{dllPath}\n\nThe desktop context menu cannot be enabled without it. Reinstalling restores the file.", "Desktop context menu unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);

                _isLoadingSettings = true;
                DesktopContextMenuCheckBox.IsChecked = false;
                _isLoadingSettings = false;
                return;
            }

            bool saved = await SettingsManager.Instance.SetDesktopContextMenuAsync(enabled);
            if (!saved)
            {
                logger.Warn("Desktop context menu setting could not be saved -> leaving extension unregistered");
                _isLoadingSettings = true;
                DesktopContextMenuCheckBox.IsChecked = !enabled;
                _isLoadingSettings = false;
                return;
            }

            bool applied = enabled
                ? ShellContextMenuHelper.Register()
                : ShellContextMenuHelper.Unregister();

            if (applied) return;

            await SettingsManager.Instance.SetDesktopContextMenuAsync(!enabled);
            _isLoadingSettings = true;
            DesktopContextMenuCheckBox.IsChecked = !enabled;
            _isLoadingSettings = false;

            MessageBox.Show(
                enabled
                    ? "The desktop context menu could not be registered. Error recorded in logs."
                    : "The desktop context menu could not be unregistered. Error recorded in logs.",
                "Desktop context menu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RefreshHotkeyList()
        {
            try
            {
                HotkeyListPanel.Children.Clear();

                var profilesWithHotkeys = _profileManager.GetProfilesWithActiveHotkeys();

                if (profilesWithHotkeys.Count == 0)
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
                    foreach (var profile in profilesWithHotkeys)
                    {
                        var hotkeyItem = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(0, 2, 0, 2)
                        };

                        var profileNameText = new TextBlock
                        {
                            Text = $"{profile.Name}:",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };

                        var hotkeyText = new TextBlock
                        {
                            Text = profile.HotkeyConfig.ToString(),
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(8, 0, 0, 0)
                        };

                        if (profile.HotkeyConfig.IsEnabled)
                            hotkeyText.Foreground = (Brush)FindResource("PrimaryTextBrush");
                        else
                            hotkeyText.Foreground = (Brush)FindResource("TertiaryTextBrush");

                        var statusText = new TextBlock
                        {
                            Text = profile.HotkeyConfig.IsEnabled ? "(Enabled)" : "(Disabled)",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 11,
                            FontStyle = profile.HotkeyConfig.IsEnabled ? FontStyles.Normal : FontStyles.Italic,
                            Foreground = profile.HotkeyConfig.IsEnabled ? (Brush)FindResource("SuccessButtonBackgroundBrush") : (Brush)FindResource("TertiaryTextBrush"),
                            Margin = new Thickness(8, 0, 0, 0)
                        };

                        hotkeyItem.Children.Add(profileNameText);
                        hotkeyItem.Children.Add(hotkeyText);
                        hotkeyItem.Children.Add(statusText);
                        HotkeyListPanel.Children.Add(hotkeyItem);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error refreshing hotkey list");
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            TitleBarHelper.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
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
                logger.Error(ex, "Error opening URL: {Url}", e.Uri.AbsoluteUri);
                MessageBox.Show($"Could not open link: {e.Uri.AbsoluteUri}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task AppendUpdateAvailableAsync(bool force = false, bool notify = false)
        {
            var app = Application.Current as App;
            var result = app == null
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
                VersionTextBlock.Inlines.Add(new System.Windows.Documents.Run(AboutHelper.GetInformationalVersion() + " "));
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
                    new { Name = AboutHelper.Libraries.NewtonsoftName, Version = AboutHelper.Libraries.NewtonsoftVersion, License = AboutHelper.Libraries.NewtonsoftLicense, Url = AboutHelper.Libraries.NewtonsoftUrl, Description = "JSON serialization" },
                    new { Name = AboutHelper.Libraries.NLogName, Version = AboutHelper.Libraries.NLogVersion, License = AboutHelper.Libraries.NLogLicense, Url = AboutHelper.Libraries.NLogUrl, Description = "Logging framework" },
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
                        Text = $" v{library.Version} ({library.License}) - {library.Description}",
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
                logger.Error(ex, "Error loading libraries");
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
                        Name        = AboutHelper.Contributors.ExytralName,
                        Url         = AboutHelper.Contributors.ExytralUrl,
                        LinkLabel   = AboutHelper.Contributors.ExytralLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.ExytralLinkUrl,
                        Description = AboutHelper.Contributors.ExytralDesc,
                        SubText     = "(community requests: custom profile icons by @ffgtthr)"
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.VivittelName,
                        Url         = AboutHelper.Contributors.VivittelUrl,
                        LinkLabel   = AboutHelper.Contributors.VivittelLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.VivittelLinkUrl,
                        Description = AboutHelper.Contributors.VivittelDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.Zac15987Name,
                        Url         = AboutHelper.Contributors.Zac15987Url,
                        LinkLabel   = AboutHelper.Contributors.Zac15987LinkLabel,
                        LinkUrl     = AboutHelper.Contributors.Zac15987LinkUrl,
                        Description = AboutHelper.Contributors.Zac15987Desc,
                        SubText     = "(community requests: audio switching by @Catriks; hotkeys by @anodynos; monitor disable/enable by @xtrilla)"
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.JarandalName,
                        Url         = AboutHelper.Contributors.JarandalUrl,
                        LinkLabel   = AboutHelper.Contributors.JarandalLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.JarandalLinkUrl,
                        Description = AboutHelper.Contributors.JarandalDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.JonathanasdfName,
                        Url         = AboutHelper.Contributors.JonathanasdfUrl,
                        LinkLabel   = AboutHelper.Contributors.JonathanasdfLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.JonathanasdfLinkUrl,
                        Description = AboutHelper.Contributors.JonathanasdfDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.RvahilarioName,
                        Url         = AboutHelper.Contributors.RvahilarioUrl,
                        LinkLabel   = AboutHelper.Contributors.RvahilarioLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.RvahilarioLinkUrl,
                        Description = AboutHelper.Contributors.RvahilarioDesc,
                        SubText     = (string)null
                    }
                };

                foreach (var contributor in contributors)
                {
                    bool isBaseAuthor = contributor.Name == AboutHelper.Contributors.Zac15987Name || contributor.Name == AboutHelper.Contributors.ExytralName;

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
                logger.Error(ex, "Error loading contributors");
            }
        }
    }
}