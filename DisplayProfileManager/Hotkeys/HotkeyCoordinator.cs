using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Hotkeys.Interop;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Hotkeys
{
    internal sealed class HotkeyCoordinator : IDisposable
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly IGlobalHotkeyRegistrar _registrar;
        private readonly Dispatcher _ownerDispatcher;
        private int _disposeRequested;
        private readonly ProfileService _profiles;
        private readonly ProfileApplyService _apply;
        private readonly SettingsService _settings;
        private readonly Action _showQuickSwitch;
        private readonly Action<string> _quickSwitchWarning;
        private readonly Action<Profile, ProfileApplyResult> _applyCompleted;

        private readonly HotkeySuspensionGate _captureSuspension;
        private readonly QuickSwitchHotkeyRegistration _quickSwitchRegistration;
        private HotkeyConfig _captureQuickSwitchFallback;
        private bool _started;
        private bool _revertingQuickSwitch;
        private bool _disposed;
        private bool _quickSwitchDisposed;
        private bool _profileHotkeysUnregistered;
        private bool _registrarDisposed;

        public HotkeyCoordinator(
            IGlobalHotkeyRegistrar registrar,
            ProfileService profiles,
            ProfileApplyService apply,
            SettingsService settings,
            Action showQuickSwitch,
            Action<string> quickSwitchWarning,
            Action<Profile, ProfileApplyResult> applyCompleted = null,
            Dispatcher ownerDispatcher = null)
        {
            _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
            _ownerDispatcher = ownerDispatcher ?? (registrar as GlobalHotkeyRegistrar)?.OwnerDispatcher
                ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (registrar is GlobalHotkeyRegistrar actualRegistrar &&
                !ReferenceEquals(_ownerDispatcher, actualRegistrar.OwnerDispatcher))
                throw new InvalidOperationException("Hotkey coordinator dispatcher must own registrar HWND");
            _captureSuspension = new HotkeySuspensionGate(_ownerDispatcher);
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _showQuickSwitch = showQuickSwitch ?? throw new ArgumentNullException(nameof(showQuickSwitch));
            _quickSwitchWarning = quickSwitchWarning;
            _applyCompleted = applyCompleted;
            _quickSwitchRegistration = new QuickSwitchHotkeyRegistration(_registrar, _showQuickSwitch);
        }

        public void Start()
        {
            if (!RunOnOwner(Start)) return;
            if (_started) return;
            _started = true;

            _profiles.ProfilesLoaded += OnProfilesLoaded;
            _profiles.ProfileAdded += OnProfileChanged;
            _profiles.ProfileUpdated += OnProfileChanged;
            _profiles.ProfileDeleted += OnProfileDeleted;
            _settings.SettingsChanged += OnSettingsChanged;
            RegisterAllProfileHotkeys();
            ReconcileQuickSwitchRegistration();
        }

        public void RegisterAllProfileHotkeys()
        {
            if (!RunOnOwner(RegisterAllProfileHotkeys)) return;
            if (_disposed || _captureSuspension.IsSuspended) return;

            _quickSwitchRegistration.Disable();

            try
            {
                var profileHotkeys = _profiles.GetAllHotkeys();
                if (profileHotkeys.Count > 0)
                {
                    _registrar.RegisterAllProfileHotkeys(profileHotkeys, CreateProfileHotkeyCallback);
                    _logger.Info($"Registered {profileHotkeys.Count} profile hotkeys");
                }
                else
                {
                    _registrar.UnregisterAllProfileHotkeys();
                    _logger.Info("No enabled profile hotkeys - unregistered all");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error registering profile hotkeys");
            }

            ReconcileQuickSwitchRegistration();
        }

        public IDisposable SuspendForHotkeyCapture()
        {
            EnsureCaptureOwner();
            return _captureSuspension.Enter(
                () =>
                {
                    _captureQuickSwitchFallback = _quickSwitchRegistration.IsRegistered
                        ? _quickSwitchRegistration.RegisteredHotkey?.Clone()
                        : null;
                    _registrar.UnregisterAllProfileHotkeys();
                    _quickSwitchRegistration.Disable();
                    _logger.Info("Suspended DPM global hotkeys for hotkey capture");
                },
                () =>
                {
                    if (_disposed) return;

                    RegisterAllProfileHotkeys();
                    _logger.Info("Resumed DPM global hotkeys after hotkey capture");
                });
        }

        private void EnsureCaptureOwner()
        {
            if (!_ownerDispatcher.CheckAccess() ||
                _ownerDispatcher.HasShutdownStarted || _ownerDispatcher.HasShutdownFinished ||
                Volatile.Read(ref _disposeRequested) != 0 || _disposed)
                throw new InvalidOperationException("Hotkey capture requires a live registrar owner dispatcher");
        }

        private bool RunOnOwner(Action action)
        {
            if (_ownerDispatcher.CheckAccess())
                return Volatile.Read(ref _disposeRequested) == 0;

            QueueOnOwner(action);
            return false;
        }

        private void QueueOnOwner(Action action)
        {
            if (_ownerDispatcher.HasShutdownStarted || _ownerDispatcher.HasShutdownFinished)
                return;

            try
            {
                _ownerDispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (!_ownerDispatcher.HasShutdownStarted && !_ownerDispatcher.HasShutdownFinished &&
                        (Volatile.Read(ref _disposeRequested) == 0 || action == (Action)Dispose))
                        action();
                }));
            }
            catch (InvalidOperationException)
            {
                _logger.Warn("Hotkey owner dispatcher unavailable");
            }
        }

        private Action CreateProfileHotkeyCallback(string profileId) => () => ApplyProfileViaHotkey(profileId);

        private async void ApplyProfileViaHotkey(string profileId)
        {
            try
            {
                var profile = _profiles.GetProfile(profileId);
                if (profile == null) return;

                _logger.Info($"Applying profile '{profile.Name}' via hotkey");
                var result = await _apply.ApplyProfileAsync(profile, ApplySource.Hotkey);
                _applyCompleted?.Invoke(profile, result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error applying profile {profileId} via hotkey");
            }
        }

        private void ReconcileQuickSwitchRegistration()
        {
            if (!RunOnOwner(ReconcileQuickSwitchRegistration)) return;
            if (_disposed || _captureSuspension.IsSuspended || _revertingQuickSwitch) return;

            bool enabled = _settings.IsQuickSwitchEnabled();
            HotkeyConfig requested = _settings.GetQuickSwitchHotkey();
            HotkeyConfig fallback = enabled
                ? _captureQuickSwitchFallback
                : null;
            var result = ReconcileQuickSwitchWithFallback(
                _quickSwitchRegistration,
                enabled,
                requested,
                fallback);
            _captureQuickSwitchFallback = null;

            if (result == QuickSwitchRegistrationResult.Registered)
                _logger.Info($"Registered Quick Switch hotkey: {_quickSwitchRegistration.RegisteredHotkey}");
            else if (result == QuickSwitchRegistrationResult.Disabled)
                _logger.Info("Quick Switch hotkey is disabled");
            else if (result == QuickSwitchRegistrationResult.Failed)
            {
                _logger.Warn($"Quick Switch hotkey registration failed: {requested}");
                string warning = BuildQuickSwitchRegistrationFailureWarning(requested, _quickSwitchRegistration);
                _quickSwitchWarning?.Invoke(warning);
                _ = RevertFailedQuickSwitchSettingAsync();
            }
        }

        internal static QuickSwitchRegistrationResult ReconcileQuickSwitchWithFallback(
            QuickSwitchHotkeyRegistration registration,
            bool enabled,
            HotkeyConfig requested,
            HotkeyConfig fallback)
        {
            var result = registration.Reconcile(enabled, requested);
            if (result != QuickSwitchRegistrationResult.Failed
                || !enabled
                || fallback?.IsValid() != true)
            {
                return result;
            }

            registration.Reconcile(true, fallback);
            return QuickSwitchRegistrationResult.Failed;
        }

        internal static string BuildQuickSwitchRegistrationFailureWarning(
            HotkeyConfig requested,
            QuickSwitchHotkeyRegistration registration) =>
            registration.IsRegistered
                ? $"Quick Switch hotkey '{requested}' is unavailable. The previous registration was preserved."
                : $"Quick Switch hotkey '{requested}' is invalid or unavailable. Quick Switch was disabled.";

        private async System.Threading.Tasks.Task RevertFailedQuickSwitchSettingAsync()
        {
            _revertingQuickSwitch = true;
            try
            {
                await RevertFailedQuickSwitchSettingAsync(_settings, _quickSwitchRegistration);
            }
            finally
            {
                _revertingQuickSwitch = false;
            }
        }

        internal static async System.Threading.Tasks.Task RevertFailedQuickSwitchSettingAsync(
            SettingsService settings,
            QuickSwitchHotkeyRegistration registration)
        {
            if (registration.IsRegistered && registration.RegisteredHotkey != null)
            {
                await settings.SetQuickSwitchHotkeyAsync(registration.RegisteredHotkey);
                await settings.SetQuickSwitchEnabledAsync(true);
            }
            else
            {
                await settings.SetQuickSwitchEnabledAsync(false);
            }
        }

        private void OnSettingsChanged(object sender, Settings.Models.AppSettings e) => ReconcileQuickSwitchRegistration();
        private void OnProfilesLoaded(object sender, EventArgs e) => RegisterAllProfileHotkeys();
        private void OnProfileChanged(object sender, Profile profile) => RegisterAllProfileHotkeys();

        private void OnProfileDeleted(object sender, string profileId)
        {
            if (!RunOnOwner(() => OnProfileDeleted(sender, profileId))) return;
            try
            {
                _registrar.UnregisterProfileHotkey(profileId);
                _logger.Info($"Unregistered hotkey for deleted profile: {profileId}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error unregistering hotkey for deleted profile {profileId}");
            }

            ReconcileQuickSwitchRegistration();
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (!_ownerDispatcher.CheckAccess())
            {
                if (_ownerDispatcher.HasShutdownStarted || _ownerDispatcher.HasShutdownFinished)
                    throw new InvalidOperationException("Hotkey owner dispatcher shut down before disposal");

                // A queued callback is not proof that the native owner has released its resources.
                try
                {
                    var operation = _ownerDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Dispose));
                    if (!operation.Task.Wait(TimeSpan.FromSeconds(5)))
                    {
                        operation.Abort();
                        throw new TimeoutException("Hotkey owner teardown did not finish within the completion bound");
                    }
                    operation.Task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Hotkey owner teardown did not complete", ex);
                }
                if (!_disposed)
                    throw new InvalidOperationException("Hotkey owner teardown remains incomplete");
                return;
            }

            if (_ownerDispatcher.HasShutdownStarted || _ownerDispatcher.HasShutdownFinished)
                throw new InvalidOperationException("Hotkey owner dispatcher shut down before disposal");

            Interlocked.Exchange(ref _disposeRequested, 1);
            _captureQuickSwitchFallback = null;
            _captureSuspension.Shutdown();

            if (_started)
            {
                _profiles.ProfilesLoaded -= OnProfilesLoaded;
                _profiles.ProfileAdded -= OnProfileChanged;
                _profiles.ProfileUpdated -= OnProfileChanged;
                _profiles.ProfileDeleted -= OnProfileDeleted;
                _settings.SettingsChanged -= OnSettingsChanged;
                _started = false;
            }

            var failures = new System.Collections.Generic.List<Exception>();
            if (!_quickSwitchDisposed)
            {
                try { _quickSwitchRegistration.Dispose(); _quickSwitchDisposed = true; }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (!_profileHotkeysUnregistered)
            {
                try { _registrar.UnregisterAllProfileHotkeys(); _profileHotkeysUnregistered = true; }
                catch (Exception ex) { failures.Add(ex); }
            }
            // The registrar must stay live until both dependent unregister stages complete.
            if (!_registrarDisposed && _quickSwitchDisposed && _profileHotkeysUnregistered)
            {
                try { _registrar.Dispose(); _registrarDisposed = true; }
                catch (Exception ex) { failures.Add(ex); }
            }

            _disposed = _quickSwitchDisposed && _profileHotkeysUnregistered && _registrarDisposed;
            if (failures.Count > 0)
                throw new AggregateException("Hotkey owner teardown incomplete", failures);
        }

    }

    internal sealed class HotkeySuspensionGate
    {
        private int _count;
        private bool _shutdown;
        private readonly Dispatcher _ownerDispatcher;

        internal HotkeySuspensionGate(Dispatcher ownerDispatcher)
        {
            _ownerDispatcher = ownerDispatcher ?? throw new ArgumentNullException(nameof(ownerDispatcher));
        }

        internal bool IsSuspended => _count > 0;

        internal IDisposable Enter(Action suspend, Action resume)
        {
            RequireOwner();
            if (_shutdown)
            {
                return SuspensionToken.Empty;
            }

            _count++;
            if (_count == 1)
            {
                try
                {
                    suspend?.Invoke();
                }
                catch
                {
                    _count--;
                    throw;
                }
            }

            return new SuspensionToken(this, resume);
        }

        internal void Shutdown()
        {
            RequireOwner();
            _shutdown = true;
            _count = 0;
        }

        private void Exit(Action resume)
        {
            RequireOwner();
            if (_shutdown || _count == 0) return;

            _count--;
            if (_count == 0)
                resume?.Invoke();
        }

        private void RequireOwner()
        {
            if (!_ownerDispatcher.CheckAccess())
                throw new InvalidOperationException("Hotkey suspension requires the registrar owner thread");
        }

        private sealed class SuspensionToken : IDisposable
        {
            internal static readonly IDisposable Empty = new SuspensionToken(null, null);

            private HotkeySuspensionGate _owner;
            private Action _resume;

            internal SuspensionToken(HotkeySuspensionGate owner, Action resume)
            {
                _owner = owner;
                _resume = resume;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;

                owner.RequireOwner();
                var resume = _resume;
                _owner = null;
                _resume = null;
                owner.Exit(resume);
            }
        }
    }
}
