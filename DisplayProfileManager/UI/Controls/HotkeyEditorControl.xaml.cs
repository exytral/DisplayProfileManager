using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Hotkeys.Models;
using DpmKeyConverter = DisplayProfileManager.Hotkeys.Interop.KeyConverter;

namespace DisplayProfileManager.UI.Controls
{
    public partial class HotkeyEditorControl : UserControl, INotifyPropertyChanged
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private HotkeyConfig _currentHotkey = new();
        private HotkeyConfig _preArmHotkey = new();
        private HotkeyConfig _pendingCandidate;
        private IDisposable _captureScope;
        private bool _isRecording;
        private bool _isCommitPending;
        private bool _hasConflict;
        private bool _isHighContrast = SystemParameters.HighContrast;
        private bool _areAnimationsEnabled = SystemParameters.ClientAreaAnimation;
        private string _hotkeyText = string.Empty;
        private string _recorderHelpText = "Hotkey recorder. Focus to record a shortcut.";
        private readonly HashSet<Key> _pressedKeys = [];

        public static readonly DependencyProperty HotkeyConfigProperty =
            DependencyProperty.Register(nameof(HotkeyConfig), typeof(HotkeyConfig), typeof(HotkeyEditorControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHotkeyConfigChanged));

        public static readonly DependencyProperty ConflictingProfileProperty =
            DependencyProperty.Register(nameof(ConflictingProfile), typeof(string), typeof(HotkeyEditorControl),
                new PropertyMetadata(null, OnConflictingProfileChanged));

        public HotkeyConfig HotkeyConfig
        {
            get => (HotkeyConfig)GetValue(HotkeyConfigProperty);
            set => SetValue(HotkeyConfigProperty, value);
        }

        public string ConflictingProfile
        {
            get => (string)GetValue(ConflictingProfileProperty);
            set => SetValue(ConflictingProfileProperty, value);
        }

        internal Func<IDisposable> CaptureScopeFactory { get; set; }

        public bool IsRecording
        {
            get => _isRecording;
            private set
            {
                if (_isRecording == value) return;
                _isRecording = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsPulseActive));
                UpdateRecorderHelpText();
            }
        }

        public bool IsCommitPending
        {
            get => _isCommitPending;
            private set
            {
                if (_isCommitPending == value) return;
                _isCommitPending = value;
                OnPropertyChanged();
                UpdateRecorderHelpText();
            }
        }

        public bool IsHighContrast
        {
            get => _isHighContrast;
            private set
            {
                if (_isHighContrast == value) return;
                _isHighContrast = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPulseActive));
            }
        }

        public bool AreAnimationsEnabled
        {
            get => _areAnimationsEnabled;
            private set
            {
                if (_areAnimationsEnabled == value) return;
                _areAnimationsEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPulseActive));
            }
        }

        public bool HasConflict
        {
            get => _hasConflict;
            set
            {
                if (_hasConflict == value) return;
                _hasConflict = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsPulseActive));
                UpdateRecorderHelpText();
            }
        }

        public bool IsPulseActive => IsRecording && !HasConflict && !IsHighContrast && AreAnimationsEnabled;

        internal static bool HasAssignedHotkey(HotkeyConfig hotkey) => hotkey != null && hotkey.Key != Key.None;

        public bool HasHotkey => HasAssignedHotkey(_currentHotkey);
        public bool IsValid => !IsRecording && HasHotkey && !HasConflict && _currentHotkey?.IsValid() == true;

        public string HotkeyText
        {
            get => _hotkeyText;
            private set
            {
                if (_hotkeyText == value) return;
                _hotkeyText = value;
                OnPropertyChanged();
            }
        }

        public string RecorderHelpText
        {
            get => _recorderHelpText;
            private set
            {
                if (_recorderHelpText == value) return;
                _recorderHelpText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<HotkeyConfig> HotkeyChanged;

        public HotkeyEditorControl()
        {
            InitializeComponent();
            UpdateHotkeyText();
            Loaded += HotkeyEditorControl_Loaded;
            Unloaded += HotkeyEditorControl_Unloaded;
        }

        private void HotkeyEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            IsHighContrast = SystemParameters.HighContrast;
            AreAnimationsEnabled = SystemParameters.ClientAreaAnimation;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersStaticPropertyChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;
        }

        private void HotkeyEditorControl_Unloaded(object sender, RoutedEventArgs e) =>
            SystemParameters.StaticPropertyChanged -= OnSystemParametersStaticPropertyChanged;

        private void OnSystemParametersStaticPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.HighContrast))
                IsHighContrast = SystemParameters.HighContrast;
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                AreAnimationsEnabled = SystemParameters.ClientAreaAnimation;
        }

        private static void OnHotkeyConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not HotkeyEditorControl control) return;

            control._currentHotkey = (e.NewValue as HotkeyConfig)?.Clone() ?? new HotkeyConfig();
            if (!control.IsRecording)
                control.UpdateHotkeyText();

            control.OnPropertyChanged(nameof(HasHotkey));
            control.OnPropertyChanged(nameof(IsValid));
            control.UpdateRecorderHelpText();
        }

        private static void OnConflictingProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HotkeyEditorControl control)
                control.HasConflict = !string.IsNullOrEmpty(e.NewValue as string);
        }

        private void UpdateHotkeyText()
        {
            HotkeyText = _currentHotkey?.Key == Key.None
                ? string.Empty
                : _currentHotkey?.ToString() ?? string.Empty;
        }

        private void UpdateRecorderHelpText()
        {
            if (HasConflict)
                RecorderHelpText = $"Shortcut conflict. {ConflictingProfile ?? "Already assigned."}";
            else if (IsCommitPending)
                RecorderHelpText = "Shortcut captured. Saving shortcut.";
            else if (IsRecording)
                RecorderHelpText = "Recording shortcut. Press shortcut. Escape cancels.";
            else if (IsValid)
                RecorderHelpText = $"Shortcut set to {_currentHotkey}. Focus to record a different shortcut.";
            else
                RecorderHelpText = "Hotkey recorder. Focus to record a shortcut.";
        }

        private bool TryArm()
        {
            if (IsRecording || IsCommitPending)
            {
                return true;
            }

            IDisposable scope = null;
            try
            {
                scope = CaptureScopeFactory?.Invoke() ?? throw new InvalidOperationException("Hotkey capture suspension is unavailable.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "HotkeyEditorControl: failed to acquire capture suspension");
                HasConflict = true;
                RecorderHelpText = "Shortcut recording unavailable.";
                return false;
            }

            _captureScope = scope;
            _preArmHotkey = _currentHotkey?.Clone() ?? new HotkeyConfig();
            _pendingCandidate = null;
            _pressedKeys.Clear();
            HasConflict = false;
            IsRecording = true;
            HotkeyText = "Recording shortcut…";
            _logger.Debug("HotkeyEditorControl: armed recording after capture suspension");
            return true;
        }

        private void HotkeyTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!TryArm())
            {
                e.Handled = true;
                return;
            }

            if (!HotkeyTextBox.IsKeyboardFocused)
                HotkeyTextBox.Focus();
        }

        private void HotkeyTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!TryArm())
                Dispatcher.BeginInvoke(new Action(() => Keyboard.ClearFocus()));
        }

        private void HotkeyTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!IsCommitPending)
                CancelCapture();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Tab && !IsCommitPending)
            {
                CancelCapture();
                e.Handled = false;
                return;
            }

            e.Handled = true;

            if (IsCommitPending || !IsRecording)
                return;

            if (key == Key.Escape)
            {
                CancelCapture();
                return;
            }

            if (DpmKeyConverter.IsModifierKey(key))
            {
                UpdateRecordingDisplay();
                return;
            }

            _pressedKeys.Add(key);
            var modifiers = DpmKeyConverter.GetCurrentModifiers();
            var candidate = new HotkeyConfig(key, modifiers, true);
            if (!candidate.IsValid())
                return;

            _pendingCandidate = candidate.Clone();
            IsCommitPending = true;
            HotkeyText = candidate.ToString();
            HotkeyChanged?.Invoke(this, candidate.Clone());
            _logger.Debug($"HotkeyEditorControl: candidate pending parent acknowledgment: {candidate}");
        }

        private void HotkeyTextBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (!IsRecording || IsCommitPending) return;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            _pressedKeys.Remove(key);
            UpdateRecordingDisplay();
        }

        private void UpdateRecordingDisplay()
        {
            if (!IsRecording || IsCommitPending) return;

            var currentModifiers = DpmKeyConverter.GetCurrentModifiers();
            var parts = new List<string>();
            if ((currentModifiers & ModifierKeys.Control) == ModifierKeys.Control) parts.Add("Ctrl");
            if ((currentModifiers & ModifierKeys.Alt) == ModifierKeys.Alt) parts.Add("Alt");
            if ((currentModifiers & ModifierKeys.Shift) == ModifierKeys.Shift) parts.Add("Shift");
            if ((currentModifiers & ModifierKeys.Windows) == ModifierKeys.Windows) parts.Add("Win");

            HotkeyText = parts.Count > 0
                ? string.Join(" + ", parts) + " + …"
                : "Recording shortcut…";
        }

        internal bool BeginExternalCommit()
        {
            if (!IsRecording || IsCommitPending)
            {
                return false;
            }

            IsCommitPending = true;
            return true;
        }

        internal void AcceptPendingHotkey(HotkeyConfig accepted = null)
        {
            var value = accepted?.Clone() ?? _pendingCandidate?.Clone() ?? _currentHotkey?.Clone() ?? new HotkeyConfig();
            ApplyCommittedHotkey(value);
            FinishCapture();
        }

        internal void RejectPendingHotkey(HotkeyConfig accepted, string conflictingProfile = null)
        {
            ApplyCommittedHotkey(accepted?.Clone() ?? _preArmHotkey?.Clone() ?? new HotkeyConfig());
            ConflictingProfile = conflictingProfile;
            FinishCapture();
        }

        internal void CancelCapture()
        {
            if (!IsRecording && !IsCommitPending && _captureScope == null)
                return;

            ApplyCommittedHotkey(_preArmHotkey?.Clone() ?? _currentHotkey?.Clone() ?? new HotkeyConfig());
            FinishCapture();
            _logger.Debug("HotkeyEditorControl: capture cancelled");
        }

        private void ApplyCommittedHotkey(HotkeyConfig hotkey)
        {
            _currentHotkey = hotkey?.Clone() ?? new HotkeyConfig();
            SetCurrentValue(HotkeyConfigProperty, _currentHotkey.Clone());
            OnPropertyChanged(nameof(HasHotkey));
            OnPropertyChanged(nameof(IsValid));
        }

        private void FinishCapture()
        {
            _pendingCandidate = null;
            _pressedKeys.Clear();
            IsCommitPending = false;
            IsRecording = false;
            UpdateHotkeyText();

            var scope = _captureScope;
            _captureScope = null;
            scope?.Dispose();

            OnPropertyChanged(nameof(IsValid));
            UpdateRecorderHelpText();
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
