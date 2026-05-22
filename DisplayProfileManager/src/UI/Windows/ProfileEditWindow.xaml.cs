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
        private bool _isEditMode;
        private List<DisplaySettingControl> _displayControls;
        private CancellationTokenSource _audioLoadCts;
        private ObservableCollection<AudioHelper.AudioDeviceInfo> _playbackDevices;
        private ObservableCollection<AudioHelper.AudioDeviceInfo> _captureDevices;
        private ObservableCollection<dynamic> _scriptList = new ObservableCollection<dynamic>();

        public ProfileEditWindow(Profile profileToEdit = null)
        {
            InitializeComponent();

            _profileManager = ProfileManager.Instance;
            _displayControls = new List<DisplaySettingControl>();
            _isEditMode = profileToEdit != null;
            _profile = profileToEdit ?? new Profile();

            // Data initialization
            _playbackDevices = new ObservableCollection<AudioHelper.AudioDeviceInfo>();
            _captureDevices = new ObservableCollection<AudioHelper.AudioDeviceInfo>();

            // Audio binding
            OutputDeviceComboBox.ItemsSource = _playbackDevices;
            InputDeviceComboBox.ItemsSource = _captureDevices;

            InitializeWindow();
        }

        private void InitializeWindow()
        {
            // Script collection setup
            if (_scriptList == null)
            {
                _scriptList = new ObservableCollection<dynamic>();
            }
            ScriptsItemsControl.ItemsSource = _scriptList;

            // Mode-specific UI state
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
                AddScriptButton.IsEnabled = false;
                ScriptsItemsControl.IsEnabled = false;
            }
        }

        private void LoadDisplaySettings(List<DisplaySetting> settings)
        {
            DisplaySettingsPanel.Children.Clear();
            _displayControls.Clear();

            if (settings.Count == 0)
                return;

            // Display grouping logic
            var displayGroups = DisplayGroupingHelper.GroupDisplaysForUI(settings);
            var cloneGroupCount = displayGroups.Count(g => g.IsCloneGroup);

            // Logging
            var logger = LoggerHelper.GetLogger();
            if (cloneGroupCount > 0)
            {
                var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);
                logger.Info($"Loading {settings.Count} displays with {cloneGroupCount} clone group(s)");
            }

            // Query WMI once for all controls rather than once per display
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

            // Remove bottom margin of mainPanel's last child
            if (_displayControls.Count > 0)
            {
                var lastControl = _displayControls[_displayControls.Count - 1];
                
                if (lastControl.Content is StackPanel panel && panel.Children.Count > 0)
                {
                    var last = panel.Children[panel.Children.Count - 1] as FrameworkElement;
                    if (last != null) last.Margin = new Thickness(0,0,0,-24);
                }
            }

            // Status bar update
            if (cloneGroupCount > 0)
            {
                var cloneGroupDisplayCount = displayGroups.Where(g => g.IsCloneGroup).Sum(g => g.AllMembers.Count);
                StatusTextBlock.Text = $"Loaded {_displayControls.Count} display(s) " +
                                     $"({cloneGroupCount} clone group(s) with {cloneGroupDisplayCount} displays)";
            }
            else
            {
                StatusTextBlock.Text = $"Loaded {settings.Count} display(s)";
            }
        }

        private async void PopulateFields()
        {
            // Basic info
            ProfileNameTextBox.Text = _profile.Name;
            ProfileDescriptionTextBox.Text = _profile.Description;
            DefaultProfileCheckBox.IsChecked = _profile.IsDefault;

            // Displays
            LoadDisplaySettings(_profile.DisplaySettings);

            // Hotkey configuration
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

            // Audio
            Task audio = LoadAudioDevices();

            // Scripting state
            EnableScriptsCheckBox.IsChecked = _profile.EnableScripts;
            AddScriptButton.IsEnabled = _profile.EnableScripts;
            ScriptsItemsControl.IsEnabled = _profile.EnableScripts;

            // Script collection processing
            _scriptList.Clear();
            if (_profile.Scripts != null)
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                foreach (var scriptString in _profile.Scripts)
                {
                    var (pathOrName, args) = ScriptHelper.ParseScriptString(scriptString);

                    string fullPath = System.IO.Path.IsPathRooted(pathOrName)
                        ? pathOrName
                        : System.IO.Path.Combine(scriptsFolder, pathOrName);

                    dynamic entry = new System.Dynamic.ExpandoObject();
                    entry.FilePath = fullPath;
                    entry.FileName = System.IO.Path.GetFileName(fullPath);
                    entry.Arguments = args;
                    entry.IsDeleted = false;

                    _scriptList.Add(entry);
                }
            }

            ScriptsItemsControl.ItemsSource = _scriptList;
            UpdateScriptsVisibility();
        }

        private async void DetectDisplaysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI feedback
                StatusTextBlock.Text = "Detecting current display settings...";
                DetectDisplaysButton.IsEnabled = false;

                var currentSettings = await _profileManager.GetCurrentDisplaySettingsAsync();

                // Primary monitor fallback
                bool hasPrimary = currentSettings.Any(s => s.IsPrimary && s.IsEnabled);
                if (!hasPrimary)
                {
                    var firstEnabled = currentSettings.FirstOrDefault(s => s.IsEnabled);
                    if (firstEnabled != null)
                    {
                        firstEnabled.IsPrimary = true;
                    }
                }

                // View update
                LoadDisplaySettings(currentSettings);

                // Logging
                var logger = LoggerHelper.GetLogger();
                logger.Info($"Detect Current: {currentSettings.Count} physical displays detected, " +
                          $"{_displayControls.Count} controls created");
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error detecting displays";
                MessageBox.Show($"Error detecting current display settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DetectDisplaysButton.IsEnabled = true;
            }
        }

        private void AddDisplaySettingControl(DisplaySetting setting, int monitorIndex = 0,
            bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null,
            List<DisplayHelper.MonitorIdInfo> monitorIds = null)
        {
            if (DisplaySettingsPanel.Children.Count == 1 &&
                DisplaySettingsPanel.Children[0] is TextBlock)
            {
                DisplaySettingsPanel.Children.Clear();
            }

            if (monitorIndex == 0)
                monitorIndex = _displayControls.Count + 1;

            var control = new DisplaySettingControl(setting, monitorIndex, isCloneGroup, cloneGroupMembers, monitorIds);
            control.OnCloneGroupChanged = RebuildDisplayControls;
            _displayControls.Add(control);
            DisplaySettingsPanel.Children.Add(control);
        }

        private void RebuildDisplayControls()
        {
            // Physical ID sorting
            var allSettings = _displayControls
                .SelectMany(c => c.GetDisplaySettings())
                .OrderBy(s => s.TargetId)
                .ToList();

            // Synchronization
            _profile.DisplaySettings.Clear();
            _profile.DisplaySettings.AddRange(allSettings);

            // UI Refresh
            LoadDisplaySettings(_profile.DisplaySettings);
        }

        private async void IdentifyDisplaysButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UI feedback
                StatusTextBlock.Text = "Identifying monitors...";
                IdentifyDisplaysButton.IsEnabled = false;

                List<DisplaySetting> displaySettings = new List<DisplaySetting>();

                // Data source selection
                if (_displayControls.Count > 0)
                {
                    displaySettings = _profile.DisplaySettings;

                    if (displaySettings.Count == 0)
                    {
                        foreach (var control in _displayControls)
                        {
                            var settings = control.GetDisplaySettings();
                            foreach (var setting in settings)
                            {
                                displaySettings.Add(setting);
                            }
                        }
                    }
                }
                else
                {
                    displaySettings = await _profileManager.GetCurrentDisplaySettingsAsync();
                }

                var identifyWindows = new List<MonitorIdentifyWindow>();

                // Overlay generation
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
                                // Coordinate mapping
                                var identifyWindow = new MonitorIdentifyWindow(index, targetScreen.Bounds.Left, targetScreen.Bounds.Top);
                                identifyWindows.Add(identifyWindow);
                            }
                        }
                    }
                    index++;
                }

                // Window activation
                foreach (var window in identifyWindows)
                {
                    window.Show();

                    logger.Debug("Showing identify window for monitor {Index} at position Left:{Left}, Top:{Top}",
                        window.MonitorIndex, window.Left, window.Top);
                }

                StatusTextBlock.Text = $"Showing identifiers on {identifyWindows.Count} monitor(s)";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error identifying displays";
                MessageBox.Show($"Error identifying displays: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (!ValidateInput())
                    return;

                // UI feedback
                SaveButton.IsEnabled = false;
                StatusTextBlock.Text = "Saving profile...";

                // Metadata mapping
                _profile.Name = ProfileNameTextBox.Text.Trim();
                _profile.Description = ProfileDescriptionTextBox.Text.Trim();

                // Display persistence
                _profile.DisplaySettings.Clear();
                foreach (var control in _displayControls)
                {
                    var settings = control.GetDisplaySettings();
                    foreach (var setting in settings)
                    {
                        _profile.DisplaySettings.Add(setting);
                    }
                }

                // Audio persistence
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

                // Script processing
                _profile.EnableScripts = EnableScriptsCheckBox.IsChecked ?? false;

                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string scriptsFolder = System.IO.Path.Combine(appDataPath, "DisplayProfileManager", "Scripts");

                if (!System.IO.Directory.Exists(scriptsFolder))
                    System.IO.Directory.CreateDirectory(scriptsFolder);

                _profile.Scripts = _scriptList
                    .Where(s =>
                    {
                        var d = (dynamic)s;
                        bool deleted = d.IsDeleted ?? false;
                        string path = (string)d.FilePath;
                        return !deleted && !string.IsNullOrWhiteSpace(path);
                    })
                    .Select(s =>
                    {
                        var d = (dynamic)s;
                        string fileName = System.IO.Path.GetFileName((string)d.FilePath);
                        string args = ((string)d.Arguments).Trim();
                        return ScriptManager.Instance.FormatCommand(fileName, args);
                    })
                    .ToList();

                // Hotkey persistence
                if (_profile.HotkeyConfig == null) _profile.HotkeyConfig = new HotkeyConfig();
                _profile.HotkeyConfig = HotkeyEditor.HotkeyConfig?.Clone() ?? new HotkeyConfig();
                _profile.HotkeyConfig.IsEnabled = EnableHotkeyCheckBox.IsChecked ?? false;

                // Default state logic
                if (DefaultProfileCheckBox.IsChecked == true && !_profile.IsDefault)
                {
                    _profile.IsDefault = true;
                    await _profileManager.SetDefaultProfileAsync(_profile.Id);
                }
                else if (DefaultProfileCheckBox.IsChecked == false && _profile.IsDefault)
                {
                    _profile.IsDefault = false;
                }

                // Database commit
                bool success = _isEditMode
                    ? await _profileManager.UpdateProfileAsync(_profile)
                    : await _profileManager.AddProfileAsync(_profile);

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
            // Profile name validation
            if (string.IsNullOrWhiteSpace(ProfileNameTextBox.Text))
            {
                MessageBox.Show("Please enter a profile name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ProfileNameTextBox.Focus();
                return false;
            }

            // Duplicate name check
            var trimmedName = ProfileNameTextBox.Text.Trim();
            if (!_isEditMode || !trimmedName.Equals(_profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (_profileManager.HasProfile(trimmedName))
                {
                    MessageBox.Show("A profile with this name already exists. Please choose a different name.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ProfileNameTextBox.Focus();
                    return false;
                }
            }

            // Display count validation
            if (_displayControls.Count == 0)
            {
                MessageBox.Show("Please add at least one display setting.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // Individual control validation
            foreach (var control in _displayControls)
            {
                if (!control.ValidateInput())
                {
                    return false;
                }
            }

            // Audio selection validation
            if (ApplyOutputDeviceCheckBox.IsChecked == true && OutputDeviceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an audio output device.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ApplyOutputDeviceCheckBox.Focus();
                return false;
            }

            if (ApplyInputDeviceCheckBox.IsChecked == true && InputDeviceComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an audio input device.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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

            // Hotkey interference prevention
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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

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
                    // Maximized layout compensation
                    TitleBarGrid.Margin = new Thickness(8, 8, 6, 0);
                    UpdateTitleBarHeight(40);
                }
                else
                {
                    // Normal layout reset
                    TitleBarGrid.Margin = new Thickness(0, 0, 0, 0);
                    UpdateTitleBarHeight(32);
                }
            }
        }

        private void UpdateTitleBarHeight(double height)
        {
            // Layout sync
            if (TitleBarRowDefinition != null)
            {
                TitleBarRowDefinition.Height = new GridLength(height);
            }

            // Chrome sync
            var windowChrome = WindowChrome.GetWindowChrome(this);
            if (windowChrome != null)
            {
                windowChrome.CaptionHeight = height;
            }
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

                // Device discovery off the UI thread — WithController + WMI can block for seconds
                var playbackDevices = await Task.Run(() => AudioHelper.GetPlaybackDevices(), token);
                token.ThrowIfCancellationRequested();

                var captureDevices = await Task.Run(() => AudioHelper.GetCaptureDevices(), token);
                token.ThrowIfCancellationRequested();

                foreach (var device in playbackDevices)
                    _playbackDevices.Add(device);

                foreach (var device in captureDevices)
                    _captureDevices.Add(device);

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
                if (deviceInList != null) OutputDeviceComboBox.SelectedItem = deviceInList;
                else if (_playbackDevices.Count > 0) OutputDeviceComboBox.SelectedIndex = 0;
            }
            else if (_playbackDevices.Count > 0)
            {
                OutputDeviceComboBox.SelectedIndex = 0;
            }
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
            {
                InputDeviceComboBox.SelectedIndex = 0;
            }
        }

        private async void DetectAudioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusTextBlock.Text = "Detecting current audio devices...";
                await LoadAudioDevices();
                StatusTextBlock.Text = "Current audio devices detected";
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error detecting audio devices");
                StatusTextBlock.Text = "Error detecting audio devices";
            }
        }

        private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OutputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
            {
                if (!string.IsNullOrEmpty(device.Id))
                {
                    StatusTextBlock.Text = $"Output device: {device.SystemName}";
                }
            }
        }

        private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InputDeviceComboBox.SelectedItem is AudioHelper.AudioDeviceInfo device)
            {
                if (!string.IsNullOrEmpty(device.Id))
                {
                    StatusTextBlock.Text = $"Input device: {device.SystemName}";
                }
            }
        }

        private void ApplyOutputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            OutputDeviceComboBox.IsEnabled = true;
            StatusTextBlock.Text = "Output device will be applied for this profile";
        }

        private void ApplyOutputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            OutputDeviceComboBox.IsEnabled = false;
            StatusTextBlock.Text = "Output device will not be applied for this profile";
        }

        private void ApplyInputDeviceCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            InputDeviceComboBox.IsEnabled = true;
            StatusTextBlock.Text = "Input device will be applied for this profile";
        }

        private void ApplyInputDeviceCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            InputDeviceComboBox.IsEnabled = false;
            StatusTextBlock.Text = "Input device will not be applied for this profile";
        }

        private void EnableScriptsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            AddScriptButton.IsEnabled = true;
            ScriptsItemsControl.IsEnabled = true;
            StatusTextBlock.Text = "Scripts will be executed for this profile";
        }

        private void EnableScriptsCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            AddScriptButton.IsEnabled = false;
            ScriptsItemsControl.IsEnabled = false;
            StatusTextBlock.Text = "Scripts will not be executed for this profile";
        }

        private async void AddScriptButton_Click(object sender, RoutedEventArgs e)
        {
            // Path resolution
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profileManagerPath = System.IO.Path.Combine(appDataPath, "DisplayProfileManager");
            string scriptsPath = System.IO.Path.Combine(profileManagerPath, "Scripts");

            if (!System.IO.Directory.Exists(scriptsPath))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(scriptsPath);
                }
                catch
                {
                    scriptsPath = profileManagerPath;
                }
            }

            // Dialog configuration
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                InitialDirectory = scriptsPath,
                Filter = "Scripts (*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk)|*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk|All files (*.*)|*.*",
                Title = "Import Script",
                DereferenceLinks = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Sandbox import — copies file, creates .lnk for .exe
                string importedFileName = await ScriptManager.Instance.ImportScriptAsync(openFileDialog.FileName);

                if (importedFileName == null)
                {
                    StatusTextBlock.Text = "Failed to import script";
                    MessageBox.Show("The selected file could not be imported to the scripts folder.",
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Entry creation
                string fullPath = System.IO.Path.Combine(scriptsPath, importedFileName);

                dynamic newEntry = new System.Dynamic.ExpandoObject();
                newEntry.FilePath = fullPath;
                newEntry.FileName = importedFileName;
                newEntry.Arguments = string.Empty;
                newEntry.IsDeleted = false;

                _scriptList.Add(newEntry);

                // Alphabetical sorting
                var sorted = _scriptList
                    .OrderBy(s => System.IO.Path.GetFileName((string)s.FilePath))
                    .ToList();

                _scriptList.Clear();
                foreach (var item in sorted)
                {
                    _scriptList.Add(item);
                }

                // View update
                UpdateScriptsVisibility();
                StatusTextBlock.Text = $"'{importedFileName}' added";
            }
        }

        private void RemoveScriptButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle deletion state
            if (sender is Button btn && btn.DataContext is System.Dynamic.ExpandoObject entry)
            {
                dynamic dEntry = entry;
                dEntry.IsDeleted = !(bool)(dEntry.IsDeleted ?? false);
                bool isNowDeleted = (bool)dEntry.IsDeleted;

                ScriptsItemsControl.Items.Refresh();
                StatusTextBlock.Text = isNowDeleted ? $"{dEntry.FileName} removed" : $"{dEntry.FileName} restored";
            }
        }

        private void ClearArgs_Click(object sender, RoutedEventArgs e)
        {
            // Reset argument string
            if (sender is Button btn && btn.DataContext is System.Dynamic.ExpandoObject entry)
            {
                ((dynamic)entry).Arguments = string.Empty;
            }
        }

        private void UpdateScriptsVisibility()
        {
            NoScriptsTextBlock.Visibility = _scriptList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HotkeyEditor_HotkeyChanged(object sender, HotkeyConfig e)
        {
            CheckForHotkeyConflicts();
        }

        private void CheckForHotkeyConflicts()
        {
            if (HotkeyEditor?.HotkeyConfig == null ||
                HotkeyEditor.HotkeyConfig.Key == Key.None)
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
            return allProfiles.FirstOrDefault(p =>
                p.Id != _profile.Id &&
                p.HotkeyConfig != null &&
                p.HotkeyConfig.Key != Key.None &&
                p.HotkeyConfig.Equals(hotkey));
        }

        private void EnableHotkeyCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Global hotkey enabled for this profile";
        }

        private void EnableHotkeyCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Global hotkey disabled for this profile";
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

    public class DisplaySettingControl : UserControl
    {
        private DisplaySetting _setting;
        private int _monitorIndex;
        private TextBox _deviceTextBox;

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

        private ComboBox _resolutionComboBox;
        private ComboBox _refreshRateComboBox;
        private ComboBox _dpiComboBox;
        private CheckBox _primaryCheckBox;
        private CheckBox _enabledCheckBox;
        private CheckBox _hdrCheckBox;
        private ComboBox _rotationComboBox;
        private List<DisplaySetting> _cloneGroupMembers;
        private bool _isCloneGroup;

        public DisplaySettingControl(DisplaySetting setting, int monitorIndex = 1,
            bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null,
            List<DisplayHelper.MonitorIdInfo> monitorIds = null,
            List<DisplayConfigHelper.DisplayConfigInfo> displayConfigs = null)
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
            var mainPanel = new StackPanel();

            var primaryFg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var secondaryFg = (Brush)Application.Current.Resources["SecondaryTextBrush"];

            FrameworkElement nameRow;
            if (_isCloneGroup && _cloneGroupMembers.Count > 1)
            {
                // Clone group: 🔗 icon (large) + stacked member names + CLONE badge
                var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new TextBlock
                {
                    Text = "\uE71B",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(icon, 0);
                nameGrid.Children.Add(icon);

                var namesPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                foreach (var member in _cloneGroupMembers)
                {
                    namesPanel.Children.Add(new TextBlock
                    {
                        Text = member.ReadableDeviceName,
                        FontWeight = FontWeights.Medium,
                        FontSize = 14,
                        Foreground = primaryFg,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                Grid.SetColumn(namesPanel, 1);
                nameGrid.Children.Add(namesPanel);

                var breakBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                breakBtnContent.Children.Add(new TextBlock
                {
                    Text = "\uE8E6",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 1, 7, 0)
                });
                breakBtnContent.Children.Add(new TextBlock
                {
                    Text = "Break Clone",
                    VerticalAlignment = VerticalAlignment.Center
                });
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
                // Single display: name left, clone dropdown right
                var singleGrid = new Grid();
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock
                {
                    Text = $"Monitor {_monitorIndex} — {_setting.ReadableDeviceName}",
                    FontWeight = FontWeights.Medium,
                    FontSize = 14,
                    Foreground = primaryFg,
                    VerticalAlignment = VerticalAlignment.Top,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameBlock, 0);
                singleGrid.Children.Add(nameBlock);

                var cloneBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                cloneBtnContent.Children.Add(new TextBlock
                {
                    Text = "Clone",
                    VerticalAlignment = VerticalAlignment.Center
                });
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

            var controlsGrid = new Grid { Margin = new Thickness(0, -8, 0, 16) };
            controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkboxPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _enabledCheckBox = new CheckBox
            {
                Content = "Enable",
                IsChecked = _setting.IsEnabled,
                FontSize = 14,
                Padding = new Thickness(6, 0, -1, 0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = primaryFg
            };
            _enabledCheckBox.Checked += EnabledCheckBox_CheckedChanged;
            _enabledCheckBox.Unchecked += EnabledCheckBox_CheckedChanged;
            checkboxPanel.Children.Add(_enabledCheckBox);

            _primaryCheckBox = new CheckBox
            {
                Content = "Primary",
                IsChecked = _setting.IsPrimary,
                FontSize = 14,
                Padding = new Thickness(6, 0, -1, 0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = primaryFg
            };
            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;
            checkboxPanel.Children.Add(_primaryCheckBox);

            _hdrCheckBox = new CheckBox
            {
                Content = _setting.IsHdrSupported ? "HDR" : "HDR (Not Supported)",
                IsChecked = _setting.IsHdrEnabled && _setting.IsHdrSupported,
                IsEnabled = _setting.IsHdrSupported,
                FontSize = 14,
                Padding = new Thickness(6, 0, -1, 0),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = primaryFg,
                ToolTip = _setting.IsHdrSupported ? "Enable HDR for this monitor" : "This monitor does not support HDR"
            };
            _hdrCheckBox.Checked += HdrCheckBox_CheckedChanged;
            _hdrCheckBox.Unchecked += HdrCheckBox_CheckedChanged;
            checkboxPanel.Children.Add(_hdrCheckBox);

            Grid.SetColumn(checkboxPanel, 0);
            controlsGrid.Children.Add(checkboxPanel);

            mainPanel.Children.Add(controlsGrid);

            // Main configuration grid for resolution, refresh, and scaling
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            contentGrid.RowDefinitions.Add(new RowDefinition());
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            contentGrid.RowDefinitions.Add(new RowDefinition());
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            contentGrid.RowDefinitions.Add(new RowDefinition());

            // Monitor selection
            var devicePanel = new StackPanel();
            devicePanel.Children.Add(new TextBlock { Text = "Monitor", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
            _deviceTextBox = new TextBox
            {
                Style = (Style)Application.Current.Resources["PrimaryTextBoxStyle"],
                IsReadOnly = true
            };
            _deviceTextBox.SetResourceReference(BackgroundProperty, "TextBoxBackgroundBrush");
            _deviceTextBox.SetResourceReference(ForegroundProperty, "TertiaryTextBrush");
            PopulateDeviceComboBox();
            devicePanel.Children.Add(_deviceTextBox);
            Grid.SetColumn(devicePanel, 0);
            Grid.SetRow(devicePanel, 0);
            contentGrid.Children.Add(devicePanel);

            // Resolution
            var resolutionPanel = new StackPanel();
            resolutionPanel.Children.Add(new TextBlock { Text = "Resolution", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
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
            Grid.SetColumn(resolutionPanel, 2);
            Grid.SetRow(resolutionPanel, 0);
            contentGrid.Children.Add(resolutionPanel);

            // Refresh Rate
            var refreshRatePanel = new StackPanel();
            refreshRatePanel.Children.Add(new TextBlock { Text = "Refresh Rate", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
            _refreshRateComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateRefreshRateComboBox();
            refreshRatePanel.Children.Add(_refreshRateComboBox);
            Grid.SetColumn(refreshRatePanel, 4);
            Grid.SetRow(refreshRatePanel, 0);
            contentGrid.Children.Add(refreshRatePanel);

            // Rotation
            var rotationPanel = new StackPanel();
            rotationPanel.Children.Add(new TextBlock { Text = "Rotation", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
            _rotationComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateRotationComboBox();
            rotationPanel.Children.Add(_rotationComboBox);
            Grid.SetColumn(rotationPanel, 0);
            Grid.SetRow(rotationPanel, 2);
            contentGrid.Children.Add(rotationPanel);

            // DPI Scaling
            var dpiPanel = new StackPanel();
            dpiPanel.Children.Add(new TextBlock { Text = "DPI Scaling", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
            _dpiComboBox = new ComboBox
            {
                Padding = new Thickness(8),
                BorderBrush = (Brush)Application.Current.Resources["ComboBoxBorderBrush"],
                BorderThickness = new Thickness(1),
                Style = (Style)Application.Current.Resources["PrimaryComboBoxStyle"]
            };
            PopulateDpiComboBox();
            dpiPanel.Children.Add(_dpiComboBox);
            Grid.SetColumn(dpiPanel, 2);
            Grid.SetRow(dpiPanel, 2);
            contentGrid.Children.Add(dpiPanel);

            mainPanel.Children.Add(contentGrid);

            Content = mainPanel;

            // Set initial control states based on enabled status
            UpdateControlStates();
        }

        private void EnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _setting.IsEnabled = _enabledCheckBox.IsChecked ?? true;
            UpdateControlStates();
        }

        private void HdrCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _setting.IsHdrEnabled = _hdrCheckBox.IsChecked == true && _setting.IsHdrSupported;
        }

        private void PopulateRotationComboBox()
        {
            _rotationComboBox.Items.Clear();
            _rotationComboBox.Items.Add("0° (Identity)");
            _rotationComboBox.Items.Add("90° (Rotate90)");
            _rotationComboBox.Items.Add("180° (Rotate180)");
            _rotationComboBox.Items.Add("270° (Rotate270)");

            // Enum mapping (1-4 to 0-3 index)
            int rotationIndex = _setting.Rotation - 1;
            if (rotationIndex >= 0 && rotationIndex < _rotationComboBox.Items.Count)
            {
                _rotationComboBox.SelectedIndex = rotationIndex;
            }
            else
            {
                _rotationComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateControlStates()
        {
            bool isEnabled = _setting.IsEnabled;

            // Interaction state synchronization
            _deviceTextBox.IsEnabled = isEnabled;
            _resolutionComboBox.IsEnabled = isEnabled;
            _refreshRateComboBox.IsEnabled = isEnabled;
            _dpiComboBox.IsEnabled = isEnabled;
            _primaryCheckBox.IsEnabled = isEnabled;
            _hdrCheckBox.IsEnabled = isEnabled && _setting.IsHdrSupported;
            _rotationComboBox.IsEnabled = isEnabled;

            // Visual feedback
            double opacity = isEnabled ? 1.0 : 0.5;
            _deviceTextBox.Opacity = opacity;
            _resolutionComboBox.Opacity = opacity;
            _refreshRateComboBox.Opacity = opacity;
            _dpiComboBox.Opacity = opacity;
            _primaryCheckBox.Opacity = opacity;
            _hdrCheckBox.Opacity = opacity;
            _rotationComboBox.Opacity = opacity;

            // Minimum display requirement check
            var parent = Parent as Panel;
            if (parent != null && !isEnabled)
            {
                int enabledCount = 0;
                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control._setting.IsEnabled)
                    {
                        enabledCount++;
                    }
                }

                if (enabledCount == 0)
                {
                    _enabledCheckBox.IsChecked = true;
                    _setting.IsEnabled = true;
                    MessageBox.Show("At least one display must remain enabled.", "Display Configuration",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Rollback visual states
                    _deviceTextBox.IsEnabled = true;
                    _resolutionComboBox.IsEnabled = true;
                    _refreshRateComboBox.IsEnabled = true;
                    _dpiComboBox.IsEnabled = true;
                    _primaryCheckBox.IsEnabled = true;
                    _deviceTextBox.Opacity = 1.0;
                    _resolutionComboBox.Opacity = 1.0;
                    _refreshRateComboBox.Opacity = 1.0;
                    _dpiComboBox.Opacity = 1.0;
                    _primaryCheckBox.Opacity = 1.0;
                }
            }

            // Primary monitor failover
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

            // Data source priority: Stored vs System Query
            if (_setting.AvailableResolutions != null && _setting.AvailableResolutions.Count > 0)
            {
                supportedResolutions = _setting.AvailableResolutions;
            }
            else
            {
                supportedResolutions = DisplayHelper.GetSupportedResolutionsOnly(_setting.DeviceName);
            }

            foreach (var resolution in supportedResolutions)
            {
                _resolutionComboBox.Items.Add(resolution);
            }

            // Current resolution selection
            var currentResolution = $"{_setting.Width}x{_setting.Height}";
            if (_resolutionComboBox.Items.Contains(currentResolution))
            {
                _resolutionComboBox.SelectedItem = currentResolution;
            }
            else
            {
                _resolutionComboBox.Items.Insert(0, currentResolution);
                _resolutionComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateDpiComboBox()
        {
            List<uint> dpiValues;

            // Data source priority: Stored vs System Query
            if (_setting.AvailableDpiScaling != null && _setting.AvailableDpiScaling.Count > 0)
            {
                dpiValues = _setting.AvailableDpiScaling;
            }
            else
            {
                dpiValues = DpiHelper.GetSupportedDPIScalingOnly(_setting.DeviceName).ToList();
            }

            foreach (uint dpi in dpiValues)
            {
                _dpiComboBox.Items.Add($"{dpi}%");
            }

            // Current DPI selection
            var currentDpi = $"{_setting.DpiScaling}%";
            if (_dpiComboBox.Items.Contains(currentDpi))
            {
                _dpiComboBox.SelectedItem = currentDpi;
            }
            else
            {
                _dpiComboBox.Items.Insert(0, currentDpi);
                _dpiComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateRefreshRateComboBox()
        {
            _refreshRateComboBox.Items.Clear();

            List<int> refreshRates;
            var currentResolution = $"{_setting.Width}x{_setting.Height}";

            // Data source priority: Stored vs System Query
            if (_setting.AvailableRefreshRates != null &&
                _setting.AvailableRefreshRates.ContainsKey(currentResolution) &&
                _setting.AvailableRefreshRates[currentResolution].Count > 0)
            {
                refreshRates = _setting.AvailableRefreshRates[currentResolution];
            }
            else
            {
                refreshRates = DisplayHelper.GetAvailableRefreshRates(_setting.DeviceName, _setting.Width, _setting.Height);
            }

            foreach (var rate in refreshRates)
            {
                _refreshRateComboBox.Items.Add($"{rate}Hz");
            }

            // Current frequency selection
            var currentRefreshRate = $"{_setting.Frequency}Hz";
            if (_refreshRateComboBox.Items.Contains(currentRefreshRate))
            {
                _refreshRateComboBox.SelectedItem = currentRefreshRate;
            }
            else if (_refreshRateComboBox.Items.Count > 0)
            {
                _refreshRateComboBox.Items.Insert(0, currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
            else if (_refreshRateComboBox.Items.Count == 0)
            {
                _refreshRateComboBox.Items.Add(currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateDeviceComboBox()
        {
            if (_isCloneGroup && _cloneGroupMembers.Count > 1)
            {
                // Clone group layout
                _deviceTextBox.Text = string.Join(Environment.NewLine, _cloneGroupMembers.Select(m => m.ReadableDeviceName));
                _deviceTextBox.Tag = _setting.DeviceName;
                _deviceTextBox.AcceptsReturn = true;
                _deviceTextBox.TextWrapping = TextWrapping.Wrap;

                // Metadata aggregation for tooltips
                var tooltipLines = new List<string> { "Clone Group Members:" };
                foreach (var member in _cloneGroupMembers)
                {
                    tooltipLines.Add($"\n{member.ReadableDeviceName}:");
                    tooltipLines.Add($"  Device: {member.DeviceName}");
                    tooltipLines.Add($"  Target ID: {member.TargetId}");
                    tooltipLines.Add($"  EDID: {member.ManufacturerName}-{member.ProductCodeID}-{member.SerialNumberID}");
                }
                _deviceTextBox.ToolTip = string.Join("\n", tooltipLines);
            }
            else
            {
                // Single display layout
                _deviceTextBox.Text = _setting.ReadableDeviceName;
                _deviceTextBox.Tag = _setting.DeviceName;
                _deviceTextBox.ToolTip =
                    $"Name: {_setting.ReadableDeviceName}\n" +
                    $"Device Name: {_setting.DeviceName}\n" +
                    $"Target ID: {_setting.TargetId}\n" +
                    $"EDID: {_setting.ManufacturerName}-{_setting.ProductCodeID}-{_setting.SerialNumberID}";
            }
        }

        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_resolutionComboBox.SelectedItem == null || _refreshRateComboBox == null)
                return;

            var resolutionText = _resolutionComboBox.SelectedItem.ToString();
            var resolutionParts = resolutionText.Split('x');

            if (resolutionParts.Length >= 2 &&
                int.TryParse(resolutionParts[0], out int width) &&
                int.TryParse(resolutionParts[1], out int height))
            {
                List<int> refreshRates;

                // Data source priority: Stored vs System Query
                if (_setting.AvailableRefreshRates != null &&
                    _setting.AvailableRefreshRates.ContainsKey(resolutionText) &&
                    _setting.AvailableRefreshRates[resolutionText].Count > 0)
                {
                    refreshRates = _setting.AvailableRefreshRates[resolutionText];
                }
                else
                {
                    refreshRates = DisplayHelper.GetAvailableRefreshRates(_setting.DeviceName, width, height);
                }

                _refreshRateComboBox.Items.Clear();
                foreach (var rate in refreshRates)
                {
                    _refreshRateComboBox.Items.Add($"{rate}Hz");
                }

                // Automatic selection of peak frequency
                if (_refreshRateComboBox.Items.Count > 0)
                {
                    _refreshRateComboBox.SelectedIndex = 0;
                }
            }
        }

        public List<DisplaySetting> GetDisplaySettings()
        {
            var settings = new List<DisplaySetting>();

            if (_resolutionComboBox.SelectedItem == null || _dpiComboBox.SelectedItem == null || _refreshRateComboBox.SelectedItem == null)
                return settings;

            var resolutionText = _resolutionComboBox.SelectedItem.ToString();
            var dpiText = _dpiComboBox.SelectedItem.ToString();
            var refreshRateText = _refreshRateComboBox.SelectedItem.ToString();

            // Resolution parsing with legacy format support
            var resolutionParts = resolutionText.Split('x');
            if (resolutionParts.Length < 2) return settings;

            if (!int.TryParse(resolutionParts[0], out int width))
                return settings;

            string heightPart = resolutionParts[1];
            if (heightPart.Contains("@"))
            {
                heightPart = heightPart.Split('@')[0].Trim();
            }

            if (!int.TryParse(heightPart, out int height))
                return settings;

            // Unit sanitization and numeric conversion
            if (!uint.TryParse(dpiText.Replace("%", ""), out uint dpiScaling))
                return settings;

            if (!int.TryParse(refreshRateText.Replace("Hz", ""), out int frequency))
                frequency = 60;

            var rotation = _rotationComboBox.SelectedIndex + 1;
            var isPrimary = _primaryCheckBox.IsChecked == true;
            var isEnabled = _enabledCheckBox.IsChecked == true;
            var isHdrEnabled = _hdrCheckBox.IsChecked == true;

            // Mapping UI state to clone group members
            bool isFirst = true;
            foreach (var originalSetting in _cloneGroupMembers)
            {
                // Determine parameter source (shared UI vs individual member storage)
                bool useOwnParams = !isFirst && string.IsNullOrEmpty(originalSetting.CloneGroupId);

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
                    PathIndex = originalSetting.PathIndex,

                    // State
                    IsEnabled = isEnabled,
                    IsPrimary = isFirst && isPrimary,

                    // Layout
                    DisplayPositionX = originalSetting.DisplayPositionX,
                    DisplayPositionY = originalSetting.DisplayPositionY,

                    // Active configuration
                    Width = useOwnParams ? originalSetting.Width : width,
                    Height = useOwnParams ? originalSetting.Height : height,
                    Frequency = useOwnParams ? originalSetting.Frequency : frequency,
                    Rotation = rotation,
                    IsHdrSupported = originalSetting.IsHdrSupported,
                    IsHdrEnabled = isHdrEnabled && originalSetting.IsHdrSupported,
                    DpiScaling = useOwnParams ? originalSetting.DpiScaling : dpiScaling,

                    // Native
                    NativeWidth = originalSetting.NativeWidth,
                    NativeHeight = originalSetting.NativeHeight,

                    // Capabilities
                    AvailableResolutions = originalSetting.AvailableResolutions,
                    AvailableRefreshRates = originalSetting.AvailableRefreshRates,
                    AvailableDpiScaling = originalSetting.AvailableDpiScaling
                };

                settings.Add(displaySetting);
                isFirst = false;
            }

            return settings;
        }

        public bool ValidateInput()
        {
            // Field presence validation
            if (_deviceTextBox.Text == null)
            {
                MessageBox.Show("Please select a monitor for all displays.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _deviceTextBox.Focus();
                return false;
            }

            if (_resolutionComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a resolution for all displays.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _resolutionComboBox.Focus();
                return false;
            }

            if (_refreshRateComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a refresh rate for all displays.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _refreshRateComboBox.Focus();
                return false;
            }

            if (_dpiComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a DPI scaling for all displays.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                _dpiComboBox.Focus();
                return false;
            }

            // Topography requirements
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
                        MessageBox.Show("At least one enabled display must be set as primary.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        _primaryCheckBox.Focus();
                        return false;
                    }
                }
            }

            return true;
        }

        private void PrimaryCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Mutually exclusive primary monitor enforcement
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
            // Minimum primary monitor requirement check
            var parent = Parent as Panel;
            if (parent != null)
            {
                int primaryCount = 0;
                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control != this)
                    {
                        if (control._primaryCheckBox.IsChecked == true && control._setting.IsEnabled)
                        {
                            primaryCount++;
                        }
                    }
                }

                if (primaryCount == 0 && _setting.IsEnabled)
                {
                    _primaryCheckBox.IsChecked = true;
                    MessageBox.Show("At least one enabled display must be set as primary.",
                                   "Display Configuration",
                                   MessageBoxButton.OK,
                                   MessageBoxImage.Information);
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

            // Filter for cloneable candidates
            var available = panel.Children
                .OfType<DisplaySettingControl>()
                .Where(c => c != this && !c._isCloneGroup)
                .ToList();

            if (!available.Any())
            {
                MessageBox.Show("No other displays available to clone with.", "Clone Display",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Theme resource retrieval
            var bg = (Brush)Application.Current.Resources["ContentBackgroundBrush"];
            var fg = (Brush)Application.Current.Resources["PrimaryTextBrush"];
            var border = (Brush)Application.Current.Resources["BorderBrush"];
            var hoverBg = (Brush)Application.Current.Resources["ControlHoverBackgroundBrush"];

            // Popup content construction
            var stack = new StackPanel { MinWidth = 220 };
            foreach (var target in available)
            {
                var num = Regex.Match(target._setting.DeviceName ?? "", @"\d+$").Value;
                var label = string.IsNullOrEmpty(num)
                    ? target._setting.ReadableDeviceName
                    : $"Display {num}  ·  {target._setting.ReadableDeviceName}";

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

            // Popup instantiation
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
            // Relationship synchronization
            var newCloneGroupId = "clone-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            uint sharedSourceId = _setting.SourceId;
            int sharedX = _setting.DisplayPositionX;
            int sharedY = _setting.DisplayPositionY;

            foreach (var member in _cloneGroupMembers)
                member.CloneGroupId = newCloneGroupId;

            foreach (var member in other._cloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.SourceId = sharedSourceId;

                // Clone members share the same source; positions must match
                member.DisplayPositionX = sharedX;
                member.DisplayPositionY = sharedY;
            }

            OnCloneGroupChanged?.Invoke();
        }

        private void BreakClone()
        {
            var panel = Parent as Panel;
            uint maxSourceId = 0;

            // Source ID resolution to prevent collisions
            if (panel != null)
            {
                foreach (var ctrl in panel.Children.OfType<DisplaySettingControl>())
                    foreach (var m in ctrl._cloneGroupMembers)
                        maxSourceId = Math.Max(maxSourceId, m.SourceId);
            }

            bool isFirst = true;
            foreach (var member in _cloneGroupMembers)
            {
                member.CloneGroupId = string.Empty;
                if (!isFirst)
                {
                    member.SourceId = ++maxSourceId;

                    // Parameter restoration for non-representative members
                    if (member.AvailableResolutions != null && member.AvailableResolutions.Count > 0)
                    {
                        // Prefer stored EDID native resolution — AvailableResolutions[0] may be a wider DCI resolution
                        string preferredRes = member.NativeWidth > 0
                            ? $"{member.NativeWidth}x{member.NativeHeight}"
                            : null;

                        string targetRes = (preferredRes != null && member.AvailableResolutions.Contains(preferredRes))
                            ? preferredRes
                            : member.AvailableResolutions[0];

                        var parts = targetRes.Split('x');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int w) &&
                            int.TryParse(parts[1], out int h))
                        {
                            member.Width = w;
                            member.Height = h;
                        }
                    }

                    var resKey = $"{member.Width}x{member.Height}";
                    if (member.AvailableRefreshRates != null &&
                        member.AvailableRefreshRates.TryGetValue(resKey, out var rates) &&
                        rates.Count > 0)
                    {
                        member.Frequency = rates[0];
                    }

                    if (member.AvailableDpiScaling != null && member.AvailableDpiScaling.Count > 0)
                        member.DpiScaling = member.AvailableDpiScaling[0];

                    // Offset position to avoid perfect overlap upon breaking
                    member.DisplayPositionX = _setting.DisplayPositionX + _setting.Width;
                    member.DisplayPositionY = _setting.DisplayPositionY;
                }
                isFirst = false;
            }

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
                {
                    foreach (var child in parent.Children)
                    {
                        if (child is DisplaySettingControl control && control != this)
                        {
                            control.SetPrimary(false);
                        }
                    }
                }
            }
        }
    }

    public static class DisplayGroupingHelper
    {
        public class DisplayGroup
        {
            public DisplaySetting RepresentativeSetting { get; set; }
            public List<DisplaySetting> AllMembers { get; set; }
            public bool IsCloneGroup => AllMembers.Count > 1;
        }

        public static List<DisplayGroup> GroupDisplaysForUI(List<DisplaySetting> displaySettings)
        {
            var result = new List<DisplayGroup>();

            // Identification of existing clone relationships
            var cloneGroups = displaySettings
                .Where(s => s.IsPartOfCloneGroup())
                .GroupBy(s => s.CloneGroupId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var processedCloneGroups = new HashSet<string>();

            foreach (var setting in displaySettings)
            {
                if (setting.IsPartOfCloneGroup() && processedCloneGroups.Contains(setting.CloneGroupId))
                {
                    continue;
                }

                if (setting.IsPartOfCloneGroup())
                {
                    processedCloneGroups.Add(setting.CloneGroupId);
                }

                var members = setting.IsPartOfCloneGroup()
                    ? cloneGroups[setting.CloneGroupId]
                    : new List<DisplaySetting> { setting };

                result.Add(new DisplayGroup
                {
                    RepresentativeSetting = setting,
                    AllMembers = members
                });
            }

            return result;
        }
    }
}