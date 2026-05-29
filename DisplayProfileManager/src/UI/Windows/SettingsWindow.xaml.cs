using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shell;

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
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoadingSettings = true;

            try
            {
                // Initialize title bar margin state
                UpdateTitleBarMargin();

                // Match owner height and position at open time
                if (Owner != null)
                {
                    Height = Owner.ActualHeight;
                    Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
                    Top = Owner.Top + (Owner.ActualHeight - Height) / 2;
                }

                // Load current settings
                var settings = _settingsManager.Settings;

                // General settings
                ThemeComboBox.ItemsSource = new[] { "System" }.Concat(ThemeHelper.AvailableThemes);
                string savedTheme = settings.Theme;
                ThemeComboBox.SelectedItem = ThemeHelper.AvailableThemes.Contains(savedTheme) || savedTheme == "System"
                    ? savedTheme
                    : "System";
                SelectComboBoxItemByTag(LanguageComboBox, settings.Language);

                // Startup settings
                StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
                StartInSystemTrayCheckBox.IsChecked = settings.StartInSystemTray;
                StartInSystemTrayCheckBox.IsEnabled = settings.StartWithWindows;

                // Auto-start mode settings
                if (settings.AutoStartMode == Core.AutoStartMode.Registry)
                    RegistryModeRadio.IsChecked = true;
                else
                    TaskSchedulerModeRadio.IsChecked = true;
                AutoStartModePanel.IsEnabled = settings.StartWithWindows;

                await LoadStartupProfiles();
                SelectComboBoxItemByTag(StartupProfileComboBox, settings.StartupProfileId);
                ApplyStartupProfileCheckBox.IsChecked = settings.ApplyStartupProfile;
                UpdateComboBoxOpacity();

                // Window behavior settings
                if (settings.CloseToTray)
                {
                    CloseToTrayRadio.IsChecked = true;
                }
                else
                {
                    ExitApplicationRadio.IsChecked = true;
                }
                RememberCloseChoiceCheckBox.IsChecked = settings.RememberCloseChoice;

                // Notifications settings
                ShowNotificationsCheckBox.IsChecked = settings.ShowNotifications;

                // Global hotkeys settings
                RefreshHotkeyList();

                // About section
                var versionLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(AboutHelper.GetInformationalVersion()))
                {
                    NavigateUri = new Uri("https://github.com/exytral/DisplayProfileManager/releases"),
                    Foreground = (Brush)FindResource("LinkBrush")
                };
                versionLink.RequestNavigate += Hyperlink_RequestNavigate;
                VersionTextBlock.Inlines.Clear();
                VersionTextBlock.Inlines.Add(versionLink);
                SettingsPathTextBlock.Text = AboutHelper.GetSettingsPath();
                LoadLibraries();
                LoadContributors();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void OnThemeChanged(object sender, EventArgs e) => ThemeComboBox.ItemsSource = new[] { "System" }.Concat(ThemeHelper.AvailableThemes);

        private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings) return;

            var theme = ThemeComboBox.SelectedItem as string;
            if (theme != null)
            {
                await _settingsManager.SetThemeAsync(theme);
                ThemeHelper.ApplyTheme(theme);
                ThemeHelper.UpdateThemeSubscription(theme);
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
                await _settingsManager.SetStartWithWindowsAsync(isChecked);

                StartInSystemTrayCheckBox.IsEnabled = isChecked;
                AutoStartModePanel.IsEnabled = isChecked;

                if (!isChecked)
                    StartInSystemTrayCheckBox.IsChecked = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating startup setting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StartWithWindowsCheckBox.IsChecked = !StartWithWindowsCheckBox.IsChecked;
            }
        }

        private async void StartInSystemTrayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                var isChecked = StartInSystemTrayCheckBox.IsChecked ?? false;
                await _settingsManager.SetStartInSystemTrayAsync(isChecked);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating system tray startup setting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StartInSystemTrayCheckBox.IsChecked = !StartInSystemTrayCheckBox.IsChecked;
            }
        }

        private async void AutoStartModeRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings) return;

            try
            {
                AutoStartMode selectedMode = RegistryModeRadio.IsChecked == true? AutoStartMode.Registry : AutoStartMode.TaskScheduler;

                // Check if switching to Task Scheduler mode
                if (selectedMode == AutoStartMode.TaskScheduler)
                {
                    // Check if already running as admin
                    if (!AutoStartHelper.IsRunningAsAdmin())
                    {
                        var result = MessageBox.Show(
                            "Quick Launch mode requires administrator privileges for initial setup.\n\n" +
                            "You are not currently running as administrator. The system will attempt to create the task, " +
                            "which may prompt for elevation.\n\n" +
                            "Do you want to continue?",
                            "Administrator Privileges Required for Setup",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.No)
                        {
                            // Revert to Registry mode
                            _isLoadingSettings = true;
                            RegistryModeRadio.IsChecked = true;
                            _isLoadingSettings = false;
                            return;
                        }
                    }
                }

                // Attempt to change the mode
                bool success = await _settingsManager.SetAutoStartModeAsync(selectedMode);
                if (!success)
                {
                    MessageBox.Show(
                        $"Failed to switch to {selectedMode} mode. " +
                        (selectedMode == AutoStartMode.TaskScheduler
                            ? "Administrator privileges may be required for setup."
                            : "Please check the logs for more details."),
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    // Revert to previous mode
                    _isLoadingSettings = true;
                    if (selectedMode == AutoStartMode.Registry)
                        TaskSchedulerModeRadio.IsChecked = true;
                    else
                        RegistryModeRadio.IsChecked = true;
                    _isLoadingSettings = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error changing auto-start mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Revert to Registry mode on error
                _isLoadingSettings = true;
                RegistryModeRadio.IsChecked = true;
                _isLoadingSettings = false;
            }
        }

        private void UpdateComboBoxOpacity() => StartupProfileComboBox.Opacity = (ApplyStartupProfileCheckBox.IsChecked == true) ? 1.0 : 0.5;

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

        private async System.Threading.Tasks.Task LoadStartupProfiles()
        {
            try
            {
                await _profileManager.LoadProfilesAsync();
                var profiles = _profileManager.GetAllProfiles();

                StartupProfileComboBox.Items.Clear();
                StartupProfileComboBox.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });

                foreach (var profile in profiles)
                {
                    StartupProfileComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = profile.Name,
                        Tag = profile.Id
                    });
                }
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
                            Foreground = profile.HotkeyConfig.IsEnabled? (Brush)FindResource("SuccessButtonBackgroundBrush") : (Brush)FindResource("TertiaryTextBrush"),
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
            UpdateTitleBarMargin();
            base.OnStateChanged(e);
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
                TitleBarRowDefinition.Height = new GridLength(height);

            var windowChrome = WindowChrome.GetWindowChrome(this);
            if (windowChrome != null)
                windowChrome.CaptionHeight = height;
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
                MessageBox.Show($"Could not open link: {e.Uri.AbsoluteUri}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
                    var libraryPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                    // Name hyperlink
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

                    // Add version, license, and description
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
                        Name        = AboutHelper.Contributors.Zac15987Name,
                        Url         = AboutHelper.Contributors.Zac15987Url,
                        LinkLabel   = AboutHelper.Contributors.Zac15987LinkLabel,
                        LinkUrl     = AboutHelper.Contributors.Zac15987LinkUrl,
                        Description = AboutHelper.Contributors.Zac15987Desc,
                        SubText     = "(community requests: audio switching by @Catriks & @Alienmario; hotkeys by @anodynos; monitor disable/enable by @xtrilla)"
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
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.XtrillaName,
                        Url         = AboutHelper.Contributors.XtrillaUrl,
                        LinkLabel   = AboutHelper.Contributors.XtrillaLinkLabel,
                        LinkUrl     = AboutHelper.Contributors.XtrillaLinkUrl,
                        Description = AboutHelper.Contributors.XtrillaDesc,
                        SubText     = (string)null
                    },
                    new
                    {
                        Name        = AboutHelper.Contributors.ExytralName,
                        Url         = AboutHelper.Contributors.ExytralUrl,
                        LinkLabel   = (string)null,
                        LinkUrl     = (string)null,
                        Description = AboutHelper.Contributors.ExytralDesc,
                        SubText     = "(community requests: custom profile icons by @ffgtthr)"
                    },
                };

                foreach (var contributor in contributors)
                {
                    bool isBaseAuthor = contributor.Name == AboutHelper.Contributors.Zac15987Name || contributor.Name == AboutHelper.Contributors.ExytralName;

                    // Render bullets only for the child contributors
                    double leftIndent = isBaseAuthor ? 0 : 8;
                    var entryPanel = new StackPanel { Margin = new Thickness(leftIndent, 2, 0, 2) };

                    var linePanel = new StackPanel { Orientation = Orientation.Horizontal };

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

                    // Name hyperlink
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

                    // Label hyperlink
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

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }
    }
}