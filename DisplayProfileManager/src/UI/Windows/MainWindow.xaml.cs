using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using DisplayProfileManager.UI.ViewModels;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace DisplayProfileManager.UI.Windows
{
    public partial class MainWindow : Window
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private ScriptManager _scriptManager;
        private ProfileManager _profileManager;
        private SettingsManager _settingsManager;
        private Profile _selectedProfile;
        private List<ProfileViewModel> _profileViewModels;
        private HwndSource _hwndSource;

        // State management
        private bool _isHoveringMaxButton = false;
        private bool _isApplying = false;

        private DateTime _hoverStartTime;
        private System.Windows.Threading.DispatcherTimer _snapLayoutsTimer;

        public MainWindow()
        {
            _scriptManager = ScriptManager.Instance;
            _profileManager = ProfileManager.Instance;
            _settingsManager = SettingsManager.Instance;

            InitializeComponent();

            SetupEventHandlers();

            LoadProfiles();
            InitializeSnapLayoutsTimer();

            Closing += MainWindow_Closing;
        }

        private void SetupEventHandlers()
        {
            _profileManager.ProfileAdded += OnProfileChanged;
            _profileManager.ProfileUpdated += OnProfileChanged;
            _profileManager.ProfileDeleted += OnProfileDeleted;
            _profileManager.ProfilesLoaded += OnProfilesLoaded;
            _profileManager.ProfileApplied += OnProfileApplied;
        }

        private void LoadProfiles()
        {
            try
            {
                StatusTextBlock.Text = "Loading profiles...";
                RefreshProfilesList();
                StatusTextBlock.Text = "Ready";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading profiles: {ex.Message}";
                MessageBox.Show($"Error loading profiles: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshProfilesList()
        {
            var profiles = _profileManager.GetAllProfiles().OrderBy(p => p.Name).ToList();
            _profileViewModels = new List<ProfileViewModel>();

            foreach (var profile in profiles)
            {
                var viewModel = new ProfileViewModel(profile);
                viewModel.IsActive = profile.Id == _profileManager.CurrentProfileId;
                _profileViewModels.Add(viewModel);
            }

            ProfilesListBox.ItemsSource = _profileViewModels;

            if (profiles.Count == 0)
            {
                StatusTextBlock.Text = "No profiles found. Create your first profile to get started.";
            }
        }

        private void UpdateProfileDetails(Profile profile)
        {
            if (profile == null)
            {
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
                DuplicateProfileButton.Visibility = Visibility.Collapsed;

                ProfileDetailsPanel.Children.Clear();
                ProfileDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Select a profile to view details",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    Foreground = (SolidColorBrush)FindResource("TertiaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 32, 0, 0)
                });

                SetManagementButtonsEnabled(false);
                return;
            }

            ActionButtonsPanel.Visibility = Visibility.Visible;
            ProfileDetailsPanel.Children.Clear();

            // Profile Information Section
            var nameRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            nameRow.Children.Add(new TextBlock
            {
                Text = profile.Name,
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            ProfileDetailsPanel.Children.Add(nameRow);

            if (!string.IsNullOrWhiteSpace(profile.Icon))
            {
                var iconSource = IconHelper.LoadImageSource(profile.Icon);
                if (iconSource != null)
                {
                    var iconImage = new Image
                    {
                        Source = iconSource,
                        Width = 18,
                        Height = 18,
                        Margin = new Thickness(0, 2, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RenderOptions.SetBitmapScalingMode(
                        iconImage, BitmapScalingMode.HighQuality);
                    nameRow.Children.Add(iconImage);
                }
            }

            if (!string.IsNullOrEmpty(profile.Description))
            {
                var descBlock = new TextBlock
                {
                    Text = profile.Description,
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                ProfileDetailsPanel.Children.Add(descBlock);
            }

            // Display Settings Section
            if (profile.DisplaySettings.Count > 0)
            {
                var displaysHeader = new TextBlock
                {
                    Text = "Displays",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(displaysHeader);

                foreach (var setting in profile.DisplaySettings)
                {
                    var settingPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                    if (!setting.IsEnabled)
                    {
                        var disabledBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(20, 255, 200, 0)),
                            BorderBrush = (SolidColorBrush)FindResource("TertiaryTextBrush"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8)
                        };

                        var innerPanel = new StackPanel();

                        var disabledIndicator = new TextBlock
                        {
                            Text = "⚠ DISABLED MONITOR",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 100, 0)),
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 4)
                        };
                        innerPanel.Children.Add(disabledIndicator);

                        var deviceName = new TextBlock
                        {
                            Text = !string.IsNullOrEmpty(setting.ReadableDeviceName) ? setting.ReadableDeviceName :
                                   (!string.IsNullOrEmpty(setting.DeviceString) ? setting.DeviceString : setting.DeviceName),
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontWeight = FontWeights.Medium,
                            Opacity = 0.7,
                            ToolTip = $"{setting.ReadableDeviceName ?? setting.DeviceString}\n{setting.DeviceName}\n\nThis monitor will be disabled when applying this profile"
                        };
                        innerPanel.Children.Add(deviceName);

                        var resolution = new TextBlock
                        {
                            Text = $"Resolution: {setting.GetResolutionString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                            Opacity = 0.6
                        };
                        innerPanel.Children.Add(resolution);

                        if (setting.Rotation != 1)
                        {
                            var rotation = new TextBlock
                            {
                                Text = $"Rotation: {GetRotationString(setting.Rotation)}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                                Opacity = 0.6
                            };
                            innerPanel.Children.Add(rotation);
                        }

                        if (setting.IsHdrSupported)
                        {
                            var hdr = new TextBlock
                            {
                                Text = $"HDR: {(setting.IsHdrEnabled ? "On" : "Off")}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                                Opacity = 0.6
                            };
                            innerPanel.Children.Add(hdr);
                        }

                        var dpi = new TextBlock
                        {
                            Text = $"DPI: {setting.GetDpiString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                            Opacity = 0.6
                        };
                        innerPanel.Children.Add(dpi);

                        if (setting.IsPrimary)
                        {
                            var primary = new TextBlock
                            {
                                Text = "Primary Display",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 11,
                                Foreground = (SolidColorBrush)FindResource("ButtonBackgroundBrush"),
                                FontWeight = FontWeights.Medium,
                                Opacity = 0.7
                            };
                            innerPanel.Children.Add(primary);
                        }

                        disabledBorder.Child = innerPanel;
                        settingPanel.Children.Add(disabledBorder);
                    }
                    else
                    {
                        var enabledBorder = new Border
                        {
                            Background = new SolidColorBrush(Colors.Transparent),
                            BorderBrush = (SolidColorBrush)FindResource("TertiaryTextBrush"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8)
                        };

                        var innerPanel = new StackPanel();

                        var deviceName = new TextBlock
                        {
                            Text = !string.IsNullOrEmpty(setting.ReadableDeviceName) ? setting.ReadableDeviceName :
                                   (!string.IsNullOrEmpty(setting.DeviceString) ? setting.DeviceString : setting.DeviceName),
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontWeight = FontWeights.Medium,
                            ToolTip = $"{setting.ReadableDeviceName ?? setting.DeviceString}\n{setting.DeviceName}"
                        };
                        innerPanel.Children.Add(deviceName);

                        var resolution = new TextBlock
                        {
                            Text = $"Resolution: {setting.GetResolutionString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                        };
                        innerPanel.Children.Add(resolution);

                        if (setting.Rotation != 1)
                        {
                            var rotation = new TextBlock
                            {
                                Text = $"Rotation: {GetRotationString(setting.Rotation)}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                            };
                            innerPanel.Children.Add(rotation);
                        }

                        if (setting.IsHdrSupported)
                        {
                            var hdr = new TextBlock
                            {
                                Text = $"HDR: {(setting.IsHdrEnabled ? "On" : "Off")}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                            };
                            innerPanel.Children.Add(hdr);
                        }

                        var dpi = new TextBlock
                        {
                            Text = $"DPI: {setting.GetDpiString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush")
                        };
                        innerPanel.Children.Add(dpi);

                        if (setting.IsPrimary)
                        {
                            var primary = new TextBlock
                            {
                                Text = "Primary Display",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 11,
                                Foreground = (SolidColorBrush)FindResource("ButtonBackgroundBrush"),
                                FontWeight = FontWeights.Medium
                            };
                            innerPanel.Children.Add(primary);
                        }

                        enabledBorder.Child = innerPanel;
                        settingPanel.Children.Add(enabledBorder);
                    }

                    ProfileDetailsPanel.Children.Add(settingPanel);
                }
            }
            SetManagementButtonsEnabled(true);
            DuplicateProfileButton.Visibility = Visibility.Visible;

            // Audio Settings Section
            if (profile.AudioSettings != null && (profile.AudioSettings.HasPlaybackDevice() || profile.AudioSettings.HasCaptureDevice()))
            {
                var audioHeader = new TextBlock
                {
                    Text = "Audio",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(audioHeader);

                var audioPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                if (profile.AudioSettings.HasPlaybackDevice())
                {
                    string outputText = profile.AudioSettings.ApplyPlaybackDevice
                        ? $"Output: {profile.AudioSettings.PlaybackDeviceName}"
                        : "Output: Not Applied";
                    var playbackDevice = new TextBlock
                    {
                        Text = outputText,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = profile.AudioSettings.ApplyPlaybackDevice
                            ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                            : (SolidColorBrush)FindResource("TertiaryTextBrush"),
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    audioPanel.Children.Add(playbackDevice);
                }

                if (profile.AudioSettings.HasCaptureDevice())
                {
                    string inputText = profile.AudioSettings.ApplyCaptureDevice
                        ? $"Input: {profile.AudioSettings.CaptureDeviceName}"
                        : "Input: Not Applied";
                    var captureDevice = new TextBlock
                    {
                        Text = inputText,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = profile.AudioSettings.ApplyCaptureDevice
                            ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                            : (SolidColorBrush)FindResource("TertiaryTextBrush")
                    };
                    audioPanel.Children.Add(captureDevice);
                }

                ProfileDetailsPanel.Children.Add(audioPanel);
            }

            // Scripts Settings Section
            if (profile.Scripts != null && profile.Scripts.Count > 0)
            {
                var scriptHeader = new TextBlock
                {
                    Text = profile.EnableScripts ? "Scripts" : "Scripts (Disabled):",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(scriptHeader);

                var scriptPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                foreach (var scriptEntry in profile.Scripts)
                {
                    string displayText = scriptEntry;
                    bool fileExists = false;

                    try
                    {
                        var parts = ScriptHelper.ParseScriptString(scriptEntry);
                        string parsedPath = parts.Path;
                        string sandboxPath = Path.Combine(_scriptManager.ScriptsFolderPath, parsedPath);
                        fileExists = File.Exists(sandboxPath);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error validating script path for {scriptEntry}: {ex.Message}");
                        fileExists = false;
                    }

                    var scriptItem = new TextBlock
                    {
                        Text = fileExists ? displayText : "⚠️ " + displayText + " (Not Found)",
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Foreground = fileExists
                            ? (SolidColorBrush)FindResource("SecondaryTextBrush")
                            : new SolidColorBrush(Colors.OrangeRed),
                        Margin = new Thickness(0, 0, 0, 2),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = profile.EnableScripts ? 1.0 : 0.6
                    };
                    scriptPanel.Children.Add(scriptItem);
                }

                ProfileDetailsPanel.Children.Add(scriptPanel);
            }

            // Hotkey Settings Section
            if (profile.HotkeyConfig != null && profile.HotkeyConfig.Key != System.Windows.Input.Key.None)
            {
                var hotkeyHeader = new TextBlock
                {
                    Text = "Hotkey Settings:",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(hotkeyHeader);

                var hotkeyPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                var hotkeyText = new TextBlock
                {
                    Text = $"Hotkey: {profile.HotkeyConfig}",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontSize = 12,
                    Foreground = (SolidColorBrush)FindResource("SecondaryTextBrush"),
                    Margin = new Thickness(0, 0, 0, 2)
                };
                hotkeyPanel.Children.Add(hotkeyText);

                var statusText = profile.HotkeyConfig.IsEnabled ? "Enabled" : "Disabled";
                var statusColor = profile.HotkeyConfig.IsEnabled ?
                    (SolidColorBrush)FindResource("SuccessButtonBackgroundBrush") :
                    (SolidColorBrush)FindResource("TertiaryTextBrush");

                var hotkeyStatus = new TextBlock
                {
                    Text = $"Status: {statusText}",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontSize = 12,
                    Foreground = statusColor,
                    FontWeight = FontWeights.Medium
                };
                hotkeyPanel.Children.Add(hotkeyStatus);

                ProfileDetailsPanel.Children.Add(hotkeyPanel);
            }

            var metaInfo = new TextBlock
            {
                Text = $"Created: {profile.CreatedDate:MMM d, yyyy 'at' h:mm tt}\nLast Modified: {profile.LastModifiedDate:MMM d, yyyy 'at' h:mm tt}",
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TertiaryTextBrush"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            ProfileDetailsPanel.Children.Add(metaInfo);
        }

        private void ProfilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedViewModel = ProfilesListBox.SelectedItem as ProfileViewModel;
            _selectedProfile = selectedViewModel?.Profile;
            UpdateProfileDetails(_selectedProfile);
        }

        private async void ProfilesListBoxItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = sender as ListBoxItem;
            var viewModel = item?.Content as ProfileViewModel;
            if (viewModel == null) return;

            var profile = viewModel.Profile;

            if (profile.Id == _profileManager.CurrentProfileId)
            {
                var editWindow = new ProfileEditWindow(profile) { Owner = this };
                editWindow.ShowDialog();
            }
            else
            {
                await ApplyProfile(profile);
            }
        }

        private async Task ApplyProfile(Profile profile)
        {
            if (_isApplying) return;
            try
            {
                _isApplying = true;

                var applyWatch = Stopwatch.StartNew();
                var applyResult = await _profileManager.ApplyProfileAsync(profile);
                applyWatch.Stop();

                if (!applyResult.Success)
                {
                    StatusTextBlock.Text = "Failed to apply profile";
                    string errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, applyResult);
                    logger.Warn(errorDetails);
                    MessageBox.Show(errorDetails, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    StatusTextBlock.Text = $"Profile '{profile.Name}' successfully applied in {(applyWatch.Elapsed.TotalSeconds == 0 ? "0" : $"{Math.Ceiling(applyWatch.Elapsed.TotalSeconds * 10) / 10:0.#}")} {(Math.Ceiling(applyWatch.Elapsed.TotalSeconds * 10) / 10 == 1 ? "second" : "seconds")}";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error applying profile";
                MessageBox.Show($"Exception: Error applying profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                logger.Error(ex, "Exception while applying profile");
            }
            finally
            {
                _isApplying = false;
            }
        }

        private async void ApplyProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var viewModel = btn?.DataContext as ProfileViewModel;
            var profile = viewModel?.Profile ?? _selectedProfile;
            if (profile == null) return;
            await ApplyProfile(profile);
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var editWindow = new ProfileEditWindow();
                editWindow.Owner = this;
                editWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening profile editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            try
            {
                var editWindow = new ProfileEditWindow(_selectedProfile);
                editWindow.Owner = this;
                editWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening profile editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            try
            {
                DuplicateProfileButton.IsEnabled = false;

                var duplicatedProfile = await _profileManager.DuplicateProfileAsync(_selectedProfile.Id);

                if (duplicatedProfile != null)
                {
                    StatusTextBlock.Text = $"Duplicated '{duplicatedProfile.Name}'";

                    RefreshProfilesList();

                    var duplicatedViewModel = _profileViewModels.FirstOrDefault(vm => vm.Profile.Id == duplicatedProfile.Id);
                    if (duplicatedViewModel != null)
                    {
                        ProfilesListBox.SelectedItem = duplicatedViewModel;
                    }

                    var editWindow = new ProfileEditWindow(duplicatedProfile);
                    editWindow.Owner = this;
                    editWindow.ShowDialog();
                }
                else
                {
                    StatusTextBlock.Text = "Error duplicating profile";
                    MessageBox.Show("Failed to duplicate profile. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error duplicating profile";
                MessageBox.Show($"Error duplicating profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DuplicateProfileButton.IsEnabled = true;
            }
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            var profileName = _selectedProfile.Name;

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{profileName}'?\n\nThis action cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _profileManager.DeleteProfileAsync(_selectedProfile.Id);
                    StatusTextBlock.Text = $"Deleted '{profileName}'";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting profile: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetManagementButtonsEnabled(bool isEnabled)
        {
            var visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
            ActionButtonsPanel.Visibility = visibility;
            DuplicateProfileButton.Visibility = visibility;

            EditProfileButton.IsEnabled = isEnabled;
            DuplicateProfileButton.IsEnabled = isEnabled;
            DeleteProfileButton.IsEnabled = isEnabled;
            ExportProfileButton.IsEnabled = isEnabled;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _profileManager.LoadProfilesAsync();
                RefreshProfilesList();
                ThemeHelper.RefreshThemes();

                // Reapply current theme unconditionally (in case of edits or deletions)
                var currentTheme = SettingsManager.Instance.Settings.Theme;
                ThemeHelper.ApplyTheme(currentTheme);
                StatusTextBlock.Text = "Refreshed profiles and themes";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error refreshing";
                MessageBox.Show($"Error refreshing: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        public void OpenSettingsWindow()
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }


        private void ToTrayButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeRestoreButton.Content = "\xE922"; // Maximize icon
                MaximizeRestoreButton.ToolTip = "Maximize";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeRestoreButton.Content = "\xE923"; // Restore icon
                MaximizeRestoreButton.ToolTip = "Restore Down";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void ExportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Profile",
                    Filter = "Display Profile (*.dpm)|*.dpm",
                    DefaultExt = ".dpm",
                    FileName = $"{_selectedProfile.Name}.dpm"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    ExportProfileButton.IsEnabled = false;

                    bool success = await _profileManager.ExportProfileAsync(_selectedProfile.Id, saveFileDialog.FileName);

                    if (success)
                    {
                        StatusTextBlock.Text = $"Exported '{_selectedProfile.Name}' to:\n{saveFileDialog.FileName}";
                    }
                    else
                    {
                        StatusTextBlock.Text = "Failed to export profile";
                        MessageBox.Show("Failed to export profile. Please try again.",
                            "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error exporting profile";
                MessageBox.Show($"Error exporting profile: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ExportProfileButton.IsEnabled = true;
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Profile, Theme, or Icon",
                    Filter = "Supported Files (*.dpm;*.xaml;*.ico)|*.dpm;*.xaml;*.ico|Profile (*.dpm)|*.dpm|Theme (*.xaml)|*.xaml|Icon (*.ico)|*.ico",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() != true) return;

                ImportButton.IsEnabled = false;
                string path = openFileDialog.FileName;
                string ext = Path.GetExtension(path).ToLower();

                if (ext == ".xaml")
                {
                    string themeName = await ThemeHelper.ImportThemeAsync(path);

                    if (themeName != null)
                        StatusTextBlock.Text = $"Imported and applied '{themeName}'";
                    else
                    {
                        StatusTextBlock.Text = "Failed to import theme";
                        MessageBox.Show("The file is not a valid DPM theme. Ensure it contains the required brush keys.",
                            "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (ext == ".dpm")
                {
                    var profile = await _profileManager.ImportProfileAsync(path);

                    if (profile != null)
                    {
                        StatusTextBlock.Text = $"'{profile.Name}' imported";
                        RefreshProfilesList();
                    }
                    else
                    {
                        StatusTextBlock.Text = "Failed to import profile";
                        MessageBox.Show("The file is not a valid DPM profile.",
                            "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (ext == ".ico")
                {
                    string filename = await IconHelper.ImportIconAsync(path);
                    StatusTextBlock.Text = $"Imported icon '{filename}'";
                }
                else
                {
                    StatusTextBlock.Text = "Unsupported file type";
                    MessageBox.Show($"'{Path.GetFileName(path)}' is not a supported file type.\n\nSupported types: .dpm, .xaml, .ico",
                        "Unsupported File", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error importing";
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportButton.IsEnabled = true;
            }
        }

        private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataFolder = _profileManager.GetAppDataFolder();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = dataFolder,
                    UseShellExecute = true,
                    Verb = "open"
                };
                Process.Start(startInfo);
                StatusTextBlock.Text = "Opened data folder";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error opening folder";
                MessageBox.Show($"Error opening data folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_settingsManager.ShouldRememberCloseChoice())
            {
                if (_settingsManager.ShouldCloseToTray())
                {
                    e.Cancel = true;
                    Hide();
                }
                else
                {
                    Application.Current.Shutdown();
                }
                return;
            }

            e.Cancel = true; // Cancel the close initially
            var dialog = new CloseConfirmationDialog();
            dialog.Owner = this;

            var result = dialog.ShowDialog();

            if (result == true)
            {
                if (dialog.RememberChoice)
                {
                    await _settingsManager.SetRememberCloseChoiceAsync(true);
                    await _settingsManager.SetCloseToTrayAsync(dialog.ShouldCloseToTray);
                }

                if (dialog.ShouldCloseToTray)
                {
                    Hide();
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
            // If result is false (Cancel or X button), do nothing - window stays open
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMaximizeRestoreButton();
            UpdateTitleBarMargin();

            LoadAppIcon();
        }

        private void LoadAppIcon()
        {
            try
            {
                var icon = Properties.Resources.AppIcon;
                if (icon != null)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    AppIconImage.Source = bitmap;

                    this.Icon = bitmap;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to load app icon");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            _hwndSource?.AddHook(WndProc);
        }

        private void InitializeSnapLayoutsTimer()
        {
            _snapLayoutsTimer = new System.Windows.Threading.DispatcherTimer();
            _snapLayoutsTimer.Interval = TimeSpan.FromMilliseconds(150);
            _snapLayoutsTimer.Tick += (s, e) =>
            {
                _snapLayoutsTimer.Stop();
                // Force a mouse position check to trigger HTMAXBUTTON if still hovering
                if (_isHoveringMaxButton)
                {
                    var pos = System.Windows.Forms.Cursor.Position;
                    SetCursorPos(pos.X, pos.Y); // Trigger a new WM_NCHITTEST
                }
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _snapLayoutsTimer?.Stop();
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();
            base.OnClosed(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            UpdateMaximizeRestoreButton();
            UpdateTitleBarMargin();

            base.OnStateChanged(e);
        }

        private void UpdateMaximizeRestoreButton()
        {
            if (MaximizeRestoreButton != null)
            {
                if (WindowState == WindowState.Maximized)
                {
                    MaximizeRestoreButton.Content = "\xE923";
                    MaximizeRestoreButton.ToolTip = "Restore Down";
                }
                else
                {
                    MaximizeRestoreButton.Content = "\xE922";
                    MaximizeRestoreButton.ToolTip = "Maximize";
                }
            }
        }

        private void UpdateTitleBarMargin()
        {
            if (TitleBarGrid != null)
            {
                if (WindowState == WindowState.Maximized)
                {
                    TitleBarGrid.Margin = new Thickness(8, 8, 6, 0);
                    UpdateTitleBarHeight(40);
                }
                else
                {
                    TitleBarGrid.Margin = new Thickness(0, 0, 0, 0);
                    UpdateTitleBarHeight(32);
                }
            }
        }

        private void UpdateTitleBarHeight(double height)
        {
            if (TitleBarRowDefinition != null)
            {
                TitleBarRowDefinition.Height = new GridLength(height);
            }

            var windowChrome = WindowChrome.GetWindowChrome(this);
            if (windowChrome != null)
            {
                windowChrome.CaptionHeight = height;
            }
        }

        private static string GetRotationString(int rotation)
        {
            switch (rotation)
            {
                case 2: return "90°";
                case 3: return "180°";
                case 4: return "270°";
                default: return "0°";
            }
        }

        private void OnProfileChanged(object sender, Profile profile)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshProfilesList();
                if (_selectedProfile?.Id == profile.Id)
                {
                    _selectedProfile = profile;
                    UpdateProfileDetails(_selectedProfile);
                }
            });
        }

        private void OnProfileDeleted(object sender, string profileId)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshProfilesList();
                if (_selectedProfile?.Id == profileId)
                {
                    _selectedProfile = null;
                    UpdateProfileDetails(null);
                    ProfilesListBox.SelectedItem = null;
                }
            });
        }

        private void OnProfilesLoaded(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshProfilesList();
            });
        }

        private void OnProfileApplied(object sender, Profile profile)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"Applied '{profile.Name}'";
                RefreshProfilesList();

                if (_selectedProfile != null)
                {
                    var viewModelToSelect = _profileViewModels.FirstOrDefault(vm => vm.Id == _selectedProfile.Id);
                    if (viewModelToSelect != null)
                    {
                        ProfilesListBox.SelectedItem = viewModelToSelect;
                    }
                }
            });
        }

        #region Windows Message Handling for Snap Layouts

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool PtInRect([In] ref RECT lprc, POINT pt);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_MOUSELEAVE = 0x02A3;
            const int HTMAXBUTTON = 9;

            switch (msg)
            {
                case WM_NCHITTEST:
                    int x = (short)((int)lParam & 0xFFFF);
                    int y = (short)(((int)lParam >> 16) & 0xFFFF);

                    // Convert screen point to client point
                    POINT pt = new POINT { X = x, Y = y };
                    ScreenToClient(hwnd, ref pt);

                    // Check if point is in the maximize button area
                    var buttonRect = GetMaximizeButtonRect();

                    if (PtInRect(ref buttonRect, pt))
                    {
                        if (!_isHoveringMaxButton)
                        {
                            // Start hover tracking
                            _isHoveringMaxButton = true;
                            _hoverStartTime = DateTime.Now;
                            _snapLayoutsTimer.Start();
                        }
                        else
                        {
                            // Check if enough time has passed to show Snap Layouts
                            var hoverDuration = DateTime.Now - _hoverStartTime;
                            if (hoverDuration.TotalMilliseconds >= 150)
                            {
                                handled = true;
                                return new IntPtr(HTMAXBUTTON);
                            }
                        }
                    }
                    else
                    {
                        // Mouse is not over maximize button
                        if (_isHoveringMaxButton)
                        {
                            _isHoveringMaxButton = false;
                            _snapLayoutsTimer.Stop();
                        }
                    }
                    break;

                case WM_MOUSEMOVE:
                    // Additional mouse move tracking if needed
                    break;

                case WM_MOUSELEAVE:
                    // Reset hover state when mouse leaves window
                    _isHoveringMaxButton = false;
                    _snapLayoutsTimer.Stop();
                    break;
            }

            return IntPtr.Zero;
        }

        private RECT GetMaximizeButtonRect()
        {
            int windowWidth = (int)this.ActualWidth;
            int buttonWidth = 46;
            int titleBarHeight = 32;

            return new RECT
            {
                left = windowWidth - (buttonWidth * 2),
                top = 0,
                right = windowWidth - buttonWidth,
                bottom = titleBarHeight
            };
        }

        #endregion
    }
}