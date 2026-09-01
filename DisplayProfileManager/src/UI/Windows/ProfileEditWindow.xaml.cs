using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DisplayProfileManager.UI.Windows
{
    #region ProfileEditWindow

    public partial class ProfileEditWindow : Window
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private ProfileManager _profileManager;
        private Profile _profile;

        private List<DisplaySettingControl> _displayControls;
        private CancellationTokenSource _audioLoadCts;
        private ObservableCollection<AudioHelper.AudioDeviceInfo> _playbackDevices;
        private ObservableCollection<AudioHelper.AudioDeviceInfo> _captureDevices;
        private ObservableCollection<ScriptListEntry> _scriptList = new ObservableCollection<ScriptListEntry>();

        private bool _isEditMode;
        private string _pendingIconFilename;
        private bool _suppressAudioSelection;
        private bool _audioDevicesLoaded;
        private bool _audioSettingsDisabled;

        public ProfileEditWindow(Profile profileToEdit = null)
        {
            InitializeComponent();

            _profileManager = ProfileManager.Instance;
            _displayControls = new List<DisplaySettingControl>();
            _isEditMode = profileToEdit != null;
            _profile = profileToEdit ?? new Profile();

            _playbackDevices = new ObservableCollection<AudioHelper.AudioDeviceInfo>();
            _captureDevices = new ObservableCollection<AudioHelper.AudioDeviceInfo>();

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
            _profile.Name = _profileManager.GetUniqueProfileName("New Profile");
            ProfileNameTextBox.Text = _profile.Name;

            _ = PrefillDisplaySettingsAsync();
            _ = LoadAudioDevices();

            try
            {
                _profile.WallpaperSettings = WallpaperHelper.Capture();
                _profile.EnableWallpaper = false;

                _suppressWallpaperEvents = true;
                EnableWallpaperCheckBox.IsChecked = false;
                _suppressWallpaperEvents = false;

                PopulateWallpaperOptions();
                UpdateWallpaperModeIndicator();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not prefill new profile from current desktop");
            }
        }

        private async Task PrefillDisplaySettingsAsync()
        {
            try
            {
                var prefillWatch = Stopwatch.StartNew();
                var currentSettings = await _profileManager.GetCurrentDisplaySettingsAsync();
                if (currentSettings.Count == 0)
                {
                    logger.Warn("Prefill found no displays -> leaving editor empty");
                    return;
                }

                LoadDisplaySettings(currentSettings);
                prefillWatch.Stop();
                logger.Info($"Prefilled {TextHelper.Plural(currentSettings.Count, "display")} in {prefillWatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Could not prefill display settings for a new profile");
            }
        }

        private void InitializeWindow()
        {
            if (_scriptList == null)
                _scriptList = new ObservableCollection<ScriptListEntry>();

            ScriptsItemsControl.ItemsSource = _scriptList;
            HotkeyEditor.HotkeyChanged += (_, __) =>
            {
                // Auto-enable when key is assigned; auto-disable when key is cleared
                bool hasKey = HotkeyEditor?.HotkeyConfig?.Key != Key.None;
                if (hasKey && !(EnableHotkeyCheckBox.IsChecked ?? false))
                    EnableHotkeyCheckBox.IsChecked = true;

                UpdateHotkeyControlsState();
                UpdateClearHotkeyButtonState();
            };

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
            }
        }

        private void LoadDisplaySettings(List<DisplaySetting> settings)
        {
            DisplaySettingsPanel.Children.Clear();
            _displayControls.Clear();

            if (settings.Count == 0) return;

            var displayGroups = DisplayGroupHelper.GroupDisplaysForUI(settings);
            var cloneGroupCount = displayGroups.Count(g => g.IsCloneGroup);
            var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);

            var displayConfigs = DisplayConfigHelper.GetDisplayConfigs();
            int monitorIndex = 1;
            foreach (var group in displayGroups)
            {
                AddDisplaySettingControl(
                    group.RepresentativeSetting,
                    monitorIndex,
                    isCloneGroup: group.IsCloneGroup,
                    cloneGroupMembers: group.AllMembers,
                    displayConfigs: displayConfigs);
                monitorIndex++;
            }

            if (cloneGroupCount > 0)
            {
                logger.Info($"Loaded {TextHelper.Plural(settings.Count, "display")} with " + $"{TextHelper.Plural(cloneGroupCount, "clone group")} " + $"({cloneGroupDisplayCount} displays in clone groups)");
                StatusTextBlock.Text = $"Loaded {TextHelper.Plural(settings.Count, "display")} " + $"({TextHelper.Plural(cloneGroupCount, "clone group")} with " + $"{cloneGroupDisplayCount} displays)";
            }
            else
            {
                logger.Info($"Loaded {TextHelper.Plural(settings.Count, "display")}");
                StatusTextBlock.Text = $"Loaded {TextHelper.Plural(settings.Count, "display")}";
            }
        }

        private void PopulateFields()
        {
            ProfileNameTextBox.Text = _profile.Name;
            ProfileDescriptionTextBox.Text = _profile.Description;
            DefaultProfileCheckBox.IsChecked = _profileManager.GetDefaultProfile()?.Id == _profile.Id;
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
                EnableAudioCheckBox.IsChecked = _profile.EnableAudio;
            }
            finally
            {
                _suppressAudioSelection = false;
            }

            _ = LoadAudioDevices();

            EnableWallpaperCheckBox.IsChecked = _profile.EnableWallpaper;
            PopulateWallpaperOptions();
            UpdateWallpaperModeIndicator();

            EnableScriptsCheckBox.IsChecked = _profile.EnableScripts;
            UpdateClearIconButtonState();
            UpdateClearHotkeyButtonState();

            _scriptList.Clear();
            if (_profile.Scripts != null)
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                foreach (var script in _profile.Scripts)
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

                var currentSettings = await _profileManager.GetCurrentDisplaySettingsAsync();
                LoadDisplaySettings(currentSettings);
                logger.Info($"Load: {currentSettings.Count} physical displays loaded, " + $"{_displayControls.Count} controls created");
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

        private void AddDisplaySettingControl(DisplaySetting setting, int monitorIndex = 0, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = null)
        {
            if (DisplaySettingsPanel.Children.Count == 1 && DisplaySettingsPanel.Children[0] is TextBlock)
                DisplaySettingsPanel.Children.Clear();

            if (monitorIndex == 0)
                monitorIndex = _displayControls.Count + 1;

            var control = new DisplaySettingControl(setting, monitorIndex, isCloneGroup, cloneGroupMembers, displayConfigs);
            control.OnCloneGroupChanged = RebuildDisplayControls;
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

                List<DisplaySetting> displaySettings = new List<DisplaySetting>();

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
                    displaySettings = await _profileManager.GetCurrentDisplaySettingsAsync();

                var identifyWindows = new List<MonitorIdentifyWindow>();

                int index = 1;
                foreach (var setting in displaySettings)
                {
                    if (setting.IsEnabled)
                    {
                        if (DisplayHelper.IsMonitorConnected(setting.DeviceName))
                        {
                            if (NativeMonitorHelper.TryGetMonitorBounds(setting.DeviceName, out var bounds))
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
                    logger.Debug("Showing identify window for monitor {Index} at position Left:{Left}, Top:{Top}", window.MonitorIndex, window.Left, window.Top);
                }

                StatusTextBlock.Text = $"Showing identifiers on {TextHelper.Plural(identifyWindows.Count, "monitor")}";
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
                if (_profile.HotkeyConfig == null)
                    _profile.HotkeyConfig = new HotkeyConfig();
                _profile.HotkeyConfig = HotkeyEditor.HotkeyConfig?.Clone() ?? new HotkeyConfig();
                bool hotkeyAssigned = _profile.HotkeyConfig.Key != Key.None;
                _profile.HotkeyConfig.IsEnabled = (EnableHotkeyCheckBox.IsChecked ?? false) && hotkeyAssigned;

                bool wasDefault = _profileManager.GetDefaultProfile()?.Id == _profile.Id;
                if (DefaultProfileCheckBox.IsChecked == true && !wasDefault)
                    await _profileManager.SetDefaultProfileAsync(_profile.Id);
                else if (DefaultProfileCheckBox.IsChecked == false && wasDefault)
                    await _profileManager.SetDefaultProfileAsync(null);

                // Displays
                _profile.DisplaySettings.Clear();
                foreach (var control in _displayControls)
                {
                    var settings = control.GetDisplaySettings();
                    foreach (var setting in settings)
                        _profile.DisplaySettings.Add(setting);
                }

                // Wallpaper
                _profile.EnableWallpaper = EnableWallpaperCheckBox.IsChecked ?? false;
                if (_profile.EnableWallpaper)
                {
                    try
                    {
                        if (_profile.WallpaperSettings == null)
                            _profile.WallpaperSettings = WallpaperHelper.Capture();

                        ApplyWallpaperOptionsToSnapshot();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error capturing wallpaper");
                        StatusTextBlock.Text = "Error capturing wallpaper — profile saved without it";
                        _profile.WallpaperSettings = null;
                        _profile.EnableWallpaper = false;
                    }
                }
                UpdateWallpaperModeIndicator();

                // Audio
                if (_profile.AudioSettings == null) _profile.AudioSettings = new AudioSetting();
                _profile.AudioSettings.ApplyPlaybackDevice = ApplyOutputDeviceCheckBox.IsChecked ?? false;
                _profile.AudioSettings.ApplyCaptureDevice = ApplyInputDeviceCheckBox.IsChecked ?? false;
                _profile.EnableAudio = (EnableAudioCheckBox.IsChecked ?? false)
                    && (_profile.AudioSettings.ApplyPlaybackDevice || _profile.AudioSettings.ApplyCaptureDevice);

                if (OutputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo selectedOutput)
                {
                    _profile.AudioSettings.DefaultPlaybackDeviceId = selectedOutput.Id;
                    _profile.AudioSettings.PlaybackDeviceName = selectedOutput.SystemName;
                }

                if (InputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo selectedInput)
                {
                    _profile.AudioSettings.DefaultCaptureDeviceId = selectedInput.Id;
                    _profile.AudioSettings.CaptureDeviceName = selectedInput.SystemName;
                }

                // Scripts
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                if (!System.IO.Directory.Exists(scriptsFolder))
                    System.IO.Directory.CreateDirectory(scriptsFolder);

                _profile.Scripts = _scriptList
                    .Where(s => !s.IsDeleted && !string.IsNullOrWhiteSpace(s.FilePath))
                    .Select(s => new Script
                    {
                        FileName = System.IO.Path.GetFileName(s.FilePath),
                        Arguments = s.Arguments?.Trim() ?? string.Empty,
                        IsEnabled = s.IsEnabled
                    })
                    .ToList();

                _profile.EnableScripts = (EnableScriptsCheckBox.IsChecked ?? false) && _profile.Scripts.Any(s => s.IsEnabled);

                bool success = _isEditMode ? await _profileManager.UpdateProfileAsync(_profile) : await _profileManager.AddProfileAsync(_profile);
                if (success)
                {
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
                if (_profileManager.HasProfile(trimmedName))
                {
                    MessageBox.Show("A profile with this name already exists. Please choose a different name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProfileNameTextBox.Focus();
                    return false;
                }
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

            // Disable hotkeys while editor is open to avoid conflicts during capture
            try
            {
                var app = Application.Current as App;
                app?.DisableProfileHotkeys();
                logger.Debug("Disabled profile hotkeys for ProfileEditWindow");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disabling profile hotkeys");
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnStateChanged(EventArgs e)
        {
            TitleBarHelper.UpdateMargin(this, TitleBarGrid, TitleBarRowDefinition);
            base.OnStateChanged(e);
        }

        private void IconScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            bool scrollingDown = e.Delta < 0;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight;
            bool atTop = sv.VerticalOffset <= 0;

            if ((scrollingDown && atBottom) || (!scrollingDown && atTop))
            {
                e.Handled = true;

                var parent = VisualTreeHelper.GetParent(sv);
                while (parent != null && !(parent is ScrollViewer))
                    parent = VisualTreeHelper.GetParent(parent);

                var outer = parent as ScrollViewer;
                outer?.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta / 2.5);
            }
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
                IconPreviewImage.Source = IconHelper.LoadImageSource(_pendingIconFilename);
                IconFilenameTextBlock.Text = _pendingIconFilename;
                if (refresh)
                    StatusTextBlock.Text = $"Icon set to '{_pendingIconFilename}'";
            }
        }

        private async Task PopulateIconGridAsync()
        {
            var icons = await Task.Run(() => IconHelper.GetAvailableIcons());

            BuiltinIconsPanel.Children.Clear();

            BuiltinIconsPanel.HorizontalAlignment = SettingsManager.Instance.Debug.CenterIconGrid
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;

            foreach (string filename in icons)
            {
                var src = await Task.Run(() => IconHelper.LoadImageSource(filename, 32));
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
                _pendingIconFilename = await IconHelper.ImportIconAsync(dlg.FileName);
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

        private static void SyncDeviceList(ObservableCollection<AudioHelper.AudioDeviceInfo> target, IEnumerable<AudioHelper.AudioDeviceInfo> source)
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

        private static void PreserveUnavailableDevice(ObservableCollection<AudioHelper.AudioDeviceInfo> target, AudioHelper.AudioDeviceInfo device)
        {
            if (device == null || device.IsAvailable || string.IsNullOrEmpty(device.Id)) return;
            if (target.Any(d => d.Id == device.Id)) return;

            target.Add(device);
        }

        private static AudioHelper.AudioDeviceInfo EnsureUnavailableDevice(ObservableCollection<AudioHelper.AudioDeviceInfo> target, string deviceId, string deviceName, AudioHelper.DeviceType type)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;

            var existing = target.FirstOrDefault(d => d.Id == deviceId);
            if (existing != null)
            {
                return existing;
            }

            var unavailable = new AudioHelper.AudioDeviceInfo
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

                var playbackDevices = await Task.Run(() => AudioHelper.GetPlaybackDevices(), token);
                token.ThrowIfCancellationRequested();

                var captureDevices = await Task.Run(() => AudioHelper.GetCaptureDevices(), token);
                token.ThrowIfCancellationRequested();

                string currentPlaybackId = (OutputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo)?.Id;
                string currentCaptureId = (InputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo)?.Id;
                var currentPlayback = OutputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo;
                var currentCapture = InputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo;

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
                                    AudioHelper.DeviceType.Playback);
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
                                    AudioHelper.DeviceType.Capture);
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
                logger.Debug("Audio device load canceled.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading audio devices");
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
            var defaultPlayback = await Task.Run(() => AudioHelper.GetDefaultPlaybackDevice());
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
            var defaultCapture = await Task.Run(() => AudioHelper.GetDefaultCaptureDevice());
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
                logger.Error(ex, "Error refreshing audio devices");
                StatusTextBlock.Text = "Error refreshing audio devices";
            }
        }

        private void LoadWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _profile.WallpaperSettings = WallpaperHelper.Capture();
                _profile.EnableWallpaper = true;
                EnableWallpaperCheckBox.IsChecked = true;
                PopulateWallpaperOptions();
                UpdateWallpaperModeIndicator();
                StatusTextBlock.Text = $"Current wallpaper captured ({WallpaperModeNames.Display(_profile.WallpaperSettings.Mode)})";
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error capturing wallpaper");
                StatusTextBlock.Text = "Error capturing wallpaper";
            }
        }

        private static readonly uint[] SlideshowIntervals = { 60, 600, 1800, 3600, 21600, 86400 };

        private bool _suppressWallpaperEvents;
        private List<uint> _intervalOptions = new List<uint>();

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

                WallpaperFitmentComboBox.ItemsSource = WallpaperHelper.AllPositions.Select(p => new FitmentOption(char.ToUpper(p[0]) + p.Substring(1), backgroundBrush)).ToList();

                var intervals = SlideshowIntervals.ToList();
                var captured = snapshot?.SlideshowConfig?.IntervalSeconds ?? 1800;
                if (!intervals.Contains(captured)) intervals.Add(captured);
                intervals.Sort();
                _intervalOptions = intervals;
                WallpaperIntervalComboBox.ItemsSource = intervals.Select(DescribeInterval).ToList();

                var position = WallpaperHelper.NormalizePosition(snapshot?.Position);
                WallpaperFitmentComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(WallpaperHelper.AllPositions, position));

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

            var path = WallpaperHelper.GetSnapshotPreviewPath(snapshot);
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
                logger.Debug(ex, "Wallpaper preview could not be decoded");
            }
        }

        private static string DescribeInterval(uint seconds)
        {
            if (seconds < 60) return TextHelper.Plural(seconds, "second");
            if (seconds < 3600) return TextHelper.Plural(seconds / 60, "minute");
            if (seconds < 86400) return TextHelper.Plural(seconds / 3600, "hour");
            return "1 day";
        }

        private static void SetSectionState(UIElement panel, UIElement control, bool applicable, bool active)
        {
            control.IsEnabled = applicable;
            panel.Opacity = !applicable ? UiOpacity.Blocked : (active ? 1.0 : UiOpacity.Inactive);
        }

        private static bool IsLetterboxFitment(string fit) => string.Equals(fit, "fit", StringComparison.OrdinalIgnoreCase) || string.Equals(fit, "center", StringComparison.OrdinalIgnoreCase);

        private sealed class FitmentOption
        {
            public FitmentOption(string name, Brush background)
            {
                Name = name;
                Background = background;
                ShowSwatch = IsLetterboxFitment(name) ? Visibility.Visible : Visibility.Collapsed;
            }

            public string Name { get; }
            public Brush Background { get; }
            public Visibility ShowSwatch { get; }

            public override string ToString() => Name;
        }

        private void UpdateWallpaperOptionsEnabled()
        {
            bool enabled = EnableWallpaperCheckBox.IsChecked == true;

            var mode = _profile.WallpaperSettings?.Mode;

            bool modeHasFitment = mode == WallpaperMode.Picture;
            bool modeHasSlideshow = mode == WallpaperMode.Slideshow;

            SetSectionState(WallpaperFitmentPanel, WallpaperFitmentComboBox, modeHasFitment, enabled);
            SetSectionState(WallpaperIntervalPanel, WallpaperIntervalComboBox, modeHasSlideshow, enabled);
            SetSectionState(WallpaperShufflePanel, WallpaperShuffleCheckBox, modeHasSlideshow, enabled);
            SetSectionState(WallpaperSourcePanel, WallpaperSourceButton, modeHasSlideshow, enabled);

            // Solid uses color directly, Fit and Center letterbox against it
            bool modeHasColor = mode == WallpaperMode.Solid || mode == WallpaperMode.Picture;
            SetSectionState(WallpaperColorPanel, WallpaperColorButton, modeHasColor, enabled);
        }

        private void ApplyWallpaperOptionsToSnapshot()
        {
            var snapshot = _profile.WallpaperSettings;
            if (snapshot == null) return;

            int fitment = WallpaperFitmentComboBox.SelectedIndex;
            if (fitment >= 0 && fitment < WallpaperHelper.AllPositions.Length)
                snapshot.Position = WallpaperHelper.AllPositions[fitment];

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
                    _profile.WallpaperSettings = WallpaperHelper.Capture();
                    PopulateWallpaperOptions();
                    UpdateWallpaperModeIndicator();
                    StatusTextBlock.Text = $"Current wallpaper captured ({WallpaperModeNames.Display(_profile.WallpaperSettings.Mode)})";
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Wallpaper capture on enable failed");
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
            if (!NativeColorDialogHelper.TryChooseColor(new WindowInteropHelper(this).Handle, initialColor, out uint selectedColor)) return;

            snapshot.SolidColorArgb = selectedColor;
            logger.Debug($"Background color set to COLORREF 0x{snapshot.SolidColorArgb:X6}");
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

            snapshot.SlideshowConfig.SourcePaths = new List<string> { dialog.FolderName };
            UpdateWallpaperPreview();
            StatusTextBlock.Text = $"Slideshow source set to {dialog.FolderName}";
        }

        private void WallpaperFitmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressWallpaperEvents || _profile.WallpaperSettings == null) return;

            int i = WallpaperFitmentComboBox.SelectedIndex;
            if (i < 0 || i >= WallpaperHelper.AllPositions.Length) return;

            _profile.WallpaperSettings.Position = WallpaperHelper.AllPositions[i];
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
            WallpaperModeTextBlock.Text = mode == null || mode == WallpaperMode.Unknown ? "Wallpaper" : WallpaperModeNames.Display(mode.Value);
        }

        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAudioSelection) return;

            if (OutputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
                if (!string.IsNullOrEmpty(device.Id))
                    StatusTextBlock.Text = $"Output device: {device.SystemName}";
        }

        private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressAudioSelection) return;

            if (InputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
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
            bool outputUnavailable = (OutputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo)?.IsAvailable == false;
            bool inputUnavailable = (InputDeviceComboBox.SelectedItem as AudioHelper.AudioDeviceInfo)?.IsAvailable == false;

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
                DereferenceLinks = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Copy into sandbox; .exe is converted to .lnk
                string importedFileName = await ScriptManager.Instance.ImportScriptAsync(openFileDialog.FileName);

                if (importedFileName == null)
                {
                    StatusTextBlock.Text = "Failed to import script";
                    MessageBox.Show("The selected file could not be imported to the scripts folder.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string fullPath = System.IO.Path.Combine(scriptsPath, importedFileName);

                _scriptList.Add(new ScriptListEntry
                {
                    FilePath = fullPath,
                    FileName = System.IO.Path.GetFileName(fullPath),
                    Arguments = string.Empty,
                    IsEnabled = true,
                    IsDeleted = false
                });

                // Auto-enable scripts when first entry is added
                if (_scriptList.Count == 1)
                    EnableScriptsCheckBox.IsChecked = true;

                var sorted = _scriptList.OrderBy(s => System.IO.Path.GetFileName((string)s.FilePath)).ToList();

                _scriptList.Clear();
                foreach (var item in sorted) _scriptList.Add(item);

                UpdateScriptsVisibility();
                UpdateScriptControlsState();
                StatusTextBlock.Text = $"'{importedFileName}' added";
            }
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
            StatusTextBlock.Text = $"{TextHelper.Plural(_scriptList.Count, "script")} marked for deletion";
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

        private void UpdateHotkeyControlsState()
        {
            bool hasKey = HotkeyEditor?.HotkeyConfig?.Key != Key.None;

            EnableHotkeyCheckBox.IsHitTestVisible = hasKey;
            EnableHotkeyCheckBox.Opacity = hasKey ? 1.0 : UiOpacity.Inactive;

            if (!hasKey)
                EnableHotkeyCheckBox.IsChecked = false;

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

        private Profile FindConflictingProfile(HotkeyConfig hotkey)
        {
            var allProfiles = _profileManager.GetAllProfiles();
            return allProfiles.FirstOrDefault(p => p.Id != _profile.Id && p.HotkeyConfig != null && p.HotkeyConfig.Key != Key.None && p.HotkeyConfig.Equals(hotkey));
        }

        private void EnableHotkeyCheckBox_Checked(object sender, RoutedEventArgs e) => StatusTextBlock.Text = "Global hotkey enabled";

        private void EnableHotkeyCheckBox_Unchecked(object sender, RoutedEventArgs e) => StatusTextBlock.Text = "Global hotkey disabled";

        private void UpdateClearHotkeyButtonState()
        {
            ClearHotkeyButton.IsEnabled = HotkeyEditor?.HotkeyConfig?.Key != Key.None;
        }

        private void ClearHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            HotkeyEditor.HotkeyConfig = new HotkeyConfig();
            EnableHotkeyCheckBox.IsChecked = false;
            UpdateHotkeyControlsState();
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioLoadCts?.Cancel();

            try
            {
                var app = Application.Current as App;
                app?.EnableProfileHotkeys();
                logger.Debug("Re-enabled profile hotkeys after ProfileEditWindow closed");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error re-enabling profile hotkeys");
            }

            base.OnClosed(e);
        }
    }

    #endregion

    #region ScriptListEntry

    public class ScriptListEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
    }

    #endregion

    #region DisplaySettingsControl

    public class DisplaySettingControl : UserControl
    {
        private DisplaySetting _setting;
        private int _monitorIndex;

        public List<DisplaySetting> CloneGroupMembers; // Public so editor can preserve device order across clone rebuilds
        private bool _isCloneGroup;
        private ComboBox _resolutionComboBox;
        private ComboBox _refreshRateComboBox;
        private CheckBox _primaryCheckBox;
        private CheckBox _enabledCheckBox;
        private CheckBox _hdrCheckBox;
        private CheckBox _acmCheckBox;
        private ComboBox _rotationComboBox;
        private ComboBox _dpiComboBox;
        private ComboBox _colorProfileComboBox;
        private TextBlock _colorProfileLabel;
        private bool _pendingAcmEnabled; // Tracks last explicit ACM choice to be restored when HDR is toggled off

        public DisplaySettingControl(DisplaySetting setting, int monitorIndex = 1, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = null)
        {
            // Skip lookup when identity and native resolution are already populated
            if (string.IsNullOrEmpty(setting.DeviceName) || setting.NativeWidth == 0 || setting.NativeHeight == 0)
                setting.ResolveDeviceName(displayConfigs);

            _setting = setting;
            _monitorIndex = monitorIndex;
            _isCloneGroup = isCloneGroup;
            CloneGroupMembers = cloneGroupMembers ?? new List<DisplaySetting> { setting };
            _pendingAcmEnabled = setting.IsAcmEnabled;

            InitializeControl();
        }

        private void InitializeControl()
        {
            var mainPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            var primaryFg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var secondaryFg = (Brush)Application.Current.Resources["SecondaryTextBrush"];
            var accentFg = (Brush)Application.Current.Resources["ButtonBackgroundBrush"];

            FrameworkElement nameRow;

            if (_isCloneGroup && CloneGroupMembers.Count > 1)
            {
                var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                nameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new TextBlock
                {
                    Text = "\uE71B",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    Foreground = accentFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(icon, 0);
                nameGrid.Children.Add(icon);

                var leftContentPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var namesPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                foreach (var member in CloneGroupMembers)
                {
                    var nameText = member.IsCloneSource ? $"{member.ReadableDeviceName}  (Source)" : $"{member.ReadableDeviceName}  (Clone)";
                    namesPanel.Children.Add(new TextBlock
                    {
                        Text = nameText,
                        FontWeight = FontWeights.Medium,
                        FontSize = 14,
                        Foreground = primaryFg,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                leftContentPanel.Children.Add(namesPanel);

                _enabledCheckBox = new CheckBox
                {
                    Content = "Enable",
                    IsChecked = _setting.IsEnabled,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg
                };
                _enabledCheckBox.Checked += EnabledCheckBox_CheckedChanged;
                _enabledCheckBox.Unchecked += EnabledCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_enabledCheckBox);

                _primaryCheckBox = new CheckBox
                {
                    Content = "Primary",
                    IsChecked = _setting.IsPrimary,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg
                };
                _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
                _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;
                leftContentPanel.Children.Add(_primaryCheckBox);

                _hdrCheckBox = new CheckBox
                {
                    Content = "HDR",
                    IsChecked = _setting.IsHdrEnabled && _setting.IsHdrSupported,
                    IsEnabled = _setting.IsHdrSupported,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg,
                    ToolTip = _setting.IsHdrSupported ? "Enable HDR for this monitor" : "This monitor does not support HDR"
                };
                _hdrCheckBox.Checked += HdrCheckBox_CheckedChanged;
                _hdrCheckBox.Unchecked += HdrCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_hdrCheckBox);

                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.IsHdrSupported);
                _acmCheckBox = new CheckBox
                {
                    Content = "ACM",
                    IsChecked = _setting.IsAcmEnabled || (_setting.IsHdrEnabled && _setting.IsHdrSupported),
                    IsEnabled = acmSupported && !(_setting.IsHdrEnabled && _setting.IsHdrSupported),
                    Visibility = acmSupported ? Visibility.Visible : Visibility.Collapsed,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg,
                    ToolTip = "Auto Color Management"
                };
                _acmCheckBox.Checked += AcmCheckBox_CheckedChanged;
                _acmCheckBox.Unchecked += AcmCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_acmCheckBox);

                Grid.SetColumn(leftContentPanel, 1);
                nameGrid.Children.Add(leftContentPanel);

                var breakBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                breakBtnContent.Children.Add(new TextBlock
                {
                    Text = "\uE8E6",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 6, 0)
                });
                breakBtnContent.Children.Add(new TextBlock { Text = "Break Clone", VerticalAlignment = VerticalAlignment.Center });

                var breakBtn = new Button
                {
                    Content = breakBtnContent,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Style = BuildPrimaryButtonStyle()
                };
                breakBtn.Click += (s, e) => BreakCloneGroup();
                Grid.SetColumn(breakBtn, 2);
                nameGrid.Children.Add(breakBtn);

                nameRow = nameGrid;
            }
            else
            {
                var singleGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var nameBlock = new TextBlock
                {
                    Text = $"{_setting.ReadableDeviceName}",
                    FontWeight = FontWeights.Medium,
                    FontSize = 18,
                    Foreground = primaryFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                leftPanel.Children.Add(nameBlock);

                _enabledCheckBox = new CheckBox
                {
                    Content = "Enable",
                    IsChecked = _setting.IsEnabled,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg
                };
                _enabledCheckBox.Checked += EnabledCheckBox_CheckedChanged;
                _enabledCheckBox.Unchecked += EnabledCheckBox_CheckedChanged;
                leftPanel.Children.Add(_enabledCheckBox);

                _primaryCheckBox = new CheckBox
                {
                    Content = "Primary",
                    IsChecked = _setting.IsPrimary,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg
                };
                _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
                _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;
                leftPanel.Children.Add(_primaryCheckBox);

                _hdrCheckBox = new CheckBox
                {
                    Content = _setting.IsHdrSupported ? "HDR" : "HDR (Not Supported)",
                    IsChecked = _setting.IsHdrEnabled && _setting.IsHdrSupported,
                    IsEnabled = _setting.IsHdrSupported,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg,
                    ToolTip = _setting.IsHdrSupported ? "Enable HDR for this monitor" : "This monitor does not support HDR"
                };
                _hdrCheckBox.Checked += HdrCheckBox_CheckedChanged;
                _hdrCheckBox.Unchecked += HdrCheckBox_CheckedChanged;
                leftPanel.Children.Add(_hdrCheckBox);

                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.IsHdrSupported);
                _acmCheckBox = new CheckBox
                {
                    Content = "ACM",
                    IsChecked = _setting.IsAcmEnabled || (_setting.IsHdrEnabled && _setting.IsHdrSupported),
                    IsEnabled = acmSupported && !(_setting.IsHdrEnabled && _setting.IsHdrSupported),
                    Visibility = acmSupported ? Visibility.Visible : Visibility.Collapsed,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = primaryFg,
                    ToolTip = "Auto Color Management"
                };
                _acmCheckBox.Checked += AcmCheckBox_CheckedChanged;
                _acmCheckBox.Unchecked += AcmCheckBox_CheckedChanged;
                leftPanel.Children.Add(_acmCheckBox);

                Grid.SetColumn(leftPanel, 0);
                singleGrid.Children.Add(leftPanel);

                var cloneBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                cloneBtnContent.Children.Add(new TextBlock { Text = "Clone", VerticalAlignment = VerticalAlignment.Center });
                cloneBtnContent.Children.Add(new TextBlock
                {
                    Text = "\u25BC",
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 2, 0, 0)
                });

                var cloneBtn = new Button
                {
                    Content = cloneBtnContent,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Style = BuildDropdownButtonStyle()
                };
                cloneBtn.Click += CloneButton_Click;
                Grid.SetColumn(cloneBtn, 1);
                singleGrid.Children.Add(cloneBtn);

                nameRow = singleGrid;
            }
            mainPanel.Children.Add(nameRow);

            // Single-row settings grid — Resolution | Refresh Rate | Rotation | DPI | SDR/HDR Color
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Resolution
            var resolutionPanel = new StackPanel();
            resolutionPanel.Children.Add(new TextBlock { Text = "Resolution", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = primaryFg });
            _resolutionComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            _resolutionComboBox.SelectionChanged += ResolutionComboBox_SelectionChanged;
            PopulateResolutionComboBox();
            resolutionPanel.Children.Add(_resolutionComboBox);
            Grid.SetColumn(resolutionPanel, 0);
            contentGrid.Children.Add(resolutionPanel);

            // Refresh Rate
            var refreshRatePanel = new StackPanel();
            refreshRatePanel.Children.Add(new TextBlock { Text = "Refresh Rate", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = primaryFg });
            _refreshRateComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateRefreshRateComboBox();
            refreshRatePanel.Children.Add(_refreshRateComboBox);
            Grid.SetColumn(refreshRatePanel, 2);
            contentGrid.Children.Add(refreshRatePanel);

            // Rotation
            var rotationPanel = new StackPanel();
            rotationPanel.Children.Add(new TextBlock { Text = "Rotation", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = primaryFg });
            _rotationComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateRotationComboBox();
            _rotationComboBox.SelectionChanged += RotationComboBox_SelectionChanged;
            rotationPanel.Children.Add(_rotationComboBox);
            Grid.SetColumn(rotationPanel, 4);
            contentGrid.Children.Add(rotationPanel);

            // DPI
            var dpiPanel = new StackPanel();
            dpiPanel.Children.Add(new TextBlock { Text = "DPI", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = primaryFg });
            _dpiComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateDpiComboBox();
            dpiPanel.Children.Add(_dpiComboBox);
            Grid.SetColumn(dpiPanel, 6);
            contentGrid.Children.Add(dpiPanel);

            // SDR/HDR Color Profile
            var colorProfilePanel = new StackPanel();
            _colorProfileLabel = new TextBlock
            {
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"]
            };
            colorProfilePanel.Children.Add(_colorProfileLabel);
            _colorProfileComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            try { PopulateColorProfileComboBox(); } catch (Exception) { }
            _colorProfileComboBox.SelectionChanged += ColorProfileComboBox_SelectionChanged;
            colorProfilePanel.Children.Add(_colorProfileComboBox);
            Grid.SetColumn(colorProfilePanel, 8);
            Grid.SetRow(colorProfilePanel, 0);
            contentGrid.Children.Add(colorProfilePanel);
            UpdateColorProfileLabel();

            mainPanel.Children.Add(contentGrid);
            Content = mainPanel;

            UpdateControlStates();
        }
        private static Style BuildPrimaryButtonStyle()
        {
            // Theme resource retrieval
            var bg = (Brush)Application.Current.Resources["ButtonBackgroundBrush"];
            var fg = (Brush)Application.Current.Resources["ButtonForegroundBrush"];
            var hoverBg = (Brush)Application.Current.Resources["ButtonHoverBackgroundBrush"];
            var pressedBg = (Brush)Application.Current.Resources["ButtonPressedBackgroundBrush"];

            // Visual tree construction
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            // State triggers
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hoverBg));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, pressedBg));
            template.Triggers.Add(pressedTrigger);

            // Style composition
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            return style;
        }

        private static Style BuildSecondaryButtonStyle()
        {
            // Theme resource retrieval
            var bg = (Brush)Application.Current.Resources["SecondaryButtonBackgroundBrush"];
            var fg = (Brush)Application.Current.Resources["SecondaryButtonForegroundBrush"];
            var hoverBg = (Brush)Application.Current.Resources["SecondaryButtonHoverBackgroundBrush"];
            var pressedBg = (Brush)Application.Current.Resources["SecondaryButtonPressedBackgroundBrush"];

            // Visual tree construction
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            // State triggers
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hoverBg));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, pressedBg));
            template.Triggers.Add(pressedTrigger);

            // Style composition
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            return style;
        }

        private static Style BuildDropdownButtonStyle()
        {
            // Theme resource retrieval
            var bg = (Brush)Application.Current.Resources["ComboBoxBackgroundBrush"];
            var fg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var hoverBg = (Brush)Application.Current.Resources["ComboBoxHoverBackgroundBrush"];
            var border = (Brush)Application.Current.Resources["ComboBoxBorderBrush"];

            // Visual tree construction
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(content);

            // State triggers
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hoverBg));
            template.Triggers.Add(hoverTrigger);

            // Style composition
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.BackgroundProperty, bg));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, border));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            return style;
        }

        private void UpdateControlStates()
        {
            // Read enabled state from checkbox — _setting.IsEnabled is not written by handlers
            bool isEnabled = _enabledCheckBox.IsChecked == true;

            double opacity = isEnabled ? 1.0 : UiOpacity.Inactive;

            _resolutionComboBox.IsEnabled = true;
            _refreshRateComboBox.IsEnabled = true;
            _dpiComboBox.IsEnabled = true;
            _primaryCheckBox.IsEnabled = isEnabled;
            _rotationComboBox.IsEnabled = true;

            _resolutionComboBox.Opacity = opacity;
            _refreshRateComboBox.Opacity = opacity;
            _dpiComboBox.Opacity = opacity;
            _primaryCheckBox.Opacity = opacity;
            _rotationComboBox.Opacity = opacity;

            // Hide HDR when display does not support it
            _hdrCheckBox.Visibility = _setting.IsHdrSupported ? Visibility.Visible : Visibility.Collapsed;
            _hdrCheckBox.IsEnabled = _setting.IsHdrSupported;
            _hdrCheckBox.Opacity = opacity;

            if (_acmCheckBox != null)
            {
                bool hdrForced = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;
                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.IsHdrSupported);

                _acmCheckBox.Visibility = acmSupported ? Visibility.Visible : Visibility.Collapsed;
                _acmCheckBox.IsEnabled = acmSupported && !hdrForced;
                _acmCheckBox.Opacity = hdrForced ? UiOpacity.Blocked : opacity;
            }

            _rotationComboBox.Opacity = isEnabled ? (_rotationComboBox.SelectedIndex == 0 ? UiOpacity.Inactive : 1.0) : UiOpacity.Inactive;

            if (_colorProfileComboBox != null)
            {
                _colorProfileComboBox.IsEnabled = true;
                _colorProfileComboBox.Opacity = isEnabled ? ((_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null ? UiOpacity.Inactive : 1.0) : UiOpacity.Inactive;
            }

            // Enforce minimum one enabled display
            var parent = Parent as Panel;
            if (parent != null && !isEnabled)
            {
                int enabledCount = 0;
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true)
                        enabledCount++;

                if (enabledCount == 0)
                {
                    // Recheck last enabled display so at least one remains active
                    _enabledCheckBox.IsChecked = true;
                    MessageBox.Show("At least one display must remain enabled.", "Display Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            // Transfer primary flag to another enabled display when current primary is disabled
            if (!isEnabled && _primaryCheckBox.IsChecked == true && parent != null)
            {
                _primaryCheckBox.IsChecked = false;

                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true)
                    {
                        control.SetPrimary(true);
                        break;
                    }
                }
            }
        }

        private void EnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        private void PrimaryCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _setting.IsPrimary = true;
            var parent = Parent as Panel;
            if (parent != null)
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control != this)
                    {
                        control._primaryCheckBox.IsChecked = false;
                        control._setting.IsPrimary = false;
                    }
        }

        private void PrimaryCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var parent = Parent as Panel;
            if (parent != null)
            {
                int primaryCount = 0;
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control != this)
                        if (control._primaryCheckBox.IsChecked == true && control._enabledCheckBox.IsChecked == true)
                            primaryCount++;

                if (primaryCount == 0 && _enabledCheckBox.IsChecked == true)
                {
                    _primaryCheckBox.IsChecked = true;
                    MessageBox.Show("At least one enabled display must be set as primary.", "Display Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            _setting.IsPrimary = false;
        }

        public void SetPrimary(bool isPrimary)
        {
            // Update primary checkbox while suppressing event loops
            _primaryCheckBox.Checked -= PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked -= PrimaryCheckBox_Unchecked;

            _primaryCheckBox.IsChecked = isPrimary;
            _setting.IsPrimary = isPrimary;

            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;

            // Enforce single-primary constraint across siblings
            if (isPrimary)
            {
                var parent = Parent as Panel;
                if (parent != null)
                    foreach (var child in parent.Children)
                        if (child is DisplaySettingControl control && control != this)
                            control.SetPrimary(false);
            }
        }

        private void HdrCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool hdrOn = _hdrCheckBox.IsChecked == true && _setting.IsHdrSupported;

            if (_acmCheckBox != null)
            {
                if (hdrOn)
                {
                    // HDR forces ACM on while preserving pending choice
                    _acmCheckBox.IsChecked = true;
                    _acmCheckBox.IsEnabled = false;
                }
                else
                {
                    _acmCheckBox.IsChecked = _pendingAcmEnabled;
                    _acmCheckBox.IsEnabled = DisplayConfigHelper.IsAcmSupported(_setting.IsHdrSupported);
                }
            }

            // Clear color profile on HDR mode switch (cross-mode profile is not valid in new mode)
            UpdateColorProfileLabel();
            try { PopulateColorProfileComboBox(clearSelection: true); } catch (Exception) { }
        }

        private void AcmCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Only record explicit changes; HDR-forced toggles fire while checkbox is disabled
            if (_hdrCheckBox?.IsChecked != true)
                _pendingAcmEnabled = _acmCheckBox.IsChecked == true;
        }

        private void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var panel = Parent as Panel;
            if (panel == null) return;

            var available = panel.Children.OfType<DisplaySettingControl>().Where(c => c != this && !c._isCloneGroup).ToList();

            if (!available.Any())
            {
                MessageBox.Show("No other displays available to clone with.", "Clone Display", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bg = (Brush)Application.Current.Resources["ContentBackgroundBrush"];
            var fg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var border = (Brush)Application.Current.Resources["BorderBrush"];
            var hoverBg = (Brush)Application.Current.Resources["ControlHoverBackgroundBrush"];

            var stack = new StackPanel { MinWidth = 220 };
            foreach (var target in available)
            {
                var num = Regex.Match(target._setting.DeviceName ?? "", @"\d+$").Value;
                var label = string.IsNullOrEmpty(num) ? target._setting.ReadableDeviceName : $"Display {num} · {target._setting.ReadableDeviceName}";

                var row = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(12, 8, 12, 8),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = label, Foreground = fg, FontSize = 13 }
                };

                row.MouseEnter += (s, ev) => row.Background = hoverBg;
                row.MouseLeave += (s, ev) => row.Background = Brushes.Transparent;

                var captured = target;
                row.MouseLeftButtonUp += (s, ev) =>
                {
                    ((Popup)((Border)((StackPanel)row.Parent).Parent).Parent).IsOpen = false;
                    CreateCloneGroup(captured);
                };
                stack.Children.Add(row);
            }

            var popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = bg,
                    BorderBrush = border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(0, 4, 0, 4),
                    Child = stack
                }
            };
            popup.IsOpen = true;
        }

        private void CreateCloneGroup(DisplaySettingControl other)
        {
            var newCloneGroupId = "clone-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            uint sharedSourceId = _setting.SourceId;
            int sharedX = _setting.DisplayPositionX;
            int sharedY = _setting.DisplayPositionY;

            // Save pre-clone state before any primary transfer changes it
            foreach (var member in other.CloneGroupMembers)
            {
                member.OriginalSettings = false;
                member.OriginalPositionX = member.DisplayPositionX;
                member.OriginalPositionY = member.DisplayPositionY;
                member.OriginalSourceId = member.SourceId;
                member.OriginalIsPrimary = member.IsPrimary;
                member.OriginalWidth = member.Width;
                member.OriginalHeight = member.Height;
                member.OriginalFrequency = member.Frequency;
                member.OriginalRotation = member.Rotation;
                member.OriginalDpiScaling = member.DpiScaling;
                member.OriginalIsHdrEnabled = member.IsHdrEnabled;
                member.OriginalIsAcmEnabled = member.IsAcmEnabled;
                member.OriginalColorProfile = member.ColorProfile;
            }

            // Only transfer primary to source if no independent display already holds it
            bool otherHadPrimary = other.CloneGroupMembers.Any(m => m.IsPrimary);
            if (otherHadPrimary)
            {
                var panel = Parent as Panel;

                // No transfer needed if this control or any other independent control already holds primary
                bool primaryExistsElsewhere = CloneGroupMembers.Any(m => m.IsPrimary) ||
                    (panel != null && panel.Children
                        .OfType<DisplaySettingControl>()
                        .Where(c => c != this && c != other)
                        .Any(c => c.CloneGroupMembers.Any(m => m.IsPrimary)));

                foreach (var m in other.CloneGroupMembers)
                    m.IsPrimary = false;

                if (!primaryExistsElsewhere)
                    CloneGroupMembers[0].IsPrimary = true;
            }

            foreach (var member in CloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = true;
            }

            foreach (var member in other.CloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = false;
                member.SourceId = sharedSourceId;
                member.DisplayPositionX = sharedX;
                member.DisplayPositionY = sharedY;
            }

            OnCloneGroupChanged?.Invoke();
        }

        private void BreakCloneGroup()
        {
            var panel = Parent as Panel;
            uint maxSourceId = 0;

            if (panel != null)
                foreach (var ctrl in panel.Children.OfType<DisplaySettingControl>())
                    foreach (var m in ctrl.CloneGroupMembers)
                        maxSourceId = Math.Max(maxSourceId, m.SourceId);

            // Partition by clone role rather than list position
            var sourceMembers = CloneGroupMembers.Where(m => m.IsCloneSource).ToList();
            var attachedMembers = CloneGroupMembers.Where(m => !m.IsCloneSource).ToList();

            // Clear group id but retain source marker until settings are rebuilt
            foreach (var member in CloneGroupMembers)
                member.CloneGroupId = string.Empty;

            // Restore attached members first so primary ownership can be resolved correctly
            bool attachedHadPrimary = attachedMembers.Any(m => m.OriginalIsPrimary == true);
            bool primaryExistsElsewhere = (Parent as Panel)?.Children
                .OfType<DisplaySettingControl>()
                .Where(c => c != this)
                .Any(c => c.CloneGroupMembers.Any(m => m.IsPrimary)) ?? false;

            foreach (var member in sourceMembers)
            {
                member.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;
                member.OriginalSettings = false;
            }

            foreach (var member in attachedMembers)
                RestoreAttachedMemberState(member, _setting, ref maxSourceId);

            // Sync representative checkbox before rebuilding controls
            _primaryCheckBox.Checked -= PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked -= PrimaryCheckBox_Unchecked;
            _primaryCheckBox.IsChecked = !attachedHadPrimary && !primaryExistsElsewhere;
            _setting.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;
            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;

            OnCloneGroupChanged?.Invoke();
        }

        public static void RestoreAttachedMemberState(DisplaySetting member, DisplaySetting cloneSource, ref uint maxSourceId)
        {
            member.IsPrimary = member.OriginalIsPrimary ?? false;

            if (member.OriginalPositionX.HasValue)
            {
                member.SourceId = member.OriginalSourceId ?? ++maxSourceId;
                member.DisplayPositionX = member.OriginalPositionX.Value;
                member.DisplayPositionY = member.OriginalPositionY ?? 0;
                member.Width = member.OriginalWidth ?? (member.NativeWidth > 0 ? member.NativeWidth : member.Width);
                member.Height = member.OriginalHeight ?? (member.NativeHeight > 0 ? member.NativeHeight : member.Height);
                member.Frequency = member.OriginalFrequency ?? member.Frequency;
                member.Rotation = member.OriginalRotation ?? member.Rotation;
                member.DpiScaling = member.OriginalDpiScaling ?? member.DpiScaling;
                member.IsHdrEnabled = member.OriginalIsHdrEnabled ?? member.IsHdrEnabled;
                member.IsAcmEnabled = member.OriginalIsAcmEnabled ?? member.IsAcmEnabled;
                member.ColorProfile = member.OriginalColorProfile;
            }
            else
            {
                // Restore sensible independent layout when no pre-clone state was saved
                member.SourceId = ++maxSourceId;
                member.DisplayPositionX = cloneSource.DisplayPositionX + cloneSource.Width;
                member.DisplayPositionY = cloneSource.DisplayPositionY;
                if (member.NativeWidth > 0) { member.Width = member.NativeWidth; member.Height = member.NativeHeight; }
                var resKey = $"{member.Width}x{member.Height}";
                if (member.AvailableRefreshRates != null && member.AvailableRefreshRates.TryGetValue(resKey, out var rates) && rates.Count > 0)
                    member.Frequency = rates[0];
                if (member.AvailableDpiScaling != null && member.AvailableDpiScaling.Count > 0)
                    member.DpiScaling = member.AvailableDpiScaling[0];
            }

            // Clear all saved originals
            member.OriginalSettings = true;
            member.OriginalPositionX = null;
            member.OriginalPositionY = null;
            member.OriginalSourceId = null;
            member.OriginalIsPrimary = null;
            member.OriginalWidth = null;
            member.OriginalHeight = null;
            member.OriginalFrequency = null;
            member.OriginalRotation = null;
            member.OriginalDpiScaling = null;
            member.OriginalIsHdrEnabled = null;
            member.OriginalIsAcmEnabled = null;
            member.OriginalColorProfile = null;
        }

        public Action OnCloneGroupChanged;

        private void PopulateResolutionComboBox()
        {
            List<string> supportedResolutions;

            // Prefer stored resolutions; fall back to live system query
            if (_setting.AvailableResolutions != null && _setting.AvailableResolutions.Count > 0)
                supportedResolutions = _setting.AvailableResolutions;
            else
                supportedResolutions = DisplayHelper.GetSupportedResolutionsOnly(_setting.DeviceName);

            string nativeRes = _setting.NativeWidth > 0 ? $"{_setting.NativeWidth}x{_setting.NativeHeight}" : null;
            foreach (var resolution in supportedResolutions)
            {
                bool isNative = nativeRes != null && string.Equals(resolution, nativeRes, StringComparison.OrdinalIgnoreCase);
                _resolutionComboBox.Items.Add(isNative ? $"{resolution} ★" : resolution);
            }

            var currentResolution = $"{_setting.Width}x{_setting.Height}";
            var matchedItem = _resolutionComboBox.Items.Cast<object>().FirstOrDefault(i => i.ToString().StartsWith(currentResolution, StringComparison.OrdinalIgnoreCase));

            if (matchedItem != null)
                _resolutionComboBox.SelectedItem = matchedItem;
            else
            {
                _resolutionComboBox.Items.Insert(0, currentResolution);
                _resolutionComboBox.SelectedIndex = 0;
            }
        }

        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_resolutionComboBox.SelectedItem == null || _refreshRateComboBox == null) return;

            var resolutionText = (_resolutionComboBox.SelectedItem?.ToString() ?? "").Replace(" ★", "").Trim();
            var resolutionParts = resolutionText.Split('x');

            if (resolutionParts.Length >= 2 && int.TryParse(resolutionParts[0], out int width) && int.TryParse(resolutionParts[1], out int height))
            {
                // Temporarily update dimensions so refresh rates are populated for selected resolution
                int prevWidth = _setting.Width, prevHeight = _setting.Height;
                _setting.Width = width;
                _setting.Height = height;
                PopulateRefreshRateComboBox();
                _setting.Width = prevWidth;
                _setting.Height = prevHeight;
            }
        }

        private void PopulateRefreshRateComboBox()
        {
            _refreshRateComboBox.Items.Clear();

            List<int> refreshRates;
            var currentResolution = $"{_setting.Width}x{_setting.Height}";

            // Prefer stored rates for current resolution; fall back to live query
            if (_setting.AvailableRefreshRates != null && _setting.AvailableRefreshRates.ContainsKey(currentResolution) && _setting.AvailableRefreshRates[currentResolution].Count > 0)
                refreshRates = _setting.AvailableRefreshRates[currentResolution];
            else
                refreshRates = DisplayHelper.GetAvailableRefreshRates(_setting.DeviceName, _setting.Width, _setting.Height);

            int maxRate = refreshRates.Count > 0 ? refreshRates.Max() : -1;
            foreach (var rate in refreshRates)
                _refreshRateComboBox.Items.Add(rate == maxRate ? $"{rate}Hz ★" : $"{rate}Hz");

            var currentRefreshRate = $"{_setting.Frequency}Hz";
            var matchedItem = _refreshRateComboBox.Items.Cast<object>().FirstOrDefault(i => i.ToString().StartsWith(currentRefreshRate, StringComparison.OrdinalIgnoreCase));

            if (matchedItem != null)
                _refreshRateComboBox.SelectedItem = matchedItem;
            else if (_refreshRateComboBox.Items.Count > 0)
            {
                _refreshRateComboBox.Items.Insert(0, currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
            else
            {
                _refreshRateComboBox.Items.Add(currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateRotationComboBox()
        {
            _rotationComboBox.Items.Clear();
            _rotationComboBox.Items.Add("Not Applied");
            _rotationComboBox.Items.Add("0°");
            _rotationComboBox.Items.Add("90°");
            _rotationComboBox.Items.Add("180°");
            _rotationComboBox.Items.Add("270°");
            _rotationComboBox.SelectedIndex = _setting.Rotation;

            RotationComboBox_SelectionChanged(null, null);
        }

        private void RotationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rotationComboBox != null && _enabledCheckBox.IsChecked == true)
                _rotationComboBox.Opacity = _rotationComboBox.SelectedIndex == 0 ? UiOpacity.Inactive : 1.0;
        }

        private void PopulateDpiComboBox()
        {
            List<uint> dpiValues;

            // Prefer stored values; fall back to live system query
            if (_setting.AvailableDpiScaling != null && _setting.AvailableDpiScaling.Count > 0)
                dpiValues = _setting.AvailableDpiScaling;
            else
                dpiValues = DpiHelper.GetSupportedDpiScalingOnly(_setting.DeviceName).ToList();

            foreach (uint dpi in dpiValues)
                _dpiComboBox.Items.Add($"{dpi}%");

            var currentDpi = $"{_setting.DpiScaling}%";
            if (_dpiComboBox.Items.Contains(currentDpi))
                _dpiComboBox.SelectedItem = currentDpi;
            else
            {
                _dpiComboBox.Items.Insert(0, currentDpi);
                _dpiComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateColorProfileComboBox(bool clearSelection = false)
        {
            // Preserve current selection across repopulation unless HDR mode just changed
            string previousTag = clearSelection ? null : ((_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _setting.ColorProfile);

            _colorProfileComboBox.Items.Clear();

            var primaryFg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            bool hdrMode = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;

            _colorProfileComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Not Applied",
                Tag = (string)null,
                Foreground = primaryFg
            });

            var installedProfiles = hdrMode
                ? ColorProfileHelper.GetInstalledColorProfilesFiltered(hdrOnly: true)
                : ColorProfileHelper.GetInstalledColorProfilesFiltered(hdrOnly: false);
            foreach (var filename in installedProfiles)
            {
                _colorProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = filename,
                    Tag = filename,
                    Foreground = primaryFg,
                });
            }

            SelectColorProfile(previousTag);
        }

        private void SelectColorProfile(string profileValue)
        {
            if (string.IsNullOrEmpty(profileValue))
            {
                _colorProfileComboBox.SelectedIndex = 0;
                UpdateColorProfileOpacity();
                return;
            }

            foreach (ComboBoxItem item in _colorProfileComboBox.Items)
            {
                if (string.Equals(item.Tag as string, profileValue, StringComparison.OrdinalIgnoreCase))
                {
                    _colorProfileComboBox.SelectedItem = item;
                    UpdateColorProfileOpacity();
                    return;
                }
            }

            // Preserve stored profile as placeholder when it is no longer installed
            var missing = new ComboBoxItem
            {
                Content = $"{profileValue}  (Not Found)",
                Tag = profileValue,
                Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
                ToolTip = "This color profile is no longer installed on this system"
            };
            _colorProfileComboBox.Items.Add(missing);
            _colorProfileComboBox.SelectedItem = missing;
            UpdateColorProfileOpacity();
        }

        private void ColorProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateColorProfileOpacity();
        }

        private void UpdateColorProfileLabel()
        {
            if (_colorProfileLabel == null) return;
            bool hdrActive = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;
            _colorProfileLabel.Text = hdrActive ? "HDR Color" : "SDR Color";
        }

        private void UpdateColorProfileOpacity()
        {
            if (_colorProfileComboBox == null) return;

            bool displayEnabled = _enabledCheckBox?.IsChecked == true;
            bool notApplied = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null;
            _colorProfileComboBox.Opacity = !displayEnabled || notApplied ? UiOpacity.Inactive : 1.0;
        }

        public List<DisplaySetting> GetDisplaySettings()
        {
            var settings = new List<DisplaySetting>();

            if (_resolutionComboBox.SelectedItem == null || _dpiComboBox.SelectedItem == null || _refreshRateComboBox.SelectedItem == null) return settings;

            var resolutionText = _resolutionComboBox.SelectedItem.ToString().Replace(" ★", "").Replace("★", "").Trim();
            var dpiText = _dpiComboBox.SelectedItem.ToString();
            var refreshRateText = _refreshRateComboBox.SelectedItem.ToString();

            var resolutionParts = resolutionText.Split('x');
            if (resolutionParts.Length < 2)
            {
                return settings;
            }
            if (!int.TryParse(resolutionParts[0], out int width))
            {
                return settings;
            }

            string heightPart = resolutionParts[1].Replace(" ★", "").Replace("★", "").Trim();
            if (heightPart.Contains("@")) heightPart = heightPart.Split('@')[0].Trim();
            if (!int.TryParse(heightPart, out int height))
            {
                return settings;
            }

            if (!uint.TryParse(dpiText.Replace("%", ""), out uint dpiScaling))
            {
                return settings;
            }

            if (!int.TryParse(refreshRateText.Replace("Hz", "").Replace(" ★", "").Trim(), out int frequency))
                frequency = 60;

            var isEnabled = _enabledCheckBox.IsChecked == true;
            var isHdrEnabled = _hdrCheckBox.IsChecked == true;
            var isAcmEnabled = _acmCheckBox?.IsChecked == true;
            var rotation = _rotationComboBox.SelectedIndex == 0 ? 0 : _rotationComboBox.SelectedIndex;
            var colorProfile = (_colorProfileComboBox?.SelectedItem is ComboBoxItem cp) ? cp.Tag as string : null;

            // Source always reads combo; attached reads own restored params only after BreakCloneGroup (CloneGroupId cleared)
            foreach (var originalSetting in CloneGroupMembers)
            {
                bool useOriginalSettings = originalSetting.OriginalSettings;
                var displaySetting = new DisplaySetting
                {
                    // Identity
                    DeviceName = originalSetting.DeviceName,
                    DeviceString = originalSetting.DeviceString,
                    ReadableDeviceName = originalSetting.ReadableDeviceName,
                    ManufacturerName = originalSetting.ManufacturerName,
                    ProductCodeID = originalSetting.ProductCodeID,
                    AdapterId = originalSetting.AdapterId,
                    TargetId = originalSetting.TargetId,
                    SourceId = originalSetting.SourceId,
                    CloneGroupId = originalSetting.CloneGroupId,
                    IsCloneSource = originalSetting.IsCloneSource && !string.IsNullOrEmpty(originalSetting.CloneGroupId),
                    PathIndex = originalSetting.PathIndex,
                    // State
                    IsEnabled = isEnabled,
                    IsPrimary = originalSetting.IsPrimary,
                    // Layout
                    DisplayPositionX = originalSetting.DisplayPositionX,
                    DisplayPositionY = originalSetting.DisplayPositionY,
                    // Configuration
                    Width = useOriginalSettings ? originalSetting.Width : width,
                    Height = useOriginalSettings ? originalSetting.Height : height,
                    Frequency = useOriginalSettings ? originalSetting.Frequency : frequency,
                    Rotation = useOriginalSettings ? originalSetting.Rotation : rotation,
                    DpiScaling = useOriginalSettings ? originalSetting.DpiScaling : dpiScaling,
                    IsHdrSupported = originalSetting.IsHdrSupported,
                    IsHdrEnabled = useOriginalSettings ? (originalSetting.IsHdrEnabled && originalSetting.IsHdrSupported) : (isHdrEnabled && originalSetting.IsHdrSupported),
                    IsAcmEnabled = useOriginalSettings ? originalSetting.IsAcmEnabled : isAcmEnabled,
                    ColorProfile = useOriginalSettings ? originalSetting.ColorProfile : colorProfile,
                    // Clone
                    OriginalSettings = originalSetting.OriginalSettings,
                    OriginalPositionX = originalSetting.OriginalPositionX,
                    OriginalPositionY = originalSetting.OriginalPositionY,
                    OriginalSourceId = originalSetting.OriginalSourceId,
                    OriginalIsPrimary = originalSetting.OriginalIsPrimary,
                    OriginalWidth = originalSetting.OriginalWidth,
                    OriginalHeight = originalSetting.OriginalHeight,
                    OriginalFrequency = originalSetting.OriginalFrequency,
                    OriginalRotation = originalSetting.OriginalRotation,
                    OriginalDpiScaling = originalSetting.OriginalDpiScaling,
                    OriginalIsHdrEnabled = originalSetting.OriginalIsHdrEnabled,
                    OriginalIsAcmEnabled = originalSetting.OriginalIsAcmEnabled,
                    OriginalColorProfile = originalSetting.OriginalColorProfile,
                    // Native
                    NativeWidth = originalSetting.NativeWidth,
                    NativeHeight = originalSetting.NativeHeight,
                    // Capabilities
                    AvailableResolutions = originalSetting.AvailableResolutions,
                    AvailableRefreshRates = originalSetting.AvailableRefreshRates,
                    AvailableDpiScaling = originalSetting.AvailableDpiScaling
                };
                settings.Add(displaySetting);
            }

            return settings;
        }

        public bool ValidateInput()
        {
            if (_resolutionComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a resolution for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _resolutionComboBox.Focus();
                return false;
            }

            if (_refreshRateComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a refresh rate for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _refreshRateComboBox.Focus();
                return false;
            }

            if (_dpiComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a DPI scaling for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _dpiComboBox.Focus();
                return false;
            }

            if (_enabledCheckBox.IsChecked == true)
            {
                var parent = Parent as Panel;
                if (parent != null)
                {
                    bool hasPrimary = false;
                    foreach (var child in parent.Children)
                    {
                        if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true && control._primaryCheckBox.IsChecked == true)
                        {
                            hasPrimary = true;
                            break;
                        }
                    }

                    if (!hasPrimary)
                    {
                        MessageBox.Show("At least one enabled display must be set as primary.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _primaryCheckBox.Focus();
                        return false;
                    }
                }
            }

            return true;
        }
    }

    #endregion
}