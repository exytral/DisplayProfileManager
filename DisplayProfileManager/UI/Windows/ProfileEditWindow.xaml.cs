using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Audio.Models;
using DisplayProfileManager.Common;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Scripts.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Displays;
using DisplayProfileManager.UI.Icons;
using DisplayProfileManager.UI.Interop;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.UI.ViewModels;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.UI.Windows
{
    #region ProfileEditWindow

    public partial class ProfileEditWindow : Window
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly ProfileEditorService _profileEditorService;
        private readonly SettingsService _settingsService;
        private readonly Func<IDisposable> _suspendHotkeysForCapture;
        private readonly Profile _profile;

        private readonly List<DisplaySettingControl> _displayControls;
        private CancellationTokenSource _audioLoadCts;
        private readonly ObservableCollection<AudioDeviceInfo> _playbackDevices;
        private readonly ObservableCollection<AudioDeviceInfo> _captureDevices;
        private readonly ObservableCollection<ScriptListEntry> _scriptList = [];

        private readonly bool _isEditMode;
        private string _pendingIconFilename;
        private bool _suppressAudioSelection;
        private bool _audioDevicesLoaded;
        private bool _audioSettingsDisabled;

        internal ProfileEditWindow(
            ProfileEditorService profileEditorService,
            SettingsService settingsService,
            Func<IDisposable> suspendHotkeysForCapture,
            Profile profileToEdit = null)
        {
            _profileEditorService = profileEditorService ?? throw new ArgumentNullException(nameof(profileEditorService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _suspendHotkeysForCapture = suspendHotkeysForCapture ?? throw new ArgumentNullException(nameof(suspendHotkeysForCapture));
            InitializeComponent();
            HotkeyEditor.CaptureScopeFactory = _suspendHotkeysForCapture;

            _displayControls = [];
            _isEditMode = profileToEdit != null;
            _profile = _profileEditorService.CreateWorkingDraft(profileToEdit);

            _playbackDevices = [];
            _captureDevices = [];

            OutputDeviceComboBox.ItemsSource = _playbackDevices;
            InputDeviceComboBox.ItemsSource = _captureDevices;

            InitializeWindow();

            if (!_isEditMode)
                PrefillNewProfile();

            UpdateScriptControlsState();
            UpdateAudioUiState();
        }

        private void PrefillNewProfile()
        {
            _profile.Name = _profileEditorService.GetUniqueProfileName("New Profile");
            ProfileNameTextBox.Text = _profile.Name;

            _ = PrefillDisplaySettingsAsync();
            _ = LoadAudioDevices();

            try
            {
                _profile.WallpaperSettings = _profileEditorService.CaptureWallpaper();
                _profile.WallpaperSettings.Enabled = false;

                _suppressWallpaperEvents = true;
                EnableWallpaperCheckBox.IsChecked = false;
                _suppressWallpaperEvents = false;

                PopulateWallpaperOptions();
                UpdateWallpaperModeIndicator();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not prefill new profile from current desktop");
            }
        }

        private async Task PrefillDisplaySettingsAsync()
        {
            try
            {
                var prefillWatch = Stopwatch.StartNew();
                var currentSettings = await _profileEditorService.CaptureCurrentDisplaySettingsAsync();
                if (currentSettings.Count == 0)
                {
                    _logger.Warn("Prefill found no displays -> leaving editor empty");
                    return;
                }

                LoadDisplaySettings(currentSettings);
                prefillWatch.Stop();
                _logger.Info($"Prefilled {TextFormat.Plural(currentSettings.Count, "display")} in {prefillWatch.ElapsedMilliseconds} ms");
            }
            catch (ProfileCaptureService.AdvancedColorCaptureUnavailableException ex)
            {
                StatusTextBlock.Text = "Advanced Color state unavailable - display prefill deferred";
                _logger.Warn(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not prefill display settings for a new profile");
            }
        }

        private void InitializeWindow()
        {
            ScriptsItemsControl.ItemsSource = _scriptList;
            HotkeyEditor.HotkeyChanged += ProfileHotkeyEditor_HotkeyChanged;

            if (_isEditMode)
            {
                TitleBarTextBlock.Text = "Edit Profile";
                Title = "Edit Profile";
                PopulateFields();
            }
            else
            {
                TitleBarTextBlock.Text = "Create New Profile";
                Title = "Create New Profile";
                _scriptList.Clear();
                _pendingIconFilename = null;
                RefreshIconPreview();
                _ = PopulateIconGridAsync();
                UpdateHotkeyControlsState();
            }
        }

        private void LoadDisplaySettings(List<DisplaySetting> settings)
        {
            DisplaySettingsPanel.Children.Clear();
            _displayControls.Clear();

            if (settings.Count == 0) return;

            var displayConfigs = _profileEditorService.GetLiveDisplays();
            foreach (var setting in settings)
            {
                var live = _profileEditorService.ResolveLiveDisplay(setting, displayConfigs);
                ReconcileAdvancedColorCapabilities(setting, live);
            }

            var displayGroups = DisplayGroupBuilder.GroupDisplaysForUI(settings);
            var cloneGroupCount = displayGroups.Count(g => g.IsCloneGroup);
            var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);

            foreach (var group in displayGroups)
            {
                AddDisplaySettingControl(
                    group.RepresentativeSetting,
                    isCloneGroup: group.IsCloneGroup,
                    cloneGroupMembers: group.AllMembers,
                    displayConfigs: displayConfigs);
            }

            if (cloneGroupCount > 0)
            {
                _logger.Info($"Loaded {TextFormat.Plural(settings.Count, "display")} with " + $"{TextFormat.Plural(cloneGroupCount, "clone group")} " + $"({cloneGroupDisplayCount} displays in clone groups)");
                StatusTextBlock.Text = $"Loaded {TextFormat.Plural(settings.Count, "display")} " + $"({TextFormat.Plural(cloneGroupCount, "clone group")} with " + $"{cloneGroupDisplayCount} displays)";
            }
            else
            {
                _logger.Info($"Loaded {TextFormat.Plural(settings.Count, "display")}");
                StatusTextBlock.Text = $"Loaded {TextFormat.Plural(settings.Count, "display")}";
            }
        }

        internal static void ReconcileAdvancedColorCapabilities(DisplaySetting setting, DisplayConfigInfo live)
        {
            if (setting == null || live == null || !live.IsAdvancedColorInfoAvailable) return;

            setting.IsHdrSupported = live.IsHdrSupported;
            setting.IsWcgSupported = live.IsWcgSupported;
            setting.LastKnownHdrSupported = live.IsHdrSupported;
            setting.LastKnownWcgSupported = live.IsWcgSupported;
        }

        private void PopulateFields()
        {
            ProfileNameTextBox.Text = _profile.Name;
            ProfileDescriptionTextBox.Text = _profile.Description;
            DefaultProfileCheckBox.IsChecked = _profileEditorService.GetDefaultProfile()?.Id == _profile.Id;
            _pendingIconFilename = _profile.Icon;
            RefreshIconPreview();
            _ = PopulateIconGridAsync();

            LoadDisplaySettings(_profile.DisplaySettings);

            if (_profile.HotkeyConfig != null)
            {
                HotkeyEditor.HotkeyConfig = _profile.HotkeyConfig.Clone();
                EnableHotkeyCheckBox.IsChecked = _profile.HotkeyConfig.IsEnabled;
            }
            else
            {
                HotkeyEditor.HotkeyConfig = new HotkeyConfig();
                EnableHotkeyCheckBox.IsChecked = false;
            }

            CheckForHotkeyConflicts();

            _suppressAudioSelection = true;
            try
            {
                EnableAudioCheckBox.IsChecked = _profile.AudioSettings?.Enabled == true;
            }
            finally
            {
                _suppressAudioSelection = false;
            }

            _ = LoadAudioDevices();

            EnableWallpaperCheckBox.IsChecked = _profile.WallpaperSettings?.Enabled == true;
            PopulateWallpaperOptions();
            UpdateWallpaperModeIndicator();

            EnableScriptsCheckBox.IsChecked = _profile.ScriptSettings?.Enabled == true;
            UpdateClearIconButtonState();

            _scriptList.Clear();
            if (_profile.ScriptSettings?.Scripts != null)
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                foreach (var script in _profile.ScriptSettings.Scripts)
                {
                    string fullPath = System.IO.Path.IsPathRooted(script.FileName)
                        ? script.FileName
                        : System.IO.Path.Combine(scriptsFolder, script.FileName);
                    bool fileExists = System.IO.File.Exists(fullPath);
                    _scriptList.Add(new ScriptListEntry
                    {
                        FilePath = fullPath,
                        FileName = fileExists
                            ? System.IO.Path.GetFileName(fullPath)
                            : $"{System.IO.Path.GetFileName(fullPath)} (Not Found)",
                        Arguments = script.Arguments ?? string.Empty,
                        IsEnabled = script.IsEnabled,
                        IsDeleted = false
                    });
                }
            }

            ScriptsItemsControl.ItemsSource = _scriptList;
            UpdateScriptsVisibility();
            UpdateScriptControlsState();
            UpdateHotkeyControlsState();
        }

        private async void LoadDisplaysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusTextBlock.Text = "Loading current display settings...";
                LoadDisplaysButton.IsEnabled = false;

                var currentSettings = await _profileEditorService.CaptureCurrentDisplaySettingsAsync();
                LoadDisplaySettings(currentSettings);
                _logger.Info($"Load: {currentSettings.Count} physical displays loaded, " + $"{_displayControls.Count} controls created");
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error loading displays";
                MessageBox.Show($"Error loading current display settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadDisplaysButton.IsEnabled = true;
            }
        }

        private void AddDisplaySettingControl(DisplaySetting setting, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayConfigInfo> displayConfigs = null)
        {
            if (DisplaySettingsPanel.Children.Count == 1 && DisplaySettingsPanel.Children[0] is TextBlock)
                DisplaySettingsPanel.Children.Clear();

            var control = new DisplaySettingControl(_profileEditorService, setting, isCloneGroup, cloneGroupMembers, displayConfigs)
            {
                OnCloneGroupChanged = RebuildDisplayControls
            };
            _displayControls.Add(control);
            DisplaySettingsPanel.Children.Add(control);
        }

        private void RebuildDisplayControls()
        {
            // Capture original device order before regrouping clone members
            var deviceOrder = _profile.DisplaySettings
                .Select(s => s.DeviceName)
                .Distinct()
                .Select((name, idx) => (name, idx))
                .ToDictionary(x => x.name, x => x.idx);

            _profile.DisplaySettings.Clear();
            _profile.DisplaySettings.AddRange(_displayControls.SelectMany(c => c.GetDisplaySettings()).OrderBy(s => deviceOrder.TryGetValue(s.DeviceName, out var i) ? i : int.MaxValue));

            LoadDisplaySettings(_profile.DisplaySettings);
        }

        private async void IdentifyDisplaysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusTextBlock.Text = "Identifying monitors...";
                IdentifyDisplaysButton.IsEnabled = false;

                List<DisplaySetting> displaySettings = [];

                // Prefer current control state; fall back to live query if no controls loaded
                if (_displayControls.Count > 0)
                {
                    displaySettings = _profile.DisplaySettings;

                    if (displaySettings.Count == 0)
                    {
                        foreach (var control in _displayControls)
                        {
                            var settings = control.GetDisplaySettings();
                            foreach (var setting in settings)
                                displaySettings.Add(setting);
                        }
                    }
                }
                else
                    displaySettings = await _profileEditorService.CaptureCurrentDisplaySettingsAsync();

                var identifyWindows = new List<MonitorIdentifyWindow>();

                int index = 1;
                foreach (var setting in displaySettings)
                {
                    if (setting.IsEnabled)
                    {
                        if (_profileEditorService.IsMonitorConnected(setting.DeviceName))
                        {
                            if (_profileEditorService.TryGetMonitorBounds(setting.DeviceName, out var bounds))
                            {
                                var identifyWindow = new MonitorIdentifyWindow(index, bounds.Left, bounds.Top);
                                identifyWindows.Add(identifyWindow);
                            }
                        }
                    }
                    index++;
                }

                foreach (var window in identifyWindows)
                {
                    window.Show();
                    _logger.Debug("Showing identify window for monitor {Index} at position Left:{Left}, Top:{Top}", window.MonitorIndex, window.Left, window.Top);
                }

                StatusTextBlock.Text = $"Showing identifiers on {TextFormat.Plural(identifyWindows.Count, "monitor")}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error identifying displays";
                MessageBox.Show($"Error identifying displays: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IdentifyDisplaysButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateInput()) return;

                SaveButton.IsEnabled = false;
                StatusTextBlock.Text = "Saving profile...";

                // Info
                _profile.Name = ProfileNameTextBox.Text.Trim();
                _profile.Description = ProfileDescriptionTextBox.Text.Trim();
                _profile.Icon = _pendingIconFilename;

                // Hotkey
                _profile.HotkeyConfig = HotkeyEditor.HotkeyConfig?.Clone() ?? new HotkeyConfig();
                bool hotkeyAssigned = _profile.HotkeyConfig.Key != Key.None;
                _profile.HotkeyConfig.IsEnabled = (EnableHotkeyCheckBox.IsChecked ?? false) && hotkeyAssigned;

                bool wasDefault = _profileEditorService.GetDefaultProfile()?.Id == _profile.Id;
                bool shouldBeDefault = DefaultProfileCheckBox.IsChecked == true;

                // Displays
                _profile.DisplaySettings.Clear();
                foreach (var control in _displayControls)
                {
                    var settings = control.GetDisplaySettings();
                    foreach (var setting in settings)
                        _profile.DisplaySettings.Add(setting);
                }

                // Wallpaper
                bool wallpaperEnabled = EnableWallpaperCheckBox.IsChecked ?? false;
                if (wallpaperEnabled)
                {
                    try
                    {
                        _profile.WallpaperSettings ??= _profileEditorService.CaptureWallpaper();

                        ApplyWallpaperOptionsToSnapshot();
                        _profile.WallpaperSettings.Enabled = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error capturing wallpaper");
                        StatusTextBlock.Text = "Error capturing wallpaper — profile saved without it";
                        _profile.WallpaperSettings = null;
                    }
                }
                else
                    _profile.WallpaperSettings?.Enabled = false;

                UpdateWallpaperModeIndicator();

                // Audio
                _profile.AudioSettings ??= new AudioSetting();
                _profile.AudioSettings.ApplyPlaybackDevice = ApplyOutputDeviceCheckBox.IsChecked ?? false;
                _profile.AudioSettings.ApplyCaptureDevice = ApplyInputDeviceCheckBox.IsChecked ?? false;
                _profile.AudioSettings.Enabled = (EnableAudioCheckBox.IsChecked ?? false)
                    && (_profile.AudioSettings.ApplyPlaybackDevice || _profile.AudioSettings.ApplyCaptureDevice);

                if (OutputDeviceComboBox.SelectedItem is AudioDeviceInfo selectedOutput)
                {
                    _profile.AudioSettings.DefaultPlaybackDeviceId = selectedOutput.Id;
                    _profile.AudioSettings.PlaybackDeviceName = selectedOutput.SystemName;
                }

                if (InputDeviceComboBox.SelectedItem is AudioDeviceInfo selectedInput)
                {
                    _profile.AudioSettings.DefaultCaptureDeviceId = selectedInput.Id;
                    _profile.AudioSettings.CaptureDeviceName = selectedInput.SystemName;
                }

                // Scripts
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                if (!System.IO.Directory.Exists(scriptsFolder))
                    System.IO.Directory.CreateDirectory(scriptsFolder);

                _profile.ScriptSettings ??= new ScriptSettings();

                _profile.ScriptSettings.Scripts = [.. _scriptList
                    .Where(s => !s.IsDeleted && !string.IsNullOrWhiteSpace(s.FilePath))
                    .Select(s => new Script
                    {
                        FileName = System.IO.Path.GetFileName(s.FilePath),
                        Arguments = s.Arguments?.Trim() ?? string.Empty,
                        IsEnabled = s.IsEnabled
                    })];

                _profile.ScriptSettings.Enabled = (EnableScriptsCheckBox.IsChecked ?? false)
                    && _profile.ScriptSettings.Scripts.Any(s => s.IsEnabled);

                bool success = await _profileEditorService.SaveAsync(_profile, !_isEditMode);
                if (success)
                {
                    bool defaultUpdated = true;
                    if (shouldBeDefault && !wasDefault)
                        defaultUpdated = await _profileEditorService.SetDefaultAsync(_profile.Id);
                    else if (!shouldBeDefault && wasDefault)
                        defaultUpdated = await _profileEditorService.SetDefaultAsync(null);

                    if (!defaultUpdated)
                    {
                        StatusTextBlock.Text = "Profile saved, but failed to update default profile";
                        MessageBox.Show(
                            "The profile was saved, but the default profile setting could not be updated.",
                            "Profile Saved",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusTextBlock.Text = "Failed to save profile";
                    MessageBox.Show("Failed to save profile. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error saving profile";
                MessageBox.Show($"Error saving profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(ProfileNameTextBox.Text))
            {
                MessageBox.Show("Please enter a profile name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProfileNameTextBox.Focus();
                return false;
            }

            // Reject duplicate names (case-insensitive, excluding current profile in edit mode)
            var trimmedName = ProfileNameTextBox.Text.Trim();
            if (!_isEditMode || !trimmedName.Equals(_profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (_profileEditorService.HasProfile(trimmedName))
                {
                    MessageBox.Show("A profile with this name already exists. Please choose a different name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProfileNameTextBox.Focus();
                    return false;
                }
            }

            CheckForHotkeyConflicts();
            if (HotkeyEditor.HasConflict)
            {
                MessageBox.Show("This hotkey is already configured for another action. Choose a different key combination.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                HotkeyEditor.Focus();
                return false;
            }

            if (_displayControls.Count == 0)
            {
                MessageBox.Show("Please add at least one display setting.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            foreach (var control in _displayControls)
            {
                if (!control.ValidateInput())
                {
                    return false;
                }
            }

            if (ApplyOutputDeviceCheckBox.IsChecked == true && OutputDeviceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an audio output device.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ApplyOutputDeviceCheckBox.Focus();
                return false;
            }

            if (ApplyInputDeviceCheckBox.IsChecked == true && InputDeviceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an audio input device.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ApplyInputDeviceCheckBox.Focus();
                return false;
            }

            return true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
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

        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            TitleBarLayout.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
            base.OnStateChanged(e);
        }

        private void RefreshIconPreview(bool refresh = false)
        {
            UpdateClearIconButtonState();
            if (string.IsNullOrWhiteSpace(_pendingIconFilename))
            {
                IconPreviewImage.Source = null;
                IconFilenameTextBlock.Text = "No icon";
                if (refresh)
                    StatusTextBlock.Text = "Icon cleared";
            }
            else
            {
                IconPreviewImage.Source = ProfileIconProvider.LoadImageSource(_pendingIconFilename);
                IconFilenameTextBlock.Text = _pendingIconFilename;
                if (refresh)
                    StatusTextBlock.Text = $"Icon set to '{_pendingIconFilename}'";
            }
        }

        private async Task PopulateIconGridAsync()
        {
            var icons = await Task.Run(() => _profileEditorService.GetAvailableIcons());

            BuiltinIconsPanel.Children.Clear();

            BuiltinIconsPanel.HorizontalAlignment = HorizontalAlignment.Left;

            foreach (string filename in icons)
            {
                var src = await Task.Run(() => ProfileIconProvider.LoadImageSource(filename, 32));
                if (src == null) continue;

                src.Freeze();

                var btn = new ToggleButton
                {
                    Width = 42,
                    Height = 42,
                    Margin = new Thickness(3),
                    Tag = filename,
                    IsChecked = filename == _pendingIconFilename,
                    ToolTip = filename,
                    Cursor = Cursors.Hand,
                    Style = (Style)FindResource("IconTileStyle")
                };
                var imgAsync = new Image
                {
                    Source = src,
                    Width = 32,
                    Height = 32,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(imgAsync, BitmapScalingMode.HighQuality);
                btn.Content = imgAsync;
                btn.Checked += IconButton_Checked;
                BuiltinIconsPanel.Children.Add(btn);
            }

            NoBuiltinIconsTextBlock.Visibility = BuiltinIconsPanel.Children.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SyncIconSelection()
        {
            foreach (var child in BuiltinIconsPanel.Children)
                if (child is ToggleButton btn)
                    btn.IsChecked = (btn.Tag as string) == _pendingIconFilename;
        }

        private void IconButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                _pendingIconFilename = btn.Tag as string;
                RefreshIconPreview(true);
                SyncIconSelection();
            }
        }

        private async void ImportIconButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Profile Icon",
                Filter = "Icon (*.ico)|*.ico",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            ImportIconButton.IsEnabled = false;
            StatusTextBlock.Text = "Importing icon...";
            try
            {
                _pendingIconFilename = await _profileEditorService.ImportIconAsync(dlg.FileName);
                await PopulateIconGridAsync();
                RefreshIconPreview(true);
                SyncIconSelection();
            }
            catch (InvalidOperationException ex)
            {
                StatusTextBlock.Text = "Import failed";
                MessageBox.Show(ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Import failed";
                MessageBox.Show($"Error importing icon:\n{ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportIconButton.IsEnabled = true;
            }
        }

        private void UpdateClearIconButtonState()
        {
            ClearIconButton.IsEnabled = !string.IsNullOrWhiteSpace(_pendingIconFilename);
        }

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingIconFilename = null;
            RefreshIconPreview(true);
            SyncIconSelection();
        }

        private static void SyncDeviceList(ObservableCollection<AudioDeviceInfo> target, IEnumerable<AudioDeviceInfo> source)
        {
            var incoming = source.ToList();

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!incoming.Any(d => d.Id == target[i].Id))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < incoming.Count; i++)
            {
                var existing = target.FirstOrDefault(d => d.Id == incoming[i].Id);

                if (existing == null)
                {
                    target.Insert(Math.Min(i, target.Count), incoming[i]);
                    continue;
                }

                existing.Name = incoming[i].Name;
                existing.SystemName = incoming[i].SystemName;
                existing.IsActive = incoming[i].IsActive;
                existing.IsAvailable = incoming[i].IsAvailable;
                existing.Type = incoming[i].Type;

                int current = target.IndexOf(existing);
                if (current != i)
                    target.Move(current, i);
            }
        }

        private static void PreserveUnavailableDevice(ObservableCollection<AudioDeviceInfo> target, AudioDeviceInfo device)
        {
            if (device == null || device.IsAvailable || string.IsNullOrEmpty(device.Id)) return;
            if (target.Any(d => d.Id == device.Id)) return;

            target.Add(device);
        }

        private static AudioDeviceInfo EnsureUnavailableDevice(ObservableCollection<AudioDeviceInfo> target, string deviceId, string deviceName, DeviceType type)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return null;
            }

            var existing = target.FirstOrDefault(d => d.Id == deviceId);
            if (existing != null)
            {
                return existing;
            }

            var unavailable = new AudioDeviceInfo
            {
                Id = deviceId,
                Name = deviceName,
                SystemName = deviceName,
                IsActive = false,
                IsAvailable = false,
                Type = type
            };
            target.Add(unavailable);
            return unavailable;
        }

        private async Task LoadAudioDevices()
        {
            _audioLoadCts?.Cancel();
            _audioLoadCts = new CancellationTokenSource();
            var token = _audioLoadCts.Token;
            bool initializeFromProfile = !_audioDevicesLoaded;

            try
            {
                _suppressAudioSelection = true;

                var playbackDevices = await Task.Run(() => _profileEditorService.GetPlaybackDevices(), token);
                token.ThrowIfCancellationRequested();

                var captureDevices = await Task.Run(() => _profileEditorService.GetCaptureDevices(), token);
                token.ThrowIfCancellationRequested();

                string currentPlaybackId = (OutputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
                string currentCaptureId = (InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
                var currentPlayback = OutputDeviceComboBox.SelectedItem as AudioDeviceInfo;
                var currentCapture = InputDeviceComboBox.SelectedItem as AudioDeviceInfo;

                SyncDeviceList(_playbackDevices, playbackDevices);
                SyncDeviceList(_captureDevices, captureDevices);
                PreserveUnavailableDevice(_playbackDevices, currentPlayback);
                PreserveUnavailableDevice(_captureDevices, currentCapture);
                _audioDevicesLoaded = true;

                if (initializeFromProfile)
                {
                    if (_isEditMode && _profile.AudioSettings != null)
                    {
                        ApplyOutputDeviceCheckBox.IsChecked = _profile.AudioSettings.ApplyPlaybackDevice;
                        ApplyInputDeviceCheckBox.IsChecked = _profile.AudioSettings.ApplyCaptureDevice;
                        OutputDeviceComboBox.Opacity = _profile.AudioSettings.ApplyPlaybackDevice ? 1.0 : UiOpacity.Inactive;
                        InputDeviceComboBox.Opacity = _profile.AudioSettings.ApplyCaptureDevice ? 1.0 : UiOpacity.Inactive;

                        if (!string.IsNullOrEmpty(_profile.AudioSettings.DefaultPlaybackDeviceId))
                        {
                            var savedPlayback = _playbackDevices.FirstOrDefault(d => d.Id == _profile.AudioSettings.DefaultPlaybackDeviceId);
                            if (savedPlayback != null)
                                OutputDeviceComboBox.SelectedItem = savedPlayback;
                            else
                            {
                                var unavailablePlayback = EnsureUnavailableDevice(
                                    _playbackDevices,
                                    _profile.AudioSettings.DefaultPlaybackDeviceId,
                                    _profile.AudioSettings.PlaybackDeviceName,
                                    DeviceType.Playback);
                                OutputDeviceComboBox.SelectedItem = unavailablePlayback;
                            }
                        }
                        else
                            await SelectDefaultPlaybackDeviceAsync();

                        if (!string.IsNullOrEmpty(_profile.AudioSettings.DefaultCaptureDeviceId))
                        {
                            var savedCapture = _captureDevices.FirstOrDefault(d => d.Id == _profile.AudioSettings.DefaultCaptureDeviceId);
                            if (savedCapture != null)
                                InputDeviceComboBox.SelectedItem = savedCapture;
                            else
                            {
                                var unavailableCapture = EnsureUnavailableDevice(_captureDevices,
                                    _profile.AudioSettings.DefaultCaptureDeviceId,
                                    _profile.AudioSettings.CaptureDeviceName,
                                    DeviceType.Capture);
                                InputDeviceComboBox.SelectedItem = unavailableCapture;
                            }
                        }
                        else
                            await SelectDefaultCaptureDeviceAsync();
                    }
                    else
                    {
                        await SelectDefaultPlaybackDeviceAsync();
                        await SelectDefaultCaptureDeviceAsync();
                    }
                }
                else
                {
                    currentPlayback = !string.IsNullOrEmpty(currentPlaybackId)
                        ? _playbackDevices.FirstOrDefault(d => d.Id == currentPlaybackId)
                        : currentPlayback;
                    if (currentPlayback != null)
                        OutputDeviceComboBox.SelectedItem = currentPlayback;
                    else
                        await SelectDefaultPlaybackDeviceAsync();

                    currentCapture = !string.IsNullOrEmpty(currentCaptureId)
                        ? _captureDevices.FirstOrDefault(d => d.Id == currentCaptureId)
                        : currentCapture;
                    if (currentCapture != null)
                        InputDeviceComboBox.SelectedItem = currentCapture;
                    else
                        await SelectDefaultCaptureDeviceAsync();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug("Audio device load canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading audio devices");
                StatusTextBlock.Text = "Could not load audio devices";
            }
            finally
            {
                _suppressAudioSelection = false;
                UpdateAudioUiState();
            }
        }

        private async Task SelectDefaultPlaybackDeviceAsync()
        {
            var defaultPlayback = await Task.Run(() => _profileEditorService.GetDefaultPlaybackDevice());
            if (defaultPlayback != null)
            {
                var deviceInList = _playbackDevices.FirstOrDefault(d => d.Id == defaultPlayback.Id);
                if (deviceInList != null)
                    OutputDeviceComboBox.SelectedItem = deviceInList;
                else if (_playbackDevices.Count > 0)
                    OutputDeviceComboBox.SelectedIndex = 0;
            }
            else if (_playbackDevices.Count > 0)
                OutputDeviceComboBox.SelectedIndex = 0;
        }

        private async Task SelectDefaultCaptureDeviceAsync()
        {
            var defaultCapture = await Task.Run(() => _profileEditorService.GetDefaultCaptureDevice());
            if (defaultCapture != null)
            {
                var deviceInList = _captureDevices.FirstOrDefault(d => d.Id == defaultCapture.Id);
                if (deviceInList != null)
                    InputDeviceComboBox.SelectedItem = deviceInList;
                else if (_captureDevices.Count > 0)
                    InputDeviceComboBox.SelectedIndex = 0;
            }
            else if (_captureDevices.Count > 0)
                InputDeviceComboBox.SelectedIndex = 0;
        }

        private async void AudioDeviceComboBox_DropDownOpened(object sender, EventArgs e)
        {
            try
            {
                await LoadAudioDevices();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing audio devices");
                StatusTextBlock.Text = "Error refreshing audio devices";
            }
        }

        private void LoadWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _profile.WallpaperSettings = _profileEditorService.CaptureWallpaper();
                _profile.WallpaperSettings.Enabled = true;
                EnableWallpaperCheckBox.IsChecked = true;
                PopulateWallpaperOptions();
                UpdateWallpaperModeIndicator();
                StatusTextBlock.Text = $"Current wallpaper captured ({WallpaperModeFormatter.Display(_profile.WallpaperSettings.Mode)})";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error capturing wallpaper");
                StatusTextBlock.Text = "Error capturing wallpaper";
            }
        }

        private static readonly uint[] _slideshowIntervals = [60, 600, 1800, 3600, 21600, 86400];

        private bool _suppressWallpaperEvents;
        private List<uint> _intervalOptions = [];

        private void PopulateWallpaperOptions()
        {
            _suppressWallpaperEvents = true;
            try
            {
                var snapshot = _profile.WallpaperSettings;

                var argb = snapshot?.SolidColorArgb ?? 0;

                var backgroundBrush = new SolidColorBrush(Color.FromRgb((byte)argb, (byte)(argb >> 8), (byte)(argb >> 16)));
                backgroundBrush.Freeze();

                WallpaperColorSwatch.Background = backgroundBrush;

                WallpaperFitmentComboBox.ItemsSource = WallpaperModeFormatter.AllPositions.Select(p => new FitmentOption(char.ToUpper(p[0]) + p[1..], backgroundBrush)).ToList();

                var intervals = _slideshowIntervals.ToList();
                var captured = snapshot?.SlideshowConfig?.IntervalSeconds ?? 1800;
                if (!intervals.Contains(captured)) intervals.Add(captured);
                intervals.Sort();
                _intervalOptions = intervals;
                WallpaperIntervalComboBox.ItemsSource = intervals.Select(DescribeInterval).ToList();

                var position = WallpaperModeFormatter.NormalizePosition(snapshot?.Position);
                WallpaperFitmentComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(WallpaperModeFormatter.AllPositions, position));

                WallpaperIntervalComboBox.SelectedIndex = intervals.IndexOf(captured);

                WallpaperShuffleCheckBox.IsChecked = snapshot?.SlideshowConfig?.Shuffle ?? false;
            }
            finally
            {
                _suppressWallpaperEvents = false;
            }

            UpdateWallpaperPreview();
            UpdateWallpaperOptionsEnabled();
        }

        private void UpdateWallpaperPreview()
        {
            WallpaperPreviewImage.Source = null;
            WallpaperPreviewSolid.Visibility = Visibility.Collapsed;
            WallpaperPreviewEmptyText.Visibility = Visibility.Visible;

            var snapshot = _profile.WallpaperSettings;

            if (snapshot != null && snapshot.Mode == WallpaperMode.Solid)
            {
                var argb = snapshot.SolidColorArgb;
                WallpaperPreviewSolid.Background = new SolidColorBrush(
                    Color.FromRgb((byte)argb, (byte)(argb >> 8), (byte)(argb >> 16)));
                WallpaperPreviewSolid.Visibility = Visibility.Visible;
                WallpaperPreviewEmptyText.Visibility = Visibility.Collapsed;
                return;
            }

            var path = WallpaperPreviewProvider.GetSnapshotPreviewPath(snapshot);
            if (path == null) return;

            try
            {
                // Load bitmap into memory so WPF does not retain source file
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                using (var stream = System.IO.File.OpenRead(path))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 192;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                WallpaperPreviewImage.Source = bitmap;
                WallpaperPreviewEmptyText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Wallpaper preview could not be decoded");
            }
        }

        private static string DescribeInterval(uint seconds)
        {
            if (seconds < 60)
            {
                return TextFormat.Plural(seconds, "second");
            }
            if (seconds < 3600)
            {
                return TextFormat.Plural(seconds / 60, "minute");
            }
            if (seconds < 86400)
            {
                return TextFormat.Plural(seconds / 3600, "hour");
            }
            return "1 day";
        }

        private static void SetSectionState(UIElement panel, UIElement control, bool applicable, bool active)
        {
            control.IsEnabled = applicable;
            panel.Opacity = !applicable ? UiOpacity.Blocked : (active ? 1.0 : UiOpacity.Inactive);
        }

        private static bool IsLetterboxFitment(string fit) => string.Equals(fit, "fit", StringComparison.OrdinalIgnoreCase) || string.Equals(fit, "center", StringComparison.OrdinalIgnoreCase);

        private sealed class FitmentOption(string name, Brush background)
        {
            public string Name { get; } = name;
            public Brush Background { get; } = background;
            public Visibility ShowSwatch { get; } = IsLetterboxFitment(name) ? Visibility.Visible : Visibility.Collapsed;

            public override string ToString() => Name;
        }

        internal static (bool HasFitment, bool HasColor, bool HasSlideshow) GetWallpaperOptionApplicability(WallpaperMode? mode)
        {
            bool hasSlideshow = mode == WallpaperMode.Slideshow;
            return (
                HasFitment: mode == WallpaperMode.Picture || hasSlideshow,
                HasColor: mode == WallpaperMode.Solid || mode == WallpaperMode.Picture || hasSlideshow,
                HasSlideshow: hasSlideshow);
        }

        private void UpdateWallpaperOptionsEnabled()
        {
            bool enabled = EnableWallpaperCheckBox.IsChecked == true;
            var (hasFitment, hasColor, hasSlideshow) = GetWallpaperOptionApplicability(_profile.WallpaperSettings?.Mode);

            SetSectionState(WallpaperFitmentPanel, WallpaperFitmentComboBox, hasFitment, enabled);
            SetSectionState(WallpaperIntervalPanel, WallpaperIntervalComboBox, hasSlideshow, enabled);
            SetSectionState(WallpaperShufflePanel, WallpaperShuffleCheckBox, hasSlideshow, enabled);
            SetSectionState(WallpaperSourcePanel, WallpaperSourceButton, hasSlideshow, enabled);
            SetSectionState(WallpaperColorPanel, WallpaperColorButton, hasColor, enabled);
        }

        private void ApplyWallpaperOptionsToSnapshot()
        {
            var snapshot = _profile.WallpaperSettings;
            if (snapshot == null) return;

            int fitment = WallpaperFitmentComboBox.SelectedIndex;
            if (fitment >= 0 && fitment < WallpaperModeFormatter.AllPositions.Length)
                snapshot.Position = WallpaperModeFormatter.AllPositions[fitment];

            if (snapshot.Mode != WallpaperMode.Slideshow) return;

            int interval = WallpaperIntervalComboBox.SelectedIndex;
            var config = EnsureSlideshowConfig();
            if (interval >= 0 && interval < _intervalOptions.Count)
                config.IntervalSeconds = _intervalOptions[interval];

            config.Shuffle = WallpaperShuffleCheckBox.IsChecked == true;
        }

        private void EnableWallpaperCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressWallpaperEvents) return;

            if (EnableWallpaperCheckBox.IsChecked == true && _profile.WallpaperSettings == null)
            {
                try
                {
                    _profile.WallpaperSettings = _profileEditorService.CaptureWallpaper();
                    PopulateWallpaperOptions();
                    UpdateWallpaperModeIndicator();
                    StatusTextBlock.Text = $"Current wallpaper captured ({WallpaperModeFormatter.Display(_profile.WallpaperSettings.Mode)})";
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Wallpaper capture on enable failed");
                }
            }

            UpdateWallpaperOptionsEnabled();
        }

        private void WallpaperColorButton_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = _profile.WallpaperSettings;
            if (snapshot == null) return;

            var argb = snapshot.SolidColorArgb;
            uint initialColor = (uint)((byte)argb | ((byte)(argb >> 8) << 8) | ((byte)(argb >> 16) << 16));
            if (!NativeColorDialog.TryChooseColor(new WindowInteropHelper(this).Handle, initialColor, out uint selectedColor)) return;

            snapshot.SolidColorArgb = selectedColor;
            _logger.Debug($"Background color set to COLORREF 0x{snapshot.SolidColorArgb:X6}");
            PopulateWallpaperOptions();
            UpdateWallpaperPreview();
            StatusTextBlock.Text = "Background color set";
        }

        private void WallpaperSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = _profile.WallpaperSettings;
            if (snapshot?.SlideshowConfig == null) return;

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose slideshow folder",
                InitialDirectory = snapshot.SlideshowConfig.SourcePaths.FirstOrDefault() ?? string.Empty
            };

            if (dialog.ShowDialog(this) != true) return;

            snapshot.SlideshowConfig.SourcePaths = [dialog.FolderName];
            UpdateWallpaperPreview();
            StatusTextBlock.Text = $"Slideshow source set to {dialog.FolderName}";
        }

        private void WallpaperFitmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressWallpaperEvents || _profile.WallpaperSettings == null) return;

            int i = WallpaperFitmentComboBox.SelectedIndex;
            if (i < 0 || i >= WallpaperModeFormatter.AllPositions.Length) return;

            _profile.WallpaperSettings.Position = WallpaperModeFormatter.AllPositions[i];
        }

        private void WallpaperIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressWallpaperEvents || _profile.WallpaperSettings == null) return;

            int i = WallpaperIntervalComboBox.SelectedIndex;
            if (i < 0 || i >= _intervalOptions.Count) return;

            EnsureSlideshowConfig().IntervalSeconds = _intervalOptions[i];
        }

        private void WallpaperShuffleCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressWallpaperEvents || _profile.WallpaperSettings == null) return;

            EnsureSlideshowConfig().Shuffle = WallpaperShuffleCheckBox.IsChecked == true;
        }

        private SlideshowConfig EnsureSlideshowConfig()
        {
            if (_profile.WallpaperSettings.SlideshowConfig == null)
                _profile.WallpaperSettings.SlideshowConfig = new SlideshowConfig();

            return _profile.WallpaperSettings.SlideshowConfig;
        }

        private void UpdateWallpaperModeIndicator()
        {
            var mode = _profile.WallpaperSettings?.Mode;
            WallpaperModeTextBlock.Text = mode == null || mode == WallpaperMode.Unknown ? "Wallpaper" : WallpaperModeFormatter.Display(mode.Value);
        }

        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAudioSelection) return;

            if (OutputDeviceComboBox.SelectedItem is AudioDeviceInfo device)
                if (!string.IsNullOrEmpty(device.Id))
                    StatusTextBlock.Text = $"Output device: {device.SystemName}";
        }

        private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAudioSelection) return;

            if (InputDeviceComboBox.SelectedItem is AudioDeviceInfo device)
                if (!string.IsNullOrEmpty(device.Id))
                    StatusTextBlock.Text = $"Input device: {device.SystemName}";
        }

        private void ApplyOutputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Output device enabled";
            if (!_suppressAudioSelection)
                EnableAudioSectionForFirstDevice();
        }

        private void ApplyOutputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Output device disabled";
            UpdateAudioUiState();
        }

        private void ApplyInputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Input device enabled";
            if (!_suppressAudioSelection)
                EnableAudioSectionForFirstDevice();
        }

        private void ApplyInputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Input device disabled";
            UpdateAudioUiState();
        }

        private void EnableAudioSectionForFirstDevice()
        {
            bool onlyOne = (ApplyOutputDeviceCheckBox.IsChecked == true) ^ (ApplyInputDeviceCheckBox.IsChecked == true);
            if (onlyOne && !_audioSettingsDisabled)
                EnableAudioCheckBox.IsChecked = true;

            UpdateAudioUiState();
        }

        private void EnableAudioCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_suppressAudioSelection)
                _audioSettingsDisabled = EnableAudioCheckBox.IsChecked == false;

            UpdateAudioUiState();
        }

        private void UpdateAudioUiState()
        {
            bool noDevices = _audioDevicesLoaded && !_playbackDevices.Any() && !_captureDevices.Any();
            bool sectionOn = EnableAudioCheckBox.IsChecked == true;
            bool outputOn = ApplyOutputDeviceCheckBox.IsChecked == true;
            bool inputOn = ApplyInputDeviceCheckBox.IsChecked == true;
            bool outputUnavailable = OutputDeviceComboBox.SelectedItem is AudioDeviceInfo { IsAvailable: false };
            bool inputUnavailable = InputDeviceComboBox.SelectedItem is AudioDeviceInfo { IsAvailable: false };

            EnableAudioCheckBox.IsEnabled = !noDevices;

            EnableAudioCheckBox.Opacity = noDevices || !(outputOn || inputOn)
                ? UiOpacity.Blocked
                : (sectionOn ? 1.0 : UiOpacity.Inactive);

            ApplyOutputDeviceCheckBox.Opacity = noDevices ? UiOpacity.Blocked : (sectionOn ? 1.0 : UiOpacity.Inactive);
            ApplyInputDeviceCheckBox.Opacity = noDevices ? UiOpacity.Blocked : (sectionOn ? 1.0 : UiOpacity.Inactive);

            OutputDeviceComboBox.Opacity = outputUnavailable || noDevices || !outputOn
                ? UiOpacity.Blocked
                : (sectionOn ? 1.0 : UiOpacity.Inactive);
            InputDeviceComboBox.Opacity = inputUnavailable || noDevices || !inputOn
                ? UiOpacity.Blocked
                : (sectionOn ? 1.0 : UiOpacity.Inactive);
        }

        private async void AddScriptButton_Click(object sender, RoutedEventArgs e)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profileManagerPath = System.IO.Path.Combine(appDataPath, "DisplayProfileManager");
            string scriptsPath = System.IO.Path.Combine(profileManagerPath, "Scripts");

            if (!System.IO.Directory.Exists(scriptsPath))
            {
                try {
                    System.IO.Directory.CreateDirectory(scriptsPath);
                }
                catch {
                    scriptsPath = profileManagerPath;
                }
            }

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                InitialDirectory = scriptsPath,
                Filter = "Scripts (*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk)|*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk|All files (*.*)|*.*",
                Title = "Import Script",
                DereferenceLinks = false,
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() != true) return;

            int existingCount = _scriptList.Count;
            var batch = await _profileEditorService.ImportScriptsAsync(openFileDialog.FileNames);
            string firstImportedFileName = null;

            foreach (var result in batch.Results)
            {
                if (!result.Success) continue;

                firstImportedFileName ??= result.ImportedFileName;
                string fullPath = System.IO.Path.Combine(scriptsPath, result.ImportedFileName);
                _scriptList.Add(new ScriptListEntry
                {
                    FilePath = fullPath,
                    FileName = System.IO.Path.GetFileName(fullPath),
                    Arguments = string.Empty,
                    IsEnabled = true,
                    IsDeleted = false
                });
            }

            if (existingCount == 0 && batch.ImportedCount > 0)
                EnableScriptsCheckBox.IsChecked = true;

            UpdateScriptsVisibility();
            UpdateScriptControlsState();

            if (batch.FailedCount == 0)
            {
                StatusTextBlock.Text = batch.ImportedCount == 1
                    ? $"'{firstImportedFileName}' added"
                    : $"{TextFormat.Plural(batch.ImportedCount, "script")} added";
                return;
            }

            var failedNames = batch.Results
                .Where(result => !result.Success)
                .Select(result => $"- {System.IO.Path.GetFileName(result.SourcePath)}");
            string failedList = string.Join(Environment.NewLine, failedNames);

            StatusTextBlock.Text = batch.ImportedCount == 0
                ? $"Failed to import {TextFormat.Plural(batch.FailedCount, "script")}"
                : $"{TextFormat.Plural(batch.ImportedCount, "script")} added; {TextFormat.Plural(batch.FailedCount, "script")} failed";

            string message = batch.ImportedCount == 0
                ? $"No scripts were imported.{Environment.NewLine}{Environment.NewLine}Failed:{Environment.NewLine}{failedList}"
                : $"{TextFormat.Plural(batch.ImportedCount, "script")} imported successfully.{Environment.NewLine}{Environment.NewLine}Failed:{Environment.NewLine}{failedList}";
            MessageBox.Show(message, "Script import", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RemoveScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ScriptListEntry entry)
            {
                entry.IsDeleted = !entry.IsDeleted;
                ScriptsItemsControl.Items.Refresh();
                UpdateScriptControlsState();
                StatusTextBlock.Text = entry.IsDeleted ? $"{entry.FileName} removed" : $"{entry.FileName} restored";
            }
        }

        private void ClearAllScriptsButton_Click(object sender, RoutedEventArgs e)
        {
            bool anyActive = _scriptList.Any(s => !s.IsDeleted);
            bool anyEnabled = _scriptList.Any(s => !s.IsDeleted && s.IsEnabled);
            bool scriptsOn = EnableScriptsCheckBox.IsChecked == true;

            if (!anyActive) return;

            foreach (var entry in _scriptList)
                entry.IsDeleted = true;

            ScriptsItemsControl.Items.Refresh();
            UpdateScriptControlsState();
            StatusTextBlock.Text = $"{TextFormat.Plural(_scriptList.Count, "script")} marked for deletion";
        }

        private void ScriptEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateScriptControlsState();
        }

        private void EnableScriptsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateScriptControlsState();
            StatusTextBlock.Text = "Scripts enabled";
        }

        private void EnableScriptsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateScriptControlsState();
            StatusTextBlock.Text = "Scripts disabled";
        }

        private void UpdateScriptsVisibility() => NoScriptsTextBlock.Visibility = _scriptList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateScriptControlsState()
        {
            bool anyActive = _scriptList.Any(s => !s.IsDeleted);
            bool anyEnabled = _scriptList.Any(s => !s.IsDeleted && s.IsEnabled);
            bool scriptsOn = EnableScriptsCheckBox.IsChecked == true;

            ScriptsItemsControl.Opacity = scriptsOn ? 1.0 : UiOpacity.Inactive;

            EnableScriptsCheckBox.IsEnabled = anyEnabled;
            EnableScriptsCheckBox.Opacity = !anyEnabled
                ? UiOpacity.Blocked
                : (scriptsOn ? 1.0 : UiOpacity.Inactive);

            ClearAllScriptsButton.IsEnabled = anyActive;
            ClearAllScriptsButton.Opacity = anyActive ? 1.0 : UiOpacity.Blocked;
        }

        internal static (bool EnableChecked, bool EnableInteractive, double EnableOpacity, bool ClearEnabled) ResolveHotkeyPresentation(bool hasKey, bool enableChecked)
        {
            return (
                EnableChecked: hasKey && enableChecked,
                EnableInteractive: hasKey,
                EnableOpacity: hasKey ? 1.0 : UiOpacity.Inactive,
                ClearEnabled: hasKey);
        }

        private void UpdateHotkeyControlsState()
        {
            bool hasKey = HotkeyEditor?.HasHotkey == true;
            var (enableChecked, enableInteractive, enableOpacity, clearEnabled) = ResolveHotkeyPresentation(hasKey, EnableHotkeyCheckBox.IsChecked == true);

            EnableHotkeyCheckBox.IsChecked = enableChecked;
            EnableHotkeyCheckBox.IsHitTestVisible = enableInteractive;
            EnableHotkeyCheckBox.Opacity = enableOpacity;
            ClearHotkeyButton.IsEnabled = clearEnabled;

            CheckForHotkeyConflicts();
        }

        private void CheckForHotkeyConflicts()
        {
            if (HotkeyEditor?.HotkeyConfig == null || HotkeyEditor.HotkeyConfig.Key == Key.None)
            {
                ConflictWarning.Visibility = Visibility.Collapsed;
                HotkeyEditor.ConflictingProfile = null;
                return;
            }

            var quickSwitchHotkey = _settingsService.GetQuickSwitchHotkey();
            if (HasQuickSwitchHotkeyConflict(HotkeyEditor.HotkeyConfig, quickSwitchHotkey))
            {
                ConflictWarning.Text = "⚠ Already assigned to Quick Switch";
                ConflictWarning.Visibility = Visibility.Visible;
                HotkeyEditor.ConflictingProfile = "Quick Switch";
                return;
            }

            var conflictingProfile = FindConflictingProfile(HotkeyEditor.HotkeyConfig);
            if (conflictingProfile != null)
            {
                var enabledState = conflictingProfile.HotkeyConfig.IsEnabled ? "" : " (disabled)";
                ConflictWarning.Text = $"⚠ Already assigned to '{conflictingProfile.Name}'{enabledState}";
                ConflictWarning.Visibility = Visibility.Visible;
                HotkeyEditor.ConflictingProfile = conflictingProfile.Name;
            }
            else
            {
                ConflictWarning.Visibility = Visibility.Collapsed;
                HotkeyEditor.ConflictingProfile = null;
            }
        }

        internal static bool HasQuickSwitchHotkeyConflict(HotkeyConfig profileHotkey, HotkeyConfig quickSwitchHotkey) =>
            HotkeyConflictPolicy.UsesSameChord(profileHotkey, quickSwitchHotkey);

        private Profile FindConflictingProfile(HotkeyConfig hotkey) =>
            _profileEditorService.FindHotkeyConflict(_profile, hotkey);

        private void ProfileHotkeyEditor_HotkeyChanged(object sender, HotkeyConfig candidate)
        {
            var accepted = HotkeyEditor.HotkeyConfig?.Clone() ?? new HotkeyConfig();
            var quickSwitchHotkey = _settingsService.GetQuickSwitchHotkey();
            if (HasQuickSwitchHotkeyConflict(candidate, quickSwitchHotkey))
            {
                ConflictWarning.Text = "⚠ Already assigned to Quick Switch";
                ConflictWarning.Visibility = Visibility.Visible;
                HotkeyEditor.RejectPendingHotkey(accepted, "Quick Switch");
                return;
            }

            var conflictingProfile = FindConflictingProfile(candidate);
            if (conflictingProfile != null)
            {
                var enabledState = conflictingProfile.HotkeyConfig.IsEnabled ? "" : " (disabled)";
                ConflictWarning.Text = $"⚠ Already assigned to '{conflictingProfile.Name}'{enabledState}";
                ConflictWarning.Visibility = Visibility.Visible;
                HotkeyEditor.RejectPendingHotkey(accepted, conflictingProfile.Name);
                return;
            }

            ConflictWarning.Visibility = Visibility.Collapsed;
            HotkeyEditor.ConflictingProfile = null;
            HotkeyEditor.AcceptPendingHotkey(candidate);
            EnableHotkeyCheckBox.IsChecked = true;
            UpdateHotkeyControlsState();
        }

        private void ClearHotkeyButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            HotkeyEditor.BeginExternalCommit();
        }

        private void EnableHotkeyCheckBox_Checked(object sender, RoutedEventArgs e) => StatusTextBlock.Text = "Global hotkey enabled";

        private void EnableHotkeyCheckBox_Unchecked(object sender, RoutedEventArgs e) => StatusTextBlock.Text = "Global hotkey disabled";

        private void ClearHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            bool capturePending = HotkeyEditor.IsCommitPending;
            var empty = new HotkeyConfig();
            HotkeyEditor.HotkeyConfig = empty;
            if (capturePending)
                HotkeyEditor.AcceptPendingHotkey(empty);

            EnableHotkeyCheckBox.IsChecked = false;
            UpdateHotkeyControlsState();
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioLoadCts?.Cancel();
            HotkeyEditor.CancelCapture();
            base.OnClosed(e);
        }
    }

    #endregion

}
