using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Audio;
using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts;
using DisplayProfileManager.Settings;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Displays;
using DisplayProfileManager.UI.Icons;
using DisplayProfileManager.UI.Interop;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.UI.Themes;
using DisplayProfileManager.UI.ViewModels;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.UI.Windows
{
    public partial class MainWindow : Window
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly ProfileService _profileService;
        private readonly SettingsService _settingsService;
        private readonly ScriptLibrary _scriptLibrary;
        private readonly IThemeService _themeService;
        private readonly IAudioDeviceService _audioService;
        private readonly ProfileEditorService _profileEditorService;
        private readonly SettingsCoordinator _settingsCoordinator;
        private readonly Func<IDisposable> _suspendAppHotkeysForCapture;

        private Profile _selectedProfile;
        private List<ProfileViewModel> _profileViewModels;
        private WindowSnapLayout _snapLayout;
        private bool _isApplying;

        internal MainWindow(
            ProfileService profileService,
            SettingsService settingsService,
            ScriptLibrary scriptLibrary,
            IThemeService themeService,
            IAudioDeviceService audioService,
            ProfileEditorService profileEditorService,
            SettingsCoordinator settingsCoordinator,
            Func<IDisposable> suspendAppHotkeysForCapture)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _scriptLibrary = scriptLibrary ?? throw new ArgumentNullException(nameof(scriptLibrary));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _profileEditorService = profileEditorService ?? throw new ArgumentNullException(nameof(profileEditorService));
            _settingsCoordinator = settingsCoordinator ?? throw new ArgumentNullException(nameof(settingsCoordinator));
            _suspendAppHotkeysForCapture = suspendAppHotkeysForCapture ?? throw new ArgumentNullException(nameof(suspendAppHotkeysForCapture));

            InitializeComponent();
            SetupEventHandlers();
            LoadProfiles();
            Closing += MainWindow_Closing;
        }

        private void SetupEventHandlers()
        {
            _profileService.ProfileAdded += OnProfileAdded;
            _profileService.ProfileUpdated += OnProfileUpdated;
            _profileService.ProfileDeleted += OnProfileDeleted;
            _profileService.ProfilesLoaded += OnProfilesLoaded;
            _profileService.ProfileApplied += OnProfileApplied;
            _themeService.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnThemeChanged(sender, e));
                return;
            }

            if (StatusTextBlock.Inlines.FirstInline is System.Windows.Documents.Hyperlink updateLink)
                ApplyUpdateNoticeForeground(updateLink);

            RefreshGeneratedProfileIcons();
        }

        private void RefreshGeneratedProfileIcons()
        {
            if (_profileViewModels == null)
                return;

            foreach (var viewModel in _profileViewModels)
            {
                if (ProfilesListBox.ItemContainerGenerator.ContainerFromItem(viewModel) is ListBoxItem item)
                {
                    var image = FindVisualChildByName<Image>(item, "ProfileLayoutImage");
                    image?.GetBindingExpression(Image.SourceProperty)?.UpdateTarget();
                }
            }
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
                {
                    return match;
                }

                var nested = FindVisualChildByName<T>(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        internal static object ResolveUpdateNoticeForegroundResourceKey(Func<object, object> tryFindResource)
        {
            if (tryFindResource == null) throw new ArgumentNullException(nameof(tryFindResource));

            return tryFindResource("SuccessButtonBackgroundBrush") != null
                ? "SuccessButtonBackgroundBrush"
                : "PrimaryTextBrush";
        }

        private static void ApplyUpdateNoticeForeground(System.Windows.Documents.Hyperlink link)
        {
            link.SetResourceReference(
                System.Windows.Documents.TextElement.ForegroundProperty,
                ResolveUpdateNoticeForegroundResourceKey(link.TryFindResource));
        }

        public void ShowUpdateAvailableNotice(UpdateCheckResult result)
        {
            if (result == null || !result.UpdateAvailable) return;

            Dispatcher.Invoke(() =>
            {
                var link = new System.Windows.Documents.Hyperlink(
                    new System.Windows.Documents.Run($"New update ({result.LatestVersion}) is available"))
                {
                    NavigateUri = new Uri(result.ReleaseUrl),
                    TextDecorations = null
                };
                link.RequestNavigate += (s, e) =>
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    e.Handled = true;
                };

                StatusTextBlock.Inlines.Clear();
                StatusTextBlock.Inlines.Add(link);
                ApplyUpdateNoticeForeground(link);
            });
        }

        public void ClearUpdateNotice()
        {
            if (StatusTextBlock.Inlines.FirstInline is System.Windows.Documents.Hyperlink)
                StatusTextBlock.Text = "Ready";
        }

        private void LoadProfiles()
        {
            try
            {
                RefreshProfilesList();
                StatusTextBlock.Text = "Ready";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading profiles: {ex.Message}";
                MessageBox.Show($"Error loading profiles: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshProfilesList()
        {
            var profiles = _profileService.GetAllProfiles().OrderBy(p => p.Name, NaturalStringComparer.Instance).ToList();
            _profileViewModels = [];

            var defaultProfileId = _profileService.GetDefaultProfile()?.Id;

            foreach (var profile in profiles)
            {
                var viewModel = new ProfileViewModel(profile)
                {
                    IsActive = profile.Id == _profileService.CurrentProfileId,
                    IsDefault = profile.Id == defaultProfileId
                };
                _profileViewModels.Add(viewModel);
            }

            ProfilesListBox.ItemsSource = _profileViewModels;

            if (profiles.Count == 0)
                StatusTextBlock.Text = "No profiles found. Create your first profile to get started.";
        }

        private void UpdateProfileDetails(Profile profile)
        {
            if (profile == null)
            {
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
                DuplicateProfileButton.Visibility = Visibility.Collapsed;

                ProfileDetailsPanel.Children.Clear();
                ProfileDetailsPanel.Children.Add(SetThemeResourceReference(new TextBlock
                {
                    Text = "Select a profile to view details",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 32, 0, 0)
                }, TextBlock.ForegroundProperty, "TertiaryTextBrush"));

                SetManagementButtonsEnabled(false);
                return;
            }

            ActionButtonsPanel.Visibility = Visibility.Visible;
            ProfileDetailsPanel.Children.Clear();

            // Profile Information Section
            var nameRow = new WrapPanel
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
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            ProfileDetailsPanel.Children.Add(nameRow);

            var iconSource = ProfileIconProvider.LoadImageSource(profile.Icon, 18);
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
                RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.HighQuality);
                nameRow.Children.Add(iconImage);
            }

            if (!string.IsNullOrEmpty(profile.Description))
            {
                var descBlock = SetThemeResourceReference(new TextBlock
                {
                    Text = profile.Description,
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 16)
                }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                ProfileDetailsPanel.Children.Add(descBlock);
            }

            // Display Section
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

                var layoutPreview = new SavedLayoutPreviewControl
                {
                    DisplaySettings = profile.DisplaySettings,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                ProfileDetailsPanel.Children.Add(layoutPreview);

                var displayGroups = DisplayGroupBuilder.GroupDisplaysForUI(profile.DisplaySettings);
                foreach (var group in displayGroups)
                {
                    var setting = group.RepresentativeSetting;
                    var displayMembers = group.AllMembers;
                    var settingPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                    if (!setting.IsEnabled)
                    {
                        const double DisabledDisplayDetailOpacity = 0.6;

                        var disabledBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(20, 255, 200, 0)),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8)
                        };
                        ApplyDetailsCardBorder(disabledBorder);

                        var innerPanel = new StackPanel();

                        var disabledIndicator = new TextBlock
                        {
                            Text = displayMembers.Count > 1 ? "DISABLED CLONE GROUP" : "DISABLED MONITOR",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 100, 0)),
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 4)
                        };
                        innerPanel.Children.Add(disabledIndicator);

                        string deviceText = displayMembers.Count > 1
                            ? string.Join("\n", displayMembers.Select(m =>
                            {
                                var name = !string.IsNullOrEmpty(m.ReadableDeviceName) ? m.ReadableDeviceName
                                        : (!string.IsNullOrEmpty(m.DeviceString) ? m.DeviceString : m.DeviceName);
                                return m.IsCloneSource ? $"{name}  (Source)" : $"{name}  (Clone)";
                            }))
                            : (!string.IsNullOrEmpty(setting.ReadableDeviceName) ? setting.ReadableDeviceName
                            : (!string.IsNullOrEmpty(setting.DeviceString) ? setting.DeviceString : setting.DeviceName));

                        var deviceName = new TextBlock
                        {
                            Text = deviceText,
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontWeight = FontWeights.Medium,
                            Opacity = 0.7,
                            TextWrapping = TextWrapping.Wrap,
                            ToolTip = displayMembers.Count > 1
                                ? $"Clone Group:\n{string.Join("\n", displayMembers.Select(m => $"• {m.ReadableDeviceName ?? m.DeviceString} ({m.DeviceName})"))}\n\nThese monitors will be disabled when applying this profile"
                                : $"{setting.ReadableDeviceName ?? setting.DeviceString}\n{setting.DeviceName}\n\nThis monitor will be disabled when applying this profile"
                        };
                        innerPanel.Children.Add(deviceName);

                        var resolution = SetThemeResourceReference(new TextBlock
                        {
                            Text = $"Resolution: {setting.ResolutionString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Opacity = DisabledDisplayDetailOpacity
                        }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                        innerPanel.Children.Add(resolution);

                        if (setting.Rotation != 1)
                        {
                            var rotation = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"Rotation: {RotationString(setting.Rotation)}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Opacity = DisabledDisplayDetailOpacity
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(rotation);
                        }

                        if (setting.IsHdrSupported)
                        {
                            var hdr = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"HDR: {(setting.IsHdrEnabled ? "On" : "Off")}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Opacity = DisabledDisplayDetailOpacity
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(hdr);
                        }

                        if (setting.IsWcgEnabled)
                        {
                            var wcg = SetThemeResourceReference(new TextBlock
                            {
                                Text = "WCG: On",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Opacity = DisabledDisplayDetailOpacity
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(wcg);
                        }

                        if (!string.IsNullOrEmpty(setting.ColorProfile))
                        {
                            var colorProfile = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"Color Profile: {setting.ColorProfile}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                                Opacity = DisabledDisplayDetailOpacity
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(colorProfile);
                        }

                        var dpi = SetThemeResourceReference(new TextBlock
                        {
                            Text = $"DPI: {setting.DpiString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                            Opacity = DisabledDisplayDetailOpacity
                        }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                        innerPanel.Children.Add(dpi);

                        if (setting.IsPrimary)
                        {
                            var primary = new TextBlock
                            {
                                Text = "Primary Display",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 11,
                                FontWeight = FontWeights.Medium,
                                Opacity = 0.7
                            };
                            primary.SetResourceReference(TextBlock.ForegroundProperty, "ButtonBackgroundBrush");
                        }

                        disabledBorder.Child = innerPanel;
                        settingPanel.Children.Add(disabledBorder);
                    }
                    else
                    {
                        var enabledBorder = new Border
                        {
                            Background = new SolidColorBrush(Colors.Transparent),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8)
                        };
                        ApplyDetailsCardBorder(enabledBorder);

                        var innerPanel = new StackPanel();

                        string deviceTextEnabled = displayMembers.Count > 1
                            ? string.Join("\n", displayMembers.Select(m =>
                            {
                                var name = !string.IsNullOrEmpty(m.ReadableDeviceName) ? m.ReadableDeviceName
                                        : (!string.IsNullOrEmpty(m.DeviceString) ? m.DeviceString : m.DeviceName);
                                return m.IsCloneSource ? $"{name}  (Source)" : $"{name}  (Clone)";
                            }))
                            : (!string.IsNullOrEmpty(setting.ReadableDeviceName) ? setting.ReadableDeviceName
                            : (!string.IsNullOrEmpty(setting.DeviceString) ? setting.DeviceString : setting.DeviceName));

                        var deviceName = new TextBlock
                        {
                            Text = deviceTextEnabled,
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontWeight = FontWeights.Medium,
                            TextWrapping = TextWrapping.Wrap,
                            ToolTip = displayMembers.Count > 1
                                ? $"Clone Group:\n{string.Join("\n", displayMembers.Select(m => $"• {m.ReadableDeviceName ?? m.DeviceString} ({m.DeviceName})"))}"
                                : $"{setting.ReadableDeviceName ?? setting.DeviceString}\n{setting.DeviceName}"
                        };
                        innerPanel.Children.Add(deviceName);

                        if (displayMembers.Count > 1)
                        {
                            var cloneIndicator = SetThemeResourceReference(new TextBlock
                            {
                                Text = "Clone Group",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 11,
                                FontWeight = FontWeights.Medium,
                            }, TextBlock.ForegroundProperty, "ButtonBackgroundBrush");
                            innerPanel.Children.Add(cloneIndicator);
                        }

                        var resolution = SetThemeResourceReference(new TextBlock
                        {
                            Text = $"Resolution: {setting.ResolutionString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                        }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                        innerPanel.Children.Add(resolution);

                        if (setting.Rotation != 1)
                        {
                            var rotation = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"Rotation: {RotationString(setting.Rotation)}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(rotation);
                        }

                        if (setting.IsHdrSupported)
                        {
                            var hdr = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"HDR: {(setting.IsHdrEnabled ? "On" : "Off")}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(hdr);
                        }

                        if (setting.IsWcgEnabled)
                        {
                            var wcg = SetThemeResourceReference(new TextBlock
                            {
                                Text = "WCG: On",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(wcg);
                        }

                        if (!string.IsNullOrEmpty(setting.ColorProfile))
                        {
                            var colorProfile = SetThemeResourceReference(new TextBlock
                            {
                                Text = $"Color Profile: {setting.ColorProfile}",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 12,
                            }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                            innerPanel.Children.Add(colorProfile);
                        }

                        var dpi = SetThemeResourceReference(new TextBlock
                        {
                            Text = $"DPI: {setting.DpiString()}",
                            Style = (Style)FindResource("PrimaryTextBlockStyle"),
                            FontSize = 12,
                        }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                        innerPanel.Children.Add(dpi);

                        if (setting.IsPrimary)
                        {
                            var primary = new TextBlock
                            {
                                Text = "Primary Display",
                                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                                FontSize = 11,
                                FontWeight = FontWeights.Medium
                            };
                            primary.SetResourceReference(TextBlock.ForegroundProperty, "ButtonBackgroundBrush");
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

            // Wallpaper Section
            if (profile.WallpaperSettings?.Enabled == true)
            {
                var wallpaperHeader = new TextBlock
                {
                    Text = "Wallpaper",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(wallpaperHeader);

                var wallpaperPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                var snapshot = profile.WallpaperSettings;

                void AddWallpaperLine(string text)
                {
                    wallpaperPanel.Children.Add(SetThemeResourceReference(new TextBlock
                    {
                        Text = text,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 2)
                    }, TextBlock.ForegroundProperty, "SecondaryTextBrush"));
                }

                AddWallpaperLine($"Mode: {WallpaperModeFormatter.Display(snapshot.Mode)}");

                if (snapshot.Mode == WallpaperMode.Picture)
                {
                    var fitment = WallpaperModeFormatter.NormalizePosition(snapshot.Position);
                    AddWallpaperLine($"Fitment: {char.ToUpper(fitment[0])}{fitment[1..]}");
                }
                else if (snapshot.Mode == WallpaperMode.Slideshow && snapshot.SlideshowConfig != null)
                {
                    var minutes = snapshot.SlideshowConfig.IntervalSeconds / 60;
                    AddWallpaperLine($"Changes Every: {(minutes >= 60 ? TextFormat.Plural(minutes / 60, "hour") : TextFormat.Plural(minutes, "minute"))}");
                    AddWallpaperLine($"Shuffle: {(snapshot.SlideshowConfig.Shuffle ? "On" : "Off")}");
                }

                ProfileDetailsPanel.Children.Add(wallpaperPanel);
            }

            // Audio Section
            bool anyAudioApplies = profile.AudioSettings != null
                && ((profile.AudioSettings.HasPlaybackDevice() && profile.AudioSettings.ApplyPlaybackDevice)
                 || (profile.AudioSettings.HasCaptureDevice() && profile.AudioSettings.ApplyCaptureDevice));

            if (anyAudioApplies)
            {
                var audioHeader = new TextBlock
                {
                    Text = profile.AudioSettings.Enabled ? "Audio" : "Audio (Disabled)",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(audioHeader);

                var audioPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                if (profile.AudioSettings.HasPlaybackDevice())
                {
                    bool outputAvailable = !profile.AudioSettings.ApplyPlaybackDevice
                        || _audioService.GetPlaybackDevices().Any(d => d.Id == profile.AudioSettings.DefaultPlaybackDeviceId);
                    string outputText = profile.AudioSettings.ApplyPlaybackDevice
                        ? $"Output: {profile.AudioSettings.PlaybackDeviceName}{(outputAvailable ? "" : " (Unavailable)")}"
                        : "Output: Not Applied";
                    var playbackDevice = SetThemeResourceReference(new TextBlock
                    {
                        Text = outputText,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 2)
                    }, TextBlock.ForegroundProperty, profile.AudioSettings.ApplyPlaybackDevice && outputAvailable
                        ? "SecondaryTextBrush"
                        : "TertiaryTextBrush");
                    audioPanel.Children.Add(playbackDevice);
                }

                if (profile.AudioSettings.HasCaptureDevice())
                {
                    bool inputAvailable = !profile.AudioSettings.ApplyCaptureDevice
                        || _audioService.GetCaptureDevices().Any(d => d.Id == profile.AudioSettings.DefaultCaptureDeviceId);
                    string inputText = profile.AudioSettings.ApplyCaptureDevice
                        ? $"Input: {profile.AudioSettings.CaptureDeviceName}{(inputAvailable ? "" : " (Unavailable)")}"
                        : "Input: Not Applied";
                    var captureDevice = SetThemeResourceReference(new TextBlock
                    {
                        Text = inputText,
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12
                    }, TextBlock.ForegroundProperty, profile.AudioSettings.ApplyCaptureDevice && inputAvailable
                        ? "SecondaryTextBrush"
                        : "TertiaryTextBrush");
                    audioPanel.Children.Add(captureDevice);
                }

                ProfileDetailsPanel.Children.Add(audioPanel);
            }

            // Script Section
            if (profile.ScriptSettings?.Scripts?.Count > 0)
            {
                var scriptHeader = new TextBlock
                {
                    Text = profile.ScriptSettings.Enabled ? "Scripts" : "Scripts (Disabled)",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(scriptHeader);

                var scriptPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                foreach (var script in profile.ScriptSettings.Scripts)
                {
                    string displayText = script.ToString();
                    bool fileExists = false;
                    try
                    {
                        string sandboxPath = Path.Combine(_scriptLibrary.ScriptsFolderPath, script.FileName);
                        fileExists = File.Exists(sandboxPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error validating script path for {script.FileName}: {ex.Message}");
                    }

                    var scriptItem = SetThemeResourceReference(new TextBlock
                    {
                        Style = (Style)FindResource("PrimaryTextBlockStyle"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 2),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = profile.ScriptSettings.Enabled && script.IsEnabled ? 1.0 : UiOpacity.Inactive
                    }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                    scriptItem.Inlines.Add(new System.Windows.Documents.Run(displayText));
                    if (!fileExists)
                    {
                        scriptItem.Inlines.Add(new System.Windows.Documents.Run(" (Not Found)")
                        {
                            Foreground = new SolidColorBrush(Colors.OrangeRed)
                        });
                    }
                    else if (!script.IsEnabled)
                        scriptItem.Inlines.Add(new System.Windows.Documents.Run(" (Disabled)"));
                    scriptPanel.Children.Add(scriptItem);
                }

                ProfileDetailsPanel.Children.Add(scriptPanel);
            }

            // Hotkey Section
            if (profile.HotkeyConfig != null && profile.HotkeyConfig.Key != System.Windows.Input.Key.None)
            {
                var hotkeyHeader = new TextBlock
                {
                    Text = "Hotkey:",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(0, 8, 0, 8)
                };
                ProfileDetailsPanel.Children.Add(hotkeyHeader);

                var hotkeyPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

                var hotkeyText = SetThemeResourceReference(new TextBlock
                {
                    Text = $"{profile.HotkeyConfig}",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 2)
                }, TextBlock.ForegroundProperty, "SecondaryTextBrush");
                hotkeyPanel.Children.Add(hotkeyText);

                var statusText = profile.HotkeyConfig.IsEnabled ? "Enabled" : "Disabled";
                var hotkeyStatus = SetThemeResourceReference(new TextBlock
                {
                    Text = $"{statusText}",
                    Style = (Style)FindResource("PrimaryTextBlockStyle"),
                    FontSize = 12,
                    FontWeight = FontWeights.Medium
                }, TextBlock.ForegroundProperty, profile.HotkeyConfig.IsEnabled
                    ? "SuccessButtonBackgroundBrush"
                    : "TertiaryTextBrush");
                hotkeyPanel.Children.Add(hotkeyStatus);

                ProfileDetailsPanel.Children.Add(hotkeyPanel);
            }

            var metaInfo = SetThemeResourceReference(new TextBlock
            {
                Text = $"Created: {profile.CreatedDate:MMM d, yyyy 'at' h:mm tt}\nLast Modified: {profile.LastModifiedDate:MMM d, yyyy 'at' h:mm tt}",
                Style = (Style)FindResource("PrimaryTextBlockStyle"),
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            }, TextBlock.ForegroundProperty, "TertiaryTextBrush");
            ProfileDetailsPanel.Children.Add(metaInfo);
        }

        private static T SetThemeResourceReference<T>(T element, DependencyProperty property, object resourceKey)
            where T : FrameworkElement
        {
            element.SetResourceReference(property, resourceKey);
            return element;
        }

        private static void ApplyDetailsCardBorder(Border border)
        {
            border.BorderThickness = new Thickness(1);
            border.SetResourceReference(
                Border.BorderBrushProperty,
                SystemParameters.HighContrast ? SystemColors.WindowTextBrushKey : ThemeService.DetailsBorderBrushKey);
        }

        private static string RotationString(int rotation)
        {
            return rotation switch
            {
                2 => "90°",
                3 => "180°",
                4 => "270°",
                _ => "0°",
            };
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
            if (item?.Content is not ProfileViewModel viewModel) return;

            var profile = viewModel.Profile;

            if (profile.Id == _profileService.CurrentProfileId)
            {
                var editWindow = new ProfileEditWindow(_profileEditorService, _settingsService, _suspendAppHotkeysForCapture, profile) { Owner = this };
                editWindow.ShowDialog();
            }
            else
                await ApplyProfile(profile);
        }

        internal static async Task RunWithApplyBusyStateAsync(Action<bool> setBusy, Func<Task> operation)
        {
            if (setBusy == null) throw new ArgumentNullException(nameof(setBusy));
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            setBusy(true);
            try
            {
                await operation();
            }
            finally
            {
                setBusy(false);
            }
        }

        private void SetApplyBusyState(bool isBusy)
        {
            _isApplying = isBusy;
            ProfilesListBox.IsHitTestVisible = !isBusy;
        }

        private async Task ApplyProfile(Profile profile)
        {
            if (_isApplying) return;

            await RunWithApplyBusyStateAsync(SetApplyBusyState, async () =>
            {
                try
                {
                    StatusTextBlock.Text = $"Applying '{profile.Name}'...";
                    var applyWatch = Stopwatch.StartNew();
                    var applyResult = await _profileService.ApplyProfileAsync(profile, ApplySource.Window);
                    applyWatch.Stop();

                    if (!applyResult.Success)
                    {
                        StatusTextBlock.Text = "Failed to apply profile";
                        string errorDetails = ProfileApplyFormatter.GetApplyResultErrorMessage(profile.Name, applyResult);
                        _logger.Warn(errorDetails);
                        MessageBox.Show(errorDetails, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        string elapsed = $"{(applyWatch.Elapsed.TotalSeconds == 0 ? "0"
                            : $"{Math.Ceiling(applyWatch.Elapsed.TotalSeconds * 10) / 10:0.#}")} {(Math.Ceiling(applyWatch.Elapsed.TotalSeconds * 10) / 10 == 1 ? "second" : "seconds")}";

                        string warningSummary = ProfileApplyFormatter.GetApplyWarningSummary(applyResult);
                        StatusTextBlock.Text = ProfileApplyFormatter.AppendApplyWarnings($"'{profile.Name}' applied in {elapsed}", warningSummary);
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error applying profile";
                    MessageBox.Show($"Exception: Error applying profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _logger.Error(ex, "Exception while applying profile");
                }
            });
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
                var editWindow = new ProfileEditWindow(_profileEditorService, _settingsService, _suspendAppHotkeysForCapture)
                {
                    Owner = this
                };
                editWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening profile editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            try
            {
                var editWindow = new ProfileEditWindow(_profileEditorService, _settingsService, _suspendAppHotkeysForCapture, _selectedProfile)
                {
                    Owner = this
                };
                editWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening profile editor: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;

            try
            {
                DuplicateProfileButton.IsEnabled = false;

                var duplicatedProfile = await _profileService.DuplicateProfileAsync(_selectedProfile.Id);
                if (duplicatedProfile != null)
                {
                    StatusTextBlock.Text = $"Duplicated '{duplicatedProfile.Name}'";

                    RefreshProfilesList();

                    var duplicatedViewModel = _profileViewModels.FirstOrDefault(vm => vm.Profile.Id == duplicatedProfile.Id);
                    if (duplicatedViewModel != null)
                        ProfilesListBox.SelectedItem = duplicatedViewModel;

                    var editWindow = new ProfileEditWindow(_profileEditorService, _settingsService, _suspendAppHotkeysForCapture, duplicatedProfile)
                    {
                        Owner = this
                    };
                    editWindow.ShowDialog();
                }
                else
                {
                    StatusTextBlock.Text = "Error duplicating profile";
                    MessageBox.Show("Failed to duplicate profile. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error duplicating profile";
                MessageBox.Show($"Error duplicating profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var result = MessageBox.Show($"Are you sure you want to delete '{profileName}'?\n\nThis action cannot be undone.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _profileService.DeleteProfileAsync(_selectedProfile.Id);
                    StatusTextBlock.Text = $"Deleted '{profileName}'";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshButton.IsEnabled = false;
                await _profileService.LoadProfilesAsync();
                RefreshProfilesList();
                _themeService.RefreshThemes();

                var currentTheme = _settingsService.Settings.Theme;
                _themeService.ApplyTheme(currentTheme);
                StatusTextBlock.Text = "Refreshed";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error refreshing";
                MessageBox.Show($"Error refreshing: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        public void OpenSettingsWindow()
        {
            var settingsWindow = new SettingsWindow(
                _settingsService,
                _settingsCoordinator,
                _themeService,
                _profileService,
                _suspendAppHotkeysForCapture)
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
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
                MaximizeRestoreButton.Content = "\xE922";
                MaximizeRestoreButton.ToolTip = "Maximize";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeRestoreButton.Content = "\xE923";
                MaximizeRestoreButton.ToolTip = "Restore Down";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataFolder = _profileService.GetAppDataFolder();
                ProcessStartInfo startInfo = new()
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
            if (_settingsService.ShouldRememberCloseChoice())
            {
                if (_settingsService.ShouldCloseToTray())
                {
                    e.Cancel = true;
                    Hide();
                }
                else
                    System.Windows.Application.Current.Shutdown();

                return;
            }

            e.Cancel = true;
            var dialog = new CloseConfirmationDialog(_settingsService)
            {
                Owner = this
            };

            var result = dialog.ShowDialog();

            if (result == true)
            {
                if (dialog.RememberChoice)
                {
                    await _settingsService.SetRememberCloseChoiceAsync(true);
                    await _settingsService.SetCloseToTrayAsync(dialog.ShouldCloseToTray);
                }

                if (dialog.ShouldCloseToTray)
                    Hide();
                else
                    System.Windows.Application.Current.Shutdown();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMaximizeRestoreButton();
            TitleBarLayout.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
            LoadAppIcon();
        }

        private void LoadAppIcon()
        {
            try
            {
                using var icon = ApplicationIconProvider.LoadIcon();
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                AppIconImage.Source = bitmap;
                this.Icon = bitmap;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to load app icon");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _snapLayout = new WindowSnapLayout(this, () => _themeService.RefreshSystemThemes());
        }

        protected override void OnClosed(EventArgs e)
        {
            _snapLayout?.Dispose();
            base.OnClosed(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            UpdateMaximizeRestoreButton();
            TitleBarLayout.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
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

        private void OnProfileAdded(object sender, Profile profile) => OnProfileChanged(profile, isNew: true);

        private void OnProfileUpdated(object sender, Profile profile) => OnProfileChanged(profile, isNew: false);

        private void OnProfileChanged(Profile profile, bool isNew)
        {
            Dispatcher.Invoke(() =>
            {
                var previouslySelectedId = _selectedProfile?.Id;

                RefreshProfilesList();

                if (previouslySelectedId == profile.Id)
                {
                    var viewModelToSelect = _profileViewModels.FirstOrDefault(vm => vm.Id == profile.Id);
                    if (viewModelToSelect != null)
                        ProfilesListBox.SelectedItem = viewModelToSelect;

                    _selectedProfile = profile;
                    UpdateProfileDetails(_selectedProfile);
                }

                StatusTextBlock.Text = isNew ? $"'{profile.Name}' created" : $"'{profile.Name}' updated";
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

        private static string GetApplySource(ApplySource source)
        {
            return source switch
            {
                ApplySource.Tray => "applied from tray",
                ApplySource.Hotkey => "applied by hotkey",
                ApplySource.CommandLine => "applied via CLI",
                ApplySource.Startup => "applied at startup",
                _ => "applied externally",
            };
        }

        private void OnProfileApplied(object sender, ProfileAppliedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var previouslySelectedId = _selectedProfile?.Id;

                bool appliedFromThisWindow = _isApplying;

                RefreshProfilesList();

                if (previouslySelectedId != null)
                {
                    var viewModelToSelect = _profileViewModels.FirstOrDefault(vm => vm.Id == previouslySelectedId);
                    if (viewModelToSelect != null)
                    {
                        _selectedProfile = viewModelToSelect.Profile;
                        ProfilesListBox.SelectedItem = viewModelToSelect;
                    }
                }

                if (!appliedFromThisWindow)
                {
                    string status = $"'{e.Profile.Name}' {GetApplySource(e.Source)} in {(e.DurationMilliseconds / 1000.0):0.0} seconds";
                    StatusTextBlock.Text = ProfileApplyFormatter.AppendApplyWarnings(status, e.WarningSummary);
                }
            });
        }

    }
}
