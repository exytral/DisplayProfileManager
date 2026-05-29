using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace DisplayProfileManager.UI.Windows
{
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

        private string _pendingIconFilename;
        private bool _isEditMode;

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
        }

        private void InitializeWindow()
        {
            if (_scriptList == null)
                _scriptList = new ObservableCollection<ScriptListEntry>();

            ScriptsItemsControl.ItemsSource = _scriptList;
            HotkeyEditor.HotkeyChanged += (_, __) =>
            {
                // Auto-enable when user assigns a key; auto-disable when key is cleared.
                bool hasKey = HotkeyEditor?.HotkeyConfig?.Key != Key.None;
                if (hasKey && !(EnableHotkeyCheckBox.IsChecked ?? false))
                    EnableHotkeyCheckBox.IsChecked = true;
                UpdateHotkeyControlsState();
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

            if (cloneGroupCount > 0)
            {
                var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);
                logger.Info($"Loading {settings.Count} displays with {cloneGroupCount} clone group(s)");
            }

            var monitorIds = DisplayHelper.GetMonitorIDsFromWmiMonitorID();
            var displayConfigs = DisplayConfigHelper.GetDisplayConfigs();
            int monitorIndex = 1;
            foreach (var group in displayGroups)
            {
                AddDisplaySettingControl(
                    group.RepresentativeSetting,
                    monitorIndex,
                    isCloneGroup: group.IsCloneGroup,
                    cloneGroupMembers: group.AllMembers,
                    monitorIds: monitorIds);
                monitorIndex++;
            }

            if (cloneGroupCount > 0)
            {
                var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);
                StatusTextBlock.Text = $"Loaded {_displayControls.Count} display(s) " + $"({cloneGroupCount} clone group(s) with {cloneGroupDisplayCount} displays)";
            }
            else
                StatusTextBlock.Text = $"Loaded {settings.Count} display(s)";
        }

        private async void PopulateFields()
        {
            ProfileNameTextBox.Text = _profile.Name;
            ProfileDescriptionTextBox.Text = _profile.Description;
            DefaultProfileCheckBox.IsChecked = _profile.IsDefault;
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

            _ = LoadAudioDevices();

            EnableScriptsCheckBox.IsChecked = _profile.EnableScripts;

            _scriptList.Clear();
            if (_profile.Scripts != null)
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                foreach (var script in _profile.Scripts)
                {
                    string fullPath = System.IO.Path.IsPathRooted(script.FileName) ? script.FileName : System.IO.Path.Combine(scriptsFolder, script.FileName);
                    _scriptList.Add(new ScriptListEntry
                    {
                        FilePath = fullPath,
                        FileName = System.IO.Path.GetFileName(fullPath),
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

        private void AddDisplaySettingControl(DisplaySetting setting, int monitorIndex = 0, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayHelper.MonitorIdInfo> monitorIds = null)
        {
            if (DisplaySettingsPanel.Children.Count == 1 && DisplaySettingsPanel.Children[0] is TextBlock)
                DisplaySettingsPanel.Children.Clear();

            if (monitorIndex == 0)
                monitorIndex = _displayControls.Count + 1;

            var control = new DisplaySettingControl(setting, monitorIndex, isCloneGroup, cloneGroupMembers, monitorIds);
            control.OnCloneGroupChanged = RebuildDisplayControls;
            _displayControls.Add(control);
            DisplaySettingsPanel.Children.Add(control);
        }

        private void RebuildDisplayControls()
        {
            // Capture order from the settings list before rebuild — _cloneGroupMembers groups source before attached
            var deviceOrder = _profile.DisplaySettings
                .Select(s => s.DeviceName)
                .Distinct()
                .Select((name, idx) => (name, idx))
                .ToDictionary(x => x.name, x => x.idx);

            _profile.DisplaySettings.Clear();
            _profile.DisplaySettings.AddRange(
                _displayControls.SelectMany(c => c.GetDisplaySettings())
                    .OrderBy(s => deviceOrder.TryGetValue(s.DeviceName, out var i) ? i : int.MaxValue));

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
                            var targetScreen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == setting.DeviceName);
                            if (targetScreen != null)
                            {
                                var identifyWindow = new MonitorIdentifyWindow(index, targetScreen.Bounds.Left, targetScreen.Bounds.Top);
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

                StatusTextBlock.Text = $"Showing identifiers on {identifyWindows.Count} monitor(s)";
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

                _profile.Name = ProfileNameTextBox.Text.Trim();
                _profile.Description = ProfileDescriptionTextBox.Text.Trim();
                _profile.Icon = _pendingIconFilename;

                // Display settings
                _profile.DisplaySettings.Clear();
                foreach (var control in _displayControls)
                {
                    var settings = control.GetDisplaySettings();
                    foreach (var setting in settings)
                        _profile.DisplaySettings.Add(setting);
                }

                // Audio settings
                if (_profile.AudioSettings == null) _profile.AudioSettings = new AudioSetting();
                _profile.AudioSettings.ApplyPlaybackDevice = ApplyOutputDeviceCheckBox.IsChecked ?? false;
                _profile.AudioSettings.ApplyCaptureDevice = ApplyInputDeviceCheckBox.IsChecked ?? false;

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

                // Scripts — strip deleted entries and build the final list
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

                // Auto-disable scripts if none survive the deletion pass
                _profile.EnableScripts = (EnableScriptsCheckBox.IsChecked ?? false) && _profile.Scripts.Count > 0;

                // Hotkey — auto-disable if no key is assigned
                if (_profile.HotkeyConfig == null)
                    _profile.HotkeyConfig = new HotkeyConfig();
                _profile.HotkeyConfig = HotkeyEditor.HotkeyConfig?.Clone() ?? new HotkeyConfig();
                bool hotkeyAssigned = _profile.HotkeyConfig.Key != Key.None;
                _profile.HotkeyConfig.IsEnabled = (EnableHotkeyCheckBox.IsChecked ?? false) && hotkeyAssigned;

                if (DefaultProfileCheckBox.IsChecked == true && !_profile.IsDefault)
                {
                    _profile.IsDefault = true;
                    await _profileManager.SetDefaultProfileAsync(_profile.Id);
                }
                else if (DefaultProfileCheckBox.IsChecked == false && _profile.IsDefault)
                {
                    _profile.IsDefault = false;
                }

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

            // Reject duplicate names (case-insensitive, excluding the current profile in edit mode)
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
                    return false;
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
            UpdateTitleBarMargin();

            // Match owner window size and position at open time
            if (Owner != null)
            {
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
                Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
                Top = Owner.Top + (Owner.ActualHeight - Height) / 2;
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
            if (string.IsNullOrWhiteSpace(_pendingIconFilename))
            {
                IconPreviewImage.Source = null;
                IconFilenameTextBlock.Text = "No icon";
                if (refresh) StatusTextBlock.Text = "Icon cleared";
            }
            else
            {
                IconPreviewImage.Source = IconHelper.LoadImageSource(_pendingIconFilename);
                IconFilenameTextBlock.Text = _pendingIconFilename;
                if (refresh) StatusTextBlock.Text = $"Icon set to '{_pendingIconFilename}'";
            }
        }

        private async Task PopulateIconGridAsync()
        {
            var icons = await Task.Run(() => IconHelper.GetAvailableIcons());

            BuiltinIconsPanel.Children.Clear();

            foreach (string filename in icons)
            {
                var src = await Task.Run(() => IconHelper.LoadImageSource(filename, 32));
                if (src == null) continue;

                src.Freeze();

                var btn = new ToggleButton
                {
                    Width = 41,
                    Height = 41,
                    Tag = filename,
                    IsChecked = filename == _pendingIconFilename,
                    ToolTip = filename,
                    Cursor = Cursors.Hand,
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

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingIconFilename = null;
            RefreshIconPreview(true);
            SyncIconSelection();
        }

        private async Task LoadAudioDevices()
        {
            _audioLoadCts?.Cancel();
            _audioLoadCts = new CancellationTokenSource();
            var token = _audioLoadCts.Token;

            try
            {
                _playbackDevices.Clear();
                _captureDevices.Clear();

                var playbackDevices = await Task.Run(() => AudioHelper.GetPlaybackDevices(), token);
                token.ThrowIfCancellationRequested();

                var captureDevices = await Task.Run(() => AudioHelper.GetCaptureDevices(), token);
                token.ThrowIfCancellationRequested();

                foreach (var device in playbackDevices) _playbackDevices.Add(device);
                foreach (var device in captureDevices) _captureDevices.Add(device);

                if (_isEditMode && _profile.AudioSettings != null)
                {
                    ApplyOutputDeviceCheckBox.IsChecked = _profile.AudioSettings.ApplyPlaybackDevice;
                    ApplyInputDeviceCheckBox.IsChecked = _profile.AudioSettings.ApplyCaptureDevice;
                    OutputDeviceComboBox.IsEnabled = _profile.AudioSettings.ApplyPlaybackDevice;
                    InputDeviceComboBox.IsEnabled = _profile.AudioSettings.ApplyCaptureDevice;

                    if (!string.IsNullOrEmpty(_profile.AudioSettings.DefaultPlaybackDeviceId))
                    {
                        var savedPlayback = _playbackDevices.FirstOrDefault(d => d.Id == _profile.AudioSettings.DefaultPlaybackDeviceId);
                        if (savedPlayback != null) OutputDeviceComboBox.SelectedItem = savedPlayback;
                        else await SelectDefaultPlaybackDeviceAsync();
                    }
                    else
                    {
                        await SelectDefaultPlaybackDeviceAsync();
                    }

                    if (!string.IsNullOrEmpty(_profile.AudioSettings.DefaultCaptureDeviceId))
                    {
                        var savedCapture = _captureDevices.FirstOrDefault(d => d.Id == _profile.AudioSettings.DefaultCaptureDeviceId);
                        if (savedCapture != null) InputDeviceComboBox.SelectedItem = savedCapture;
                        else await SelectDefaultCaptureDeviceAsync();
                    }
                    else
                    {
                        await SelectDefaultCaptureDeviceAsync();
                    }
                }
                else
                {
                    await SelectDefaultPlaybackDeviceAsync();
                    await SelectDefaultCaptureDeviceAsync();
                }
            }
            catch (OperationCanceledException)
            {
                logger.Debug("Audio device load cancelled.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading audio devices");
                StatusTextBlock.Text = "Could not load audio devices";
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
                if (deviceInList != null) InputDeviceComboBox.SelectedItem = deviceInList;
                else if (_captureDevices.Count > 0) InputDeviceComboBox.SelectedIndex = 0;
            }
            else if (_captureDevices.Count > 0)
                InputDeviceComboBox.SelectedIndex = 0;
        }

        private async void LoadAudioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _ = LoadAudioDevices();
                StatusTextBlock.Text = "Current audio devices loaded";
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error loading audio devices");
                StatusTextBlock.Text = "Error loading audio devices";
            }
        }

        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
                if (!string.IsNullOrEmpty(device.Id))
                    StatusTextBlock.Text = $"Output device: {device.SystemName}";
        }

        private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
                if (!string.IsNullOrEmpty(device.Id))
                    StatusTextBlock.Text = $"Input device: {device.SystemName}";
        }

        private void ApplyOutputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            OutputDeviceComboBox.IsEnabled = true;
            StatusTextBlock.Text = "Output device enabled";
        }

        private void ApplyOutputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            OutputDeviceComboBox.IsEnabled = false;
            StatusTextBlock.Text = "Output device disabled";
        }

        private void ApplyInputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            InputDeviceComboBox.IsEnabled = true;
            StatusTextBlock.Text = "Input device enabled";
        }

        private void ApplyInputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            InputDeviceComboBox.IsEnabled = false;
            StatusTextBlock.Text = "Input device disabled";
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

        private async void AddScriptButton_Click(object sender, RoutedEventArgs e)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profileManagerPath = System.IO.Path.Combine(appDataPath, "DisplayProfileManager");
            string scriptsPath = System.IO.Path.Combine(profileManagerPath, "Scripts");

            if (!System.IO.Directory.Exists(scriptsPath))
            {
                try { System.IO.Directory.CreateDirectory(scriptsPath); }
                catch { scriptsPath = profileManagerPath; }
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
                    MessageBox.Show("The selected file could not be imported to the scripts folder.",
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

                // Auto-enable scripts when the first entry is added
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
            if (!anyActive) return;

            foreach (var entry in _scriptList)
                entry.IsDeleted = true;

            ScriptsItemsControl.Items.Refresh();
            UpdateScriptControlsState();
            StatusTextBlock.Text = $"{_scriptList.Count} script(s) marked for deletion";
        }

        private void ClearArgs_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ScriptListEntry entry)
                entry.Arguments = string.Empty;
        }

        private void UpdateScriptsVisibility() => NoScriptsTextBlock.Visibility = _scriptList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void UpdateScriptControlsState()
        {
            bool anyActive = _scriptList.Any(s => !s.IsDeleted);
            bool scriptsOn = EnableScriptsCheckBox.IsChecked == true;

            ScriptsItemsControl.Tag = scriptsOn ? 1.0 : 0.5;

            EnableScriptsCheckBox.IsHitTestVisible = anyActive;
            EnableScriptsCheckBox.Opacity = anyActive ? 1.0 : 0.5;
        }

        private void UpdateHotkeyControlsState()
        {
            bool hasKey = HotkeyEditor?.HotkeyConfig?.Key != Key.None;

            EnableHotkeyCheckBox.IsHitTestVisible = hasKey;
            EnableHotkeyCheckBox.Opacity = hasKey ? 1.0 : 0.5;

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
            return allProfiles.FirstOrDefault(p =>  p.Id != _profile.Id && p.HotkeyConfig != null && p.HotkeyConfig.Key != Key.None && p.HotkeyConfig.Equals(hotkey));
        }

        private void EnableHotkeyCheckBox_Checked(object sender, RoutedEventArgs e) => 
            StatusTextBlock.Text = "Global hotkey enabled";

        private void EnableHotkeyCheckBox_Unchecked(object sender, RoutedEventArgs e) =>
            StatusTextBlock.Text = "Global hotkey disabled";

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

    public class ScriptListEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
    }

    public class DisplaySettingControl : UserControl
    {
        private DisplaySetting _setting;
        private int _monitorIndex;

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


        public List<DisplaySetting> _cloneGroupMembers;
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

        public DisplaySettingControl(DisplaySetting setting, int monitorIndex = 1, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayHelper.MonitorIdInfo> monitorIds = null, List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = null)
        {
            // Skip WMI resolution if identity and native resolution are already populated
            if (string.IsNullOrEmpty(setting.DeviceName) || setting.NativeWidth == 0 || setting.NativeHeight == 0)
                setting.UpdateDeviceNameFromWMI(monitorIds, displayConfigs);

            _setting = setting;
            _monitorIndex = monitorIndex;
            _isCloneGroup = isCloneGroup;
            _cloneGroupMembers = cloneGroupMembers ?? new List<DisplaySetting> { setting };

            InitializeControl();
        }

        private void InitializeControl()
        {
            // Build the full display control: header row + settings row
            var mainPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            var primaryFg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var secondaryFg = (Brush)Application.Current.Resources["SecondaryTextBrush"];
            var accentFg = (Brush)Application.Current.Resources["ButtonBackgroundBrush"];

            FrameworkElement nameRow;

            if (_isCloneGroup && _cloneGroupMembers.Count > 1)
            {
                // Clone group header row — icon, stacked device names, checkboxes, Break Clone button
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
                foreach (var member in _cloneGroupMembers)
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
                leftContentPanel.Children.Add(_hdrCheckBox);

                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.TargetId);
                _acmCheckBox = new CheckBox // Grayed out and force-checked when HDR is active; hidden entirely when ACM is not supported
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
                breakBtn.Click += (s, e) => BreakClone();
                Grid.SetColumn(breakBtn, 2);
                nameGrid.Children.Add(breakBtn);

                nameRow = nameGrid;
            }
            else
            {
                // Single display header row — name, checkboxes, Clone dropdown button
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

                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.TargetId);
                _acmCheckBox = new CheckBox // Grayed out and force-checked when HDR is active; hidden entirely when ACM is not supported
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

            // DPI Scaling
            var dpiPanel = new StackPanel();
            dpiPanel.Children.Add(new TextBlock { Text = "DPI Scaling", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = primaryFg });
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

        private void EnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _setting.IsEnabled = _enabledCheckBox.IsChecked ?? true;
            UpdateControlStates();
        }

        private void HdrCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool hdrOn = _hdrCheckBox.IsChecked == true && _setting.IsHdrSupported;
            _setting.IsHdrEnabled = hdrOn;

            if (_acmCheckBox != null)
            {
                if (hdrOn)
                {
                    // HDR forces ACM on
                    _acmCheckBox.IsChecked = true;
                    _acmCheckBox.IsEnabled = false;
                }
                else
                {
                    _acmCheckBox.IsChecked = _setting.IsAcmEnabled;
                    _acmCheckBox.IsEnabled = DisplayConfigHelper.IsAcmSupported(_setting.TargetId);
                }
            }

            if (_colorProfileComboBox != null && _colorProfileComboBox.Items.Count > 0)
                _colorProfileComboBox.SelectedIndex = 0;

            UpdateColorProfileLabel();
            try { PopulateColorProfileComboBox(); } catch (Exception) { }
        }

        private void AcmCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_hdrCheckBox?.IsChecked != true)
                _setting.IsAcmEnabled = _acmCheckBox.IsChecked == true;
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
            bool notApplied = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null;
            _colorProfileComboBox.Opacity = notApplied ? 0.5 : 1.0;
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

        private void UpdateControlStates()
        {
            bool isEnabled = _setting.IsEnabled;
            double opacity = isEnabled ? 1.0 : 0.5;

            _resolutionComboBox.IsEnabled = isEnabled;
            _refreshRateComboBox.IsEnabled = isEnabled;
            _dpiComboBox.IsEnabled = isEnabled;
            _primaryCheckBox.IsEnabled = isEnabled;
            _rotationComboBox.IsEnabled = isEnabled;
            _hdrCheckBox.IsEnabled = isEnabled && _setting.IsHdrSupported;

            if (_acmCheckBox != null)
            {
                bool hdrForced = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;
                bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.TargetId);
                _acmCheckBox.IsEnabled = isEnabled && acmSupported && !hdrForced;
                _acmCheckBox.Opacity = acmSupported ? opacity : 0.5;
            }

            _resolutionComboBox.Opacity = opacity;
            _refreshRateComboBox.Opacity = opacity;
            _dpiComboBox.Opacity = opacity;
            _primaryCheckBox.Opacity = opacity;
            _hdrCheckBox.Opacity = opacity;
            _rotationComboBox.Opacity = isEnabled ? (_rotationComboBox.SelectedIndex == 0 ? 0.5 : 1.0) : 0.5;

            if (_acmCheckBox != null) _acmCheckBox.Opacity = opacity;

            if (_colorProfileComboBox != null)
            {
                _colorProfileComboBox.IsEnabled = isEnabled;
                _colorProfileComboBox.Opacity = isEnabled
                    ? ((_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null ? 0.5 : 1.0)
                    : 0.5;
            }

            // Enforce minimum one enabled display
            var parent = Parent as Panel;
            if (parent != null && !isEnabled)
            {
                int enabledCount = 0;
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control._setting.IsEnabled)
                        enabledCount++;

                if (enabledCount == 0)
                {
                    _enabledCheckBox.IsChecked = true;
                    _setting.IsEnabled = true;
                    MessageBox.Show("At least one display must remain enabled.", "Display Configuration",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Rollback visual states
                    _resolutionComboBox.IsEnabled = true;
                    _refreshRateComboBox.IsEnabled = true;
                    _dpiComboBox.IsEnabled = true;
                    _primaryCheckBox.IsEnabled = true;
                    if (_acmCheckBox != null)
                    {
                        bool hdrForced = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;
                        bool acmSupported = DisplayConfigHelper.IsAcmSupported(_setting.TargetId);
                        _acmCheckBox.IsEnabled = isEnabled && acmSupported && !hdrForced;
                        _acmCheckBox.Opacity = acmSupported ? opacity : 0.5;
                    }
                    _rotationComboBox.Opacity = _rotationComboBox.SelectedIndex == 0 ? 0.5 : 1.0;
                    _refreshRateComboBox.Opacity = 1.0;
                    _dpiComboBox.Opacity = 1.0;

                    if (_colorProfileComboBox != null)
                    {
                        _colorProfileComboBox.IsEnabled = true;
                        _colorProfileComboBox.Opacity = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null ? 0.5 : 1.0;
                    }
                }
            }

            // Transfer primary flag to another enabled display when current primary is disabled
            if (!isEnabled && _setting.IsPrimary && parent != null)
            {
                _primaryCheckBox.IsChecked = false;
                _setting.IsPrimary = false;

                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control._setting.IsEnabled)
                    {
                        control.SetPrimary(true);
                        break;
                    }
                }
            }
        }

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

        private void PopulateColorProfileComboBox()
        {
            // Preserve current selection so HDR toggle doesn't lose it on re-population
            string previousTag = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _setting.ColorProfile;

            _colorProfileComboBox.Items.Clear();

            var primaryFg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            bool hdrMode = _hdrCheckBox?.IsChecked == true && _setting.IsHdrSupported;

            // null sentinel matches "Not Applied"
            _colorProfileComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Not Applied",
                Tag = (string)null,
                Foreground = primaryFg
            });

            var installedProfiles = hdrMode ? ColorProfileHelper.GetInstalledColorProfilesFiltered(hdrOnly: true) : ColorProfileHelper.GetInstalledColorProfilesFiltered(hdrOnly: false);
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
            // null → "Not Applied" (index 0)
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

            // Stored profile no longer installed — insert as a placeholder to preserve value
            var missing = new ComboBoxItem
            {
                Content = $"{profileValue}  (not found)",
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
            if (_colorProfileComboBox?.SelectedItem is ComboBoxItem item)
                _setting.ColorProfile = item.Tag as string;
            UpdateColorProfileOpacity();
        }

        private void RotationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rotationComboBox != null && _setting.IsEnabled)
                _rotationComboBox.Opacity = _rotationComboBox.SelectedIndex == 0 ? 0.5 : 1.0;
        }

        private void PopulateRefreshRateComboBox()
        {
            _refreshRateComboBox.Items.Clear();

            List<int> refreshRates;
            var currentResolution = $"{_setting.Width}x{_setting.Height}";

            // Prefer stored rates for the current resolution; fall back to live query
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

        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_resolutionComboBox.SelectedItem == null || _refreshRateComboBox == null) return;

            var resolutionText = (_resolutionComboBox.SelectedItem?.ToString() ?? "").Replace(" ★", "").Trim();
            var resolutionParts = resolutionText.Split('x');

            if (resolutionParts.Length >= 2 && int.TryParse(resolutionParts[0], out int width) && int.TryParse(resolutionParts[1], out int height))
            {
                int prevWidth = _setting.Width, prevHeight = _setting.Height;
                _setting.Width = width;
                _setting.Height = height;
                PopulateRefreshRateComboBox();

                _setting.Width = prevWidth;
                _setting.Height = prevHeight;
            }
        }

        public List<DisplaySetting> GetDisplaySettings()
        {
            var settings = new List<DisplaySetting>();

            if (_resolutionComboBox.SelectedItem == null || _dpiComboBox.SelectedItem == null || _refreshRateComboBox.SelectedItem == null) return settings;

            var resolutionText = _resolutionComboBox.SelectedItem.ToString().Replace(" ★", "").Replace("★", "").Trim();
            var dpiText = _dpiComboBox.SelectedItem.ToString();
            var refreshRateText = _refreshRateComboBox.SelectedItem.ToString();

            var resolutionParts = resolutionText.Split('x');
            if (resolutionParts.Length < 2) return settings;
            if (!int.TryParse(resolutionParts[0], out int width)) return settings;

            string heightPart = resolutionParts[1].Replace(" ★", "").Replace("★", "").Trim();
            if (heightPart.Contains("@")) heightPart = heightPart.Split('@')[0].Trim();
            if (!int.TryParse(heightPart, out int height)) return settings;

            if (!uint.TryParse(dpiText.Replace("%", ""), out uint dpiScaling)) return settings;

            if (!int.TryParse(refreshRateText.Replace("Hz", "").Replace(" ★", "").Trim(), out int frequency))
                frequency = 60;

            var isEnabled = _enabledCheckBox.IsChecked == true;
            var isHdrEnabled = _hdrCheckBox.IsChecked == true;
            var isAcmEnabled = _acmCheckBox?.IsChecked == true;
            var rotation = _rotationComboBox.SelectedIndex == 0 ? 0 : _rotationComboBox.SelectedIndex;
            var colorProfile = (_colorProfileComboBox?.SelectedItem is ComboBoxItem cp) ? cp.Tag as string : null;

            // Source always reads combo; attached reads own restored params only after BreakClone (CloneGroupId cleared)
            foreach (var originalSetting in _cloneGroupMembers)
            {
                bool useOwnParams = !originalSetting.IsCloneSource && string.IsNullOrEmpty(originalSetting.CloneGroupId);

                var displaySetting = new DisplaySetting
                {
                    // Identity
                    DeviceName = originalSetting.DeviceName,
                    DeviceString = originalSetting.DeviceString,
                    ReadableDeviceName = originalSetting.ReadableDeviceName,
                    ManufacturerName = originalSetting.ManufacturerName,
                    ProductCodeID = originalSetting.ProductCodeID,
                    SerialNumberID = originalSetting.SerialNumberID,
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
                    // Active configuration
                    Width = useOwnParams ? originalSetting.Width : width,
                    Height = useOwnParams ? originalSetting.Height : height,
                    Frequency = useOwnParams ? originalSetting.Frequency : frequency,
                    Rotation = useOwnParams ? originalSetting.Rotation : rotation,
                    DpiScaling = useOwnParams ? originalSetting.DpiScaling : dpiScaling,
                    IsHdrSupported = originalSetting.IsHdrSupported,
                    IsHdrEnabled = useOwnParams ? (originalSetting.IsHdrEnabled && originalSetting.IsHdrSupported) : (isHdrEnabled && originalSetting.IsHdrSupported),
                    IsAcmEnabled = useOwnParams ? originalSetting.IsAcmEnabled : isAcmEnabled,
                    ColorProfile = useOwnParams ? originalSetting.ColorProfile : colorProfile,
                    // Clone
                    OriginalPositionX = originalSetting.OriginalPositionX,
                    OriginalPositionY = originalSetting.OriginalPositionY,
                    OriginalSourceId = originalSetting.OriginalSourceId,
                    OriginalWidth = originalSetting.OriginalWidth,
                    OriginalHeight = originalSetting.OriginalHeight,
                    OriginalFrequency = originalSetting.OriginalFrequency,
                    OriginalIsPrimary = originalSetting.OriginalIsPrimary,
                    OriginalDpiScaling = originalSetting.OriginalDpiScaling,
                    OriginalRotation = originalSetting.OriginalRotation,
                    OriginalColorProfile = originalSetting.OriginalColorProfile,
                    OriginalIsHdrEnabled = originalSetting.OriginalIsHdrEnabled,
                    OriginalIsAcmEnabled = originalSetting.OriginalIsAcmEnabled,
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

            if (_setting.IsEnabled)
            {
                var parent = Parent as Panel;
                if (parent != null)
                {
                    bool hasPrimary = false;
                    foreach (var child in parent.Children)
                    {
                        if (child is DisplaySettingControl control && control._setting.IsEnabled && control._setting.IsPrimary)
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

        private void PrimaryCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _setting.IsPrimary = true;
            var parent = Parent as Panel;
            if (parent != null)
            {
                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control != this)
                    {
                        control._primaryCheckBox.IsChecked = false;
                        control._setting.IsPrimary = false;
                    }
                }
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
                        if (control._primaryCheckBox.IsChecked == true && control._setting.IsEnabled)
                            primaryCount++;

                if (primaryCount == 0 && _setting.IsEnabled)
                {
                    _primaryCheckBox.IsChecked = true;
                    MessageBox.Show("At least one enabled display must be set as primary.", "Display Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            _setting.IsPrimary = false;
        }

        public Action OnCloneGroupChanged;

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
                    Clone(captured);
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

        private void Clone(DisplaySettingControl other)
        {
            var newCloneGroupId = "clone-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            uint sharedSourceId = _setting.SourceId;
            int sharedX = _setting.DisplayPositionX;
            int sharedY = _setting.DisplayPositionY;

            // Save all pre-clone state BEFORE any modifications — IsPrimary is cleared by the primary transfer below
            foreach (var member in other._cloneGroupMembers)
            {
                member.OriginalPositionX = member.DisplayPositionX;
                member.OriginalPositionY = member.DisplayPositionY;
                member.OriginalSourceId = member.SourceId;
                member.OriginalWidth = member.Width;
                member.OriginalHeight = member.Height;
                member.OriginalFrequency = member.Frequency;
                member.OriginalIsPrimary = member.IsPrimary;
                member.OriginalDpiScaling = member.DpiScaling;
                member.OriginalRotation = member.Rotation;
                member.OriginalColorProfile = member.ColorProfile;
                member.OriginalIsHdrEnabled = member.IsHdrEnabled;
                member.OriginalIsAcmEnabled = member.IsAcmEnabled;
            }

            // Only transfer primary to source if no independent display already holds it
            bool otherHadPrimary = other._cloneGroupMembers.Any(m => m.IsPrimary);
            if (otherHadPrimary)
            {
                var panel = Parent as Panel;

                // No transfer needed if this control or any other independent control already holds primary
                bool primaryExistsElsewhere = _cloneGroupMembers.Any(m => m.IsPrimary) ||
                    (panel != null && panel.Children
                        .OfType<DisplaySettingControl>()
                        .Where(c => c != this && c != other)
                        .Any(c => c._cloneGroupMembers.Any(m => m.IsPrimary)));

                foreach (var m in other._cloneGroupMembers)
                    m.IsPrimary = false;

                if (!primaryExistsElsewhere)
                    _cloneGroupMembers[0].IsPrimary = true;
            }

            foreach (var member in _cloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = true;
            }

            foreach (var member in other._cloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = false;
                member.SourceId = sharedSourceId;
                member.DisplayPositionX = sharedX;
                member.DisplayPositionY = sharedY;
            }

            OnCloneGroupChanged?.Invoke();
        }

        private void BreakClone()
        {
            var panel = Parent as Panel;
            uint maxSourceId = 0;

            if (panel != null)
                foreach (var ctrl in panel.Children.OfType<DisplaySettingControl>())
                    foreach (var m in ctrl._cloneGroupMembers)
                        maxSourceId = Math.Max(maxSourceId, m.SourceId);

            // Partition by role — AllMembers ordering is not guaranteed to match Clone() iteration order
            var sourceMembers = _cloneGroupMembers.Where(m => m.IsCloneSource).ToList();
            var attachedMembers = _cloneGroupMembers.Where(m => !m.IsCloneSource).ToList();

            // Clear CloneGroupId only; retain IsCloneSource so GetDisplaySettings() routes source vs attached correctly after rebuild; IsCloneSource is false in output whenever CloneGroupId is empty, so new controls are independent
            foreach (var member in _cloneGroupMembers)
                member.CloneGroupId = string.Empty;

            // Restore the attached display's pre-clone state first so primary is resolved correctly
            bool attachedHadPrimary = attachedMembers.Any(m => m.OriginalIsPrimary == true);
            bool primaryExistsElsewhere = (Parent as Panel)?.Children
                .OfType<DisplaySettingControl>()
                .Where(c => c != this)
                .Any(c => c._cloneGroupMembers.Any(m => m.IsPrimary)) ?? false;

            foreach (var member in sourceMembers)
                member.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;

            foreach (var member in attachedMembers)
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
                    member.DpiScaling = member.OriginalDpiScaling ?? member.DpiScaling;
                    member.Rotation = member.OriginalRotation ?? member.Rotation;
                    member.ColorProfile = member.OriginalColorProfile;  // null = "Not Applied" — valid restore target
                    member.IsHdrEnabled = member.OriginalIsHdrEnabled ?? member.IsHdrEnabled;
                    member.IsAcmEnabled = member.OriginalIsAcmEnabled ?? member.IsAcmEnabled;
                }
                else
                {
                    // Fallback for old profiles (OriginalPositionX not serialized; set via legacy code path)
                    member.SourceId = ++maxSourceId;
                    member.DisplayPositionX = _setting.DisplayPositionX + _setting.Width;
                    member.DisplayPositionY = _setting.DisplayPositionY;
                    if (member.NativeWidth > 0) { member.Width = member.NativeWidth; member.Height = member.NativeHeight; }
                    var resKey = $"{member.Width}x{member.Height}";
                    if (member.AvailableRefreshRates != null && member.AvailableRefreshRates.TryGetValue(resKey, out var rates) && rates.Count > 0)
                        member.Frequency = rates[0];
                    if (member.AvailableDpiScaling != null && member.AvailableDpiScaling.Count > 0)
                        member.DpiScaling = member.AvailableDpiScaling[0];
                }

                // Clear all saved originals
                member.OriginalPositionX = null;
                member.OriginalPositionY = null;
                member.OriginalSourceId = null;
                member.OriginalWidth = null;
                member.OriginalHeight = null;
                member.OriginalFrequency = null;
                member.OriginalIsPrimary = null;
                member.OriginalDpiScaling = null;
                member.OriginalRotation = null;
                member.OriginalColorProfile = null;
                member.OriginalIsHdrEnabled = null;
                member.OriginalIsAcmEnabled = null;
            }

            // Sync primary checkbox on the representative (source) before rebuild reads it
            _primaryCheckBox.Checked -= PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked -= PrimaryCheckBox_Unchecked;
            _primaryCheckBox.IsChecked = !attachedHadPrimary && !primaryExistsElsewhere;
            _setting.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;
            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;

            OnCloneGroupChanged?.Invoke();
        }

        public void SetPrimary(bool isPrimary)
        {
            // Update primary status while suppressing event loops
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
    }
}