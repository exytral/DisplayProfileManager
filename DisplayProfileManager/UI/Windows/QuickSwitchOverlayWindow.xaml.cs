using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;
using NLog;

using DisplayProfileManager.Application;
using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Settings;
using DisplayProfileManager.UI.Icons;
using DisplayProfileManager.UI.Interop;

namespace DisplayProfileManager.UI.Windows
{
    internal sealed class QuickSwitchItem : INotifyPropertyChanged
    {
        internal Profile Profile { get; }
        public string Name => Profile.Name;
        public ImageSource LayoutIcon { get; private set; }
        public ImageSource CustomIcon { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        internal QuickSwitchItem(Profile profile)
        {
            Profile = profile;
            CustomIcon = ProfileIconProvider.LoadImageSource(profile.Icon, 16);
            RefreshLayoutIcon();
        }

        internal void RefreshLayoutIcon()
        {
            LayoutIcon = SavedLayoutThumbnailRenderer.Create(Profile.DisplaySettings, 22);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LayoutIcon)));
        }
    }

    internal sealed class QuickSwitchSettingsRefreshCoordinator : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly Action _refresh;
        private int _disposed;

        internal QuickSwitchSettingsRefreshCoordinator(Dispatcher dispatcher, Action refresh)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        internal void RequestRefresh()
        {
            if (Volatile.Read(ref _disposed) != 0 || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return;

            if (_dispatcher.CheckAccess())
            {
                RefreshIfActive();
                return;
            }

            try
            {
                _dispatcher.BeginInvoke(new Action(RefreshIfActive), DispatcherPriority.DataBind);
            }
            catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
            }
        }

        private void RefreshIfActive()
        {
            if (Volatile.Read(ref _disposed) == 0)
                _refresh();
        }

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }

    public partial class QuickSwitchOverlayWindow : Window, IQuickSwitchOverlay
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private readonly ProfileService _profiles;
        private readonly ProfileApplyService _apply;
        private readonly SettingsService _settings;
        private readonly IThemeService _themeService;
        private readonly Action<Profile, ProfileApplyResult> _applyCompleted;
        private readonly ObservableCollection<QuickSwitchItem> _items = [];

        private QuickSwitchWorkArea? _targetWorkArea;
        private readonly QuickSwitchInteractionController _interaction;
        private readonly QuickSwitchProfileRefreshCoordinator _profileRefresh;
        private readonly QuickSwitchSettingsRefreshCoordinator _settingsRefresh;
        private int? _cornerHResult;
        private bool _cornerManagedFailure;
        private SystemBackdropNativeFrameResult _nativeFrameResult;
        private SystemBackdropFrameMetrics? _frameMetrics;
        private uint _visibleFrameBorderThickness;
        private uint _currentDpi = 96;
        private bool _preShowParkingSucceeded;
        private bool _placementDiagnosticLogged;
        private bool _settledDiagnosticLogged;
        private bool _stagingFailed;
        private bool _finalStagePlaced;
        private QuickSwitchWindowBounds? _finalStageBounds;
        private bool _finalStageRectReadSucceeded;
        private bool _finalStageRectExact;
        private bool _visibilityReleased;
        private bool _initialRevealAttempted;
        private bool _initialShowReturned;
        private bool _activationPending;

        internal QuickSwitchOverlayWindow(
            ProfileService profiles,
            ProfileApplyService apply,
            SettingsService settings,
            IThemeService themeService,
            Action<Profile, ProfileApplyResult> applyCompleted)
        {
            InitializeComponent();
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            _applyCompleted = applyCompleted;
            _interaction = new QuickSwitchInteractionController(
                (profile, source) => _apply.ApplyProfileAsync(profile, source),
                Close,
                _applyCompleted);
            _profileRefresh = new QuickSwitchProfileRefreshCoordinator(RefreshProfilesOnDispatcher);
            _settingsRefresh = new QuickSwitchSettingsRefreshCoordinator(Dispatcher, RefreshHotkeyPresentation);
            ProfileList.ItemsSource = _items;
            FooterIcon.Source = ApplicationIconProvider.LoadImageSource(16);
            RefreshHotkeyPresentation();
            _profiles.ProfilesLoaded += _profileRefresh.OnProfilesLoaded;
            _profiles.ProfileAdded += _profileRefresh.OnProfileAdded;
            _profiles.ProfileUpdated += _profileRefresh.OnProfileUpdated;
            _profiles.ProfileDeleted += _profileRefresh.OnProfileDeleted;
            _settings.SettingsChanged += OnSettingsChanged;
        }

        internal void RefreshAndActivate()
        {
            if (_interaction.IsDismissed)
                return;

            RefreshProfiles();
            RefreshHotkeyPresentation();
            _activationPending = true;
            if (!IsVisible)
            {
                _targetWorkArea = QuickSwitchWindowPlacement.CaptureTarget(
                    _settings.ShouldQuickSwitchUseActiveMonitor(),
                    _settings.GetQuickSwitchActiveMonitorSource());
                Show();
                _initialShowReturned = true;
                _logger.Debug(
                    $"Quick Switch lifecycle | phase=show-return isLoaded={IsLoaded} isVisible={IsVisible} " +
                    $"isActive={IsActive} opacity={Opacity} preShowParked={_preShowParkingSucceeded}");
                if (!_preShowParkingSucceeded)
                {
                    Close();
                    return;
                }

                if (IsLoaded && !_visibilityReleased && !_stagingFailed)
                    TryCompleteInitialStaticFinalReveal();
            }

            TryActivateAfterReveal();
        }

        event EventHandler IQuickSwitchOverlay.Closed
        {
            add => Closed += value;
            remove => Closed -= value;
        }

        void IQuickSwitchOverlay.RefreshAndActivate() => RefreshAndActivate();
        void IQuickSwitchOverlay.Close() => Close();

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            _preShowParkingSucceeded =
                _targetWorkArea.HasValue &&
                QuickSwitchWindowPlacement.TryParkOffscreen(this, _targetWorkArea.Value);
            if (!_preShowParkingSucceeded)
            {
                FailClosedStaging("pre-show-park");
                return;
            }

            // Static-final reveal uses XAML opacity plus pre-show parking only.
            // The real WPF HWND is never application-cloaked or moved through an entrance path.
            Opacity = 0.0;
            ContentRendered += OnInitialContentRendered;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _themeService.ThemeChanged += OnThemeChanged;

            _nativeFrameResult = SystemBackdrop.TryPrepareQuickSwitchNativeFrame(hwnd);
            if (_nativeFrameResult.NativeFrameReady)
            {
                if (SystemBackdrop.TryGetFrameMetrics(hwnd, out var frameMetrics))
                    _frameMetrics = frameMetrics;
                SystemBackdrop.TryApplyRoundedCorners(hwnd, out _cornerHResult, out _cornerManagedFailure);
            }

            RefreshOuterEdgePolicy(hwnd, SystemParameters.HighContrast, _nativeFrameResult.NativeFrameReady);
            ApplyOpaqueBrushes();
            LogCreation();

            Loaded += OnInitialLoaded;
            _logger.Debug(
                $"Quick Switch lifecycle | phase=loaded-subscribed isLoaded={IsLoaded} isVisible={IsVisible} " +
                $"isActive={IsActive} opacity={Opacity} hwnd=0x{hwnd.ToInt64():X}");
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            RefreshOuterEdgePolicy(
                new WindowInteropHelper(this).Handle,
                SystemParameters.HighContrast,
                _nativeFrameResult.NativeFrameReady);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _logger.Debug(
                $"Quick Switch lifecycle | phase=activated isLoaded={IsLoaded} isVisible={IsVisible} " +
                $"opacity={Opacity} visibilityReleased={_visibilityReleased}");
        }

        protected override void OnClosed(EventArgs e)
        {
            _logger.Debug(
                $"Quick Switch lifecycle | phase=closed isLoaded={IsLoaded} isVisible={IsVisible} " +
                $"opacity={Opacity} visibilityReleased={_visibilityReleased} stagingFailed={_stagingFailed}");
            Loaded -= OnInitialLoaded;
            ContentRendered -= OnInitialContentRendered;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _themeService.ThemeChanged -= OnThemeChanged;
            _profiles.ProfilesLoaded -= _profileRefresh.OnProfilesLoaded;
            _profiles.ProfileAdded -= _profileRefresh.OnProfileAdded;
            _profiles.ProfileUpdated -= _profileRefresh.OnProfileUpdated;
            _profiles.ProfileDeleted -= _profileRefresh.OnProfileDeleted;
            _settingsRefresh.Dispose();
            _settings.SettingsChanged -= OnSettingsChanged;
            base.OnClosed(e);
        }

        private void RefreshProfiles()
        {
            string selectedId = (ProfileList.SelectedItem as QuickSwitchItem)?.Profile.Id;
            string currentId = _profiles.CurrentProfileId;
            var profiles = _profiles.GetAllProfiles()
                .OrderBy(profile => profile.Name, NaturalStringComparer.Instance)
                .ToList();

            _items.Clear();
            foreach (var profile in profiles)
                _items.Add(new QuickSwitchItem(profile));

            int index = QuickSwitchSelectionPolicy.GetInitialIndex(
                profiles,
                currentId,
                selectedId);

            ProfileList.SelectedIndex = index;
            if (index >= 0)
                ProfileList.ScrollIntoView(ProfileList.SelectedItem);
        }

        private void RefreshHotkeyPresentation() =>
            HotkeyPanel.ItemsSource = QuickSwitchHotkeyPresentation.Build(_settings.GetQuickSwitchHotkey());

        private bool PositionOnWorkArea()
        {
            _targetWorkArea ??= QuickSwitchWindowPlacement.CaptureTarget(
                _settings.ShouldQuickSwitchUseActiveMonitor(),
                _settings.GetQuickSwitchActiveMonitorSource());
            return _targetWorkArea.HasValue &&
                   QuickSwitchWindowPlacement.TryPlace(this, _targetWorkArea.Value);
        }

        private void OnInitialLoaded(object sender, RoutedEventArgs e)
        {
            _logger.Debug(
                $"Quick Switch lifecycle | phase=loaded-entry isLoaded={IsLoaded} isVisible={IsVisible} " +
                $"isActive={IsActive} opacity={Opacity} actual={ActualWidth}x{ActualHeight} " +
                $"targetWorkArea={(_targetWorkArea.HasValue ? "present" : "missing")}");
            Loaded -= OnInitialLoaded;

            try
            {
                TryCompleteInitialStaticFinalReveal();
                TryActivateAfterReveal();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    $"Quick Switch lifecycle | phase=loaded-exception type={ex.GetType().FullName} message={ex.Message}");
                throw;
            }
        }

        private void TryCompleteInitialStaticFinalReveal()
        {
            if (_initialRevealAttempted)
                return;

            _initialRevealAttempted = true;
            if (_stagingFailed ||
                !_preShowParkingSucceeded ||
                !_targetWorkArea.HasValue ||
                ActualWidth <= 0 ||
                ActualHeight <= 0)
            {
                FailClosedStaging("final-placement-prerequisite");
                return;
            }

            var finalBounds = QuickSwitchWindowPlacement.CalculateBounds(
                _targetWorkArea.Value,
                new Size(ActualWidth, ActualHeight));
            LogPlacementState(_targetWorkArea.Value, finalBounds);

            _finalStageBounds = finalBounds;
            _finalStagePlaced = QuickSwitchWindowPlacement.TryPlace(this, finalBounds);
            if (!_finalStagePlaced)
            {
                FailClosedStaging("final-placement");
                return;
            }

            if (!QuickSwitchWindowPlacement.TryGetWindowBounds(this, out var actualBounds))
            {
                _finalStageRectReadSucceeded = false;
                FailClosedStaging("final-rect-read");
                return;
            }

            _finalStageRectReadSucceeded = true;
            _finalStageRectExact = actualBounds == finalBounds;
            if (!_finalStageRectExact)
            {
                FailClosedStaging("final-rect-mismatch");
                return;
            }

            Opacity = 1.0;
            _visibilityReleased = true;
        }

        private void TryActivateAfterReveal()
        {
            if (_interaction.IsDismissed ||
                !_activationPending || !_initialShowReturned || !_visibilityReleased || _stagingFailed)
                return;

            _activationPending = false;
            Activate();
            Focus();
            ProfileList.Focus();
        }

        private void FailClosedStaging(string reason)
        {
            if (_stagingFailed)
                return;

            _stagingFailed = true;
            Opacity = 0.0;
            _visibilityReleased = false;
            _logger.Warn(
                $"Quick Switch static-final reveal failed | reason={reason} " +
                $"finalPlacementAttempted={_finalStageBounds.HasValue} finalPlacementSucceeded={_finalStagePlaced} " +
                $"finalRectExpected={FormatBounds(_finalStageBounds)} finalRectReadSucceeded={_finalStageRectReadSucceeded} " +
                $"finalRectExact={_finalStageRectExact} visibilityReleased={_visibilityReleased}");
            Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Loaded);
        }

        private void LogPlacementState(QuickSwitchWorkArea workArea, QuickSwitchWindowBounds finalBounds)
        {
            if (_placementDiagnosticLogged)
                return;

            var monitor = workArea.Monitor;
            var rawWork = workArea.RawWork;
            var autoHide = workArea.AutoHideEdges;
            string targetPolicy = workArea.TargetSource == QuickSwitchTargetSource.Primary && !workArea.TargetFallbackToPrimary
                ? "targetPolicy=Primary"
                : $"targetPolicy=Active targetSource={workArea.TargetSource}" +
                  $"{(workArea.TargetFallbackToPrimary ? " targetFallback=Primary" : string.Empty)}";
            _logger.Debug(
                $"Quick Switch placement | {targetPolicy} rcMonitor={monitor.Left},{monitor.Top},{monitor.Right},{monitor.Bottom} " +
                $"rcWork={rawWork.Left},{rawWork.Top},{rawWork.Right},{rawWork.Bottom} " +
                $"effectiveWork={workArea.Left},{workArea.Top},{workArea.Right},{workArea.Bottom} " +
                $"targetDpi={workArea.Dpi} foregroundDpi={workArea.ForegroundDpi?.ToString() ?? "unavailable"} " +
                $"autoHideLeft={FormatAutoHide(autoHide.Left)} autoHideTop={FormatAutoHide(autoHide.Top)} " +
                $"autoHideRight={FormatAutoHide(autoHide.Right)} autoHideBottom={FormatAutoHide(autoHide.Bottom)} " +
                $"shellTaskbar={FormatShellTaskbar(workArea.ShellTaskbar)} " +
                $"final={finalBounds.X},{finalBounds.Y},{finalBounds.Width},{finalBounds.Height} " +
                $"entranceRoute=StaticFinal");

            _placementDiagnosticLogged = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var action = QuickSwitchSelectionPolicy.GetInputAction(e.Key, _items.Count);
            if (action == QuickSwitchInputAction.None) return;

            e.Handled = true;

            if (action == QuickSwitchInputAction.Dismiss)
            {
                _interaction.Dismiss();
                return;
            }

            if (action == QuickSwitchInputAction.MoveUp || action == QuickSwitchInputAction.MoveDown)
            {
                int delta = action == QuickSwitchInputAction.MoveDown ? 1 : -1;
                ProfileList.SelectedIndex = QuickSwitchSelectionPolicy.Move(
                    ProfileList.SelectedIndex,
                    _items.Count,
                    delta);
                ProfileList.ScrollIntoView(ProfileList.SelectedItem);
                return;
            }

            if (action == QuickSwitchInputAction.Apply)
                _ = ApplySelectedAsync();
        }

        private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_visibilityReleased || ProfileList.SelectedItem == null) return;
            ProfileList.ScrollIntoView(ProfileList.SelectedItem);
        }

        private void ProfileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(ProfileList, e.OriginalSource as DependencyObject) is ListBoxItem item)
            {
                ProfileList.SelectedItem = item.DataContext;
                e.Handled = true;
                _ = ApplySelectedAsync();
            }
        }

        private async Task ApplySelectedAsync()
        {
            if (ProfileList.SelectedItem is not QuickSwitchItem selected) return;

            Profile profile = selected.Profile;
            try
            {
                _logger.Info($"Applying profile '{profile.Name}' via Quick Switch");
                await _interaction.ApplyAsync(profile);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error applying profile {profile.Id} via Quick Switch");
            }
        }

        private void OpenDpmButton_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current is App app)
                _interaction.DismissAndExecute(app.ShowMainWindowFromQuickSwitch);
            else
                _interaction.Dismiss();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            _logger.Debug(
                $"Quick Switch lifecycle | phase=deactivated isLoaded={IsLoaded} isVisible={IsVisible} " +
                $"opacity={Opacity} visibilityReleased={_visibilityReleased}");
            _interaction.Dismiss(ScheduleDeactivationClose);
        }

        private void ScheduleDeactivationClose()
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!IsLoaded || !IsVisible)
                        return;

                    Close();
                }),
                DispatcherPriority.Background);
        }

        private void RefreshProfilesOnDispatcher()
        {
            if (Dispatcher.CheckAccess())
                RefreshProfiles();
            else
                Dispatcher.BeginInvoke(new Action(RefreshProfiles));
        }

        private void OnInitialContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= OnInitialContentRendered;
            if (_stagingFailed || !_visibilityReleased)
                return;

            Dispatcher.BeginInvoke(new Action(LogSettledPresentation), DispatcherPriority.ContextIdle);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            void RefreshThemePresentation()
            {
                RefreshGeneratedLayoutImages();
                ApplyOpaqueBrushes();
            }

            if (Dispatcher.CheckAccess())
                RefreshThemePresentation();
            else
                Dispatcher.BeginInvoke(new Action(RefreshThemePresentation));
        }

        private void RefreshGeneratedLayoutImages()
        {
            foreach (var item in _items)
                item.RefreshLayoutIcon();
        }

        private void OnSettingsChanged(object sender, Settings.Models.AppSettings e) =>
            _settingsRefresh.RequestRefresh();

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            void RefreshPreferencePresentation()
            {
                _targetWorkArea = QuickSwitchWindowPlacement.CaptureForWindow(this) ?? _targetWorkArea;
                PositionOnWorkArea();

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                RefreshOuterEdgePolicy(hwnd, SystemParameters.HighContrast, _nativeFrameResult.NativeFrameReady);
                ApplyOpaqueBrushes();
            }

            if (Dispatcher.CheckAccess())
                RefreshPreferencePresentation();
            else
                Dispatcher.BeginInvoke(new Action(RefreshPreferencePresentation));
        }

        private void RefreshOuterEdgePolicy(IntPtr hwnd, bool highContrast, bool nativeFrameReady)
        {
            uint reportedVisibleFrameThickness = 0;
            if (hwnd != IntPtr.Zero)
                SystemBackdrop.TryGetVisibleFrameBorderThickness(hwnd, out reportedVisibleFrameThickness);
            _visibleFrameBorderThickness = SystemBackdrop.GetQualifiedVisibleFrameThickness(
                nativeFrameReady,
                reportedVisibleFrameThickness);

            _currentDpi = (uint)Math.Max(1.0, Math.Round(VisualTreeHelper.GetDpi(this).DpiScaleX * 96.0));
            double thickness = SystemBackdrop.GetWpfOuterBorderThicknessDip(
                _visibleFrameBorderThickness,
                _currentDpi,
                highContrast);
            BackdropHost.BorderThickness = new Thickness(thickness);
        }

        private void LogCreation()
        {
            string cornerHResult = FormatHResult(_cornerHResult);
            string cornerFailureKind = FormatNativeFailureKind(_cornerHResult, _cornerManagedFailure);
            string windowRect = _frameMetrics.HasValue
                ? $"{_frameMetrics.Value.WindowLeft},{_frameMetrics.Value.WindowTop},{_frameMetrics.Value.WindowRight},{_frameMetrics.Value.WindowBottom}"
                : "unavailable";
            string clientRect = _frameMetrics.HasValue
                ? $"{_frameMetrics.Value.ClientLeft},{_frameMetrics.Value.ClientTop},{_frameMetrics.Value.ClientRight},{_frameMetrics.Value.ClientBottom}"
                : "unavailable";
            string nonClient = _frameMetrics.HasValue
                ? $"{_frameMetrics.Value.LeftMargin},{_frameMetrics.Value.TopMargin},{_frameMetrics.Value.RightMargin},{_frameMetrics.Value.BottomMargin}"
                : "unavailable";

            _logger.Debug(
                $"Quick Switch frame | preShowParked={_preShowParkingSucceeded} " +
                $"styleReadSucceeded={_nativeFrameResult.StyleReadSucceeded} " +
                $"rawStyle={(_nativeFrameResult.StyleReadSucceeded ? $"0x{_nativeFrameResult.RawStyle:X}" : "unavailable")} " +
                $"styleWriteSucceeded={_nativeFrameResult.StyleWriteSucceeded} frameChangedSucceeded={_nativeFrameResult.FrameChangedSucceeded} " +
                $"confirmedStyleReadSucceeded={_nativeFrameResult.ConfirmedStyleReadSucceeded} " +
                $"confirmedStyle={(_nativeFrameResult.ConfirmedStyleReadSucceeded ? $"0x{_nativeFrameResult.ConfirmedStyle:X}" : "unavailable")} " +
                $"wsBorderConfirmed={_nativeFrameResult.HasConfirmedBorder} wsCaptionConfirmed={_nativeFrameResult.HasConfirmedCaption} " +
                $"wsThickFrameConfirmed={_nativeFrameResult.HasConfirmedThickFrame} nativeFrameReady={_nativeFrameResult.NativeFrameReady} " +
                $"windowRect={windowRect} clientRect={clientRect} ncMargins={nonClient} " +
                $"cornerHresult={cornerHResult} cornerFailureKind={cornerFailureKind} " +
                $"frameBorderPx={_visibleFrameBorderThickness} dpi={_currentDpi} entranceRoute=StaticFinal");
        }

        private void LogSettledPresentation()
        {
            if (_settledDiagnosticLogged)
                return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var styleRead = SystemBackdrop.TryGetWindowStyleState(hwnd);
            bool metricsReadSucceeded = SystemBackdrop.TryGetFrameMetrics(hwnd, out var frameMetrics);
            bool visibleFrameReadSucceeded = SystemBackdrop.TryGetVisibleFrameBorderThickness(hwnd, out uint visibleFrameThickness);
            bool cornerReadSucceeded = SystemBackdrop.TryGetWindowCornerPreference(hwnd, out uint cornerPreference);

            string style = SystemBackdrop.FormatNativeReadback(styleRead.Succeeded, styleRead.Style, hexadecimal: true);
            string visibleFrame = SystemBackdrop.FormatNativeReadback(visibleFrameReadSucceeded, visibleFrameThickness);
            string corner = SystemBackdrop.FormatNativeReadback(cornerReadSucceeded, cornerPreference);
            string windowRect = metricsReadSucceeded
                ? $"{frameMetrics.WindowLeft},{frameMetrics.WindowTop},{frameMetrics.WindowRight},{frameMetrics.WindowBottom}"
                : "unavailable";
            string clientRect = metricsReadSucceeded
                ? $"{frameMetrics.ClientLeft},{frameMetrics.ClientTop},{frameMetrics.ClientRight},{frameMetrics.ClientBottom}"
                : "unavailable";
            string nonClient = metricsReadSucceeded
                ? $"{frameMetrics.LeftMargin},{frameMetrics.TopMargin},{frameMetrics.RightMargin},{frameMetrics.BottomMargin}"
                : "unavailable";
            string wsBorder = styleRead.Succeeded
                ? ((styleRead.Style & SystemBackdrop.WsBorder) != 0).ToString()
                : "unavailable";
            string wsCaption = styleRead.Succeeded
                ? ((styleRead.Style & SystemBackdrop.WsCaption) == SystemBackdrop.WsCaption).ToString()
                : "unavailable";
            string wsThickFrame = styleRead.Succeeded
                ? ((styleRead.Style & SystemBackdrop.WsThickFrame) != 0).ToString()
                : "unavailable";

            _logger.Debug(
                $"Quick Switch settled | entranceRoute=StaticFinal active={IsActive} " +
                $"preShowParked={_preShowParkingSucceeded} finalPlacementAttempted={_finalStageBounds.HasValue} " +
                $"finalPlacementSucceeded={_finalStagePlaced} finalRectExpected={FormatBounds(_finalStageBounds)} " +
                $"finalRectReadSucceeded={_finalStageRectReadSucceeded} finalRectExact={_finalStageRectExact} " +
                $"visibilityReleased={_visibilityReleased} " +
                $"windowBackgroundType={GetBrushType(Background)} windowBackgroundAlpha={GetBrushAlpha(Background)} " +
                $"backdropHostType={GetBrushType(BackdropHost.Background)} backdropHostAlpha={GetBrushAlpha(BackdropHost.Background)} " +
                $"styleReadSucceeded={styleRead.Succeeded} style={style} wsBorder={wsBorder} wsCaption={wsCaption} wsThickFrame={wsThickFrame} " +
                $"frameMetricsReadSucceeded={metricsReadSucceeded} windowRect={windowRect} clientRect={clientRect} ncMargins={nonClient} " +
                $"visibleFrameReadSucceeded={visibleFrameReadSucceeded} visibleFramePx={visibleFrame} " +
                $"cornerReadSucceeded={cornerReadSucceeded} cornerPreference={corner}");

            _settledDiagnosticLogged = true;
        }

        private static string GetBrushType(Brush brush) =>
            brush?.GetType().Name ?? "null";

        private static string GetBrushAlpha(Brush brush) =>
            brush is SolidColorBrush solid ? solid.Color.A.ToString() : "unavailable";

        private static string FormatBounds(QuickSwitchWindowBounds? bounds) =>
            bounds.HasValue
                ? $"{bounds.Value.X},{bounds.Value.Y},{bounds.Value.Width},{bounds.Value.Height}"
                : "not-attempted";

        private static string FormatHResult(int? hResult) =>
            hResult.HasValue ? $"0x{hResult.Value:X8}" : "not-attempted";

        private static string FormatNativeFailureKind(int? hResult, bool managedFailure)
        {
            if (!hResult.HasValue)
            {
                return "not-attempted";
            }
            if (managedFailure)
            {
                return "ManagedInterop";
            }
            return hResult.Value == 0 ? "None" : "NativeHResult";
        }

        private void ApplyOpaqueBrushes()
        {
            Brush fallback = CreateOpaqueFallbackBrush();
            Background = fallback;
            BackdropHost.Background = fallback;
        }

        internal static Color GetOpaqueFallbackColor(Color color) =>
            Color.FromRgb(color.R, color.G, color.B);

        private Brush CreateOpaqueFallbackBrush()
        {
            if (TryFindResource("WindowBackgroundBrush") is SolidColorBrush brush)
            {
                return new SolidColorBrush(GetOpaqueFallbackColor(brush.Color));
            }

            return SystemColors.WindowBrush;
        }

        private static string FormatShellTaskbar(QuickSwitchShellTaskbar taskbar)
        {
            if (!taskbar.Present)
            {
                return "None";
            }
            if (!taskbar.GeometryAvailable)
            {
                return $"hwnd=0x{taskbar.Hwnd.ToInt64():X},class={taskbar.ClassName},geometry=unavailable";
            }

            var bounds = taskbar.Bounds;
            string source = taskbar.UsedExtendedFrameBounds ? "dwm" : "window";
            return $"hwnd=0x{taskbar.Hwnd.ToInt64():X},class={taskbar.ClassName},geometry={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom},source={source},edge={taskbar.Edge},qualified=True";
        }

        private static string FormatAutoHide(QuickSwitchAutoHideEdge edge)
        {
            if (!edge.Present)
            {
                return "None";
            }
            if (!edge.GeometryAvailable)
            {
                return $"hwnd=0x{edge.Hwnd.ToInt64():X},geometry=unavailable";
            }

            var bounds = edge.Bounds;
            string source = edge.UsedExtendedFrameBounds ? "dwm" : "window";
            return $"hwnd=0x{edge.Hwnd.ToInt64():X},geometry={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom},source={source}";
        }
    }
}
