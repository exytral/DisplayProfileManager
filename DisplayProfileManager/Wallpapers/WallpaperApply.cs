using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Presentation;
using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;
using static DisplayProfileManager.Wallpapers.Interop.DesktopWallpaperNative;
using static DisplayProfileManager.Wallpapers.Interop.WindowsWallpaperPlatform;
using static DisplayProfileManager.Wallpapers.Policy.WallpaperMonitorMapper;
using static DisplayProfileManager.Wallpapers.SpotlightProvider;

namespace DisplayProfileManager.Wallpapers
{
    internal enum WallpaperApplyOutcome
    {
        Success,
        Partial,
        Failed,
    }

    internal sealed class PictureAssignment(string deviceName, string monitorId, string path)
    {
        public string DeviceName { get; } = deviceName;
        public string MonitorId { get; } = monitorId;
        public string Path { get; } = path;
    }

    internal sealed class PictureRollbackAssignment(string monitorId, string path)
    {
        public string MonitorId { get; } = monitorId;
        public string Path { get; } = path;
    }

    internal sealed class PictureRollbackSnapshot
    {
        public List<PictureRollbackAssignment> Assignments { get; } = [];
    }

    internal sealed class PicturePreflightResult
    {
        public List<PictureAssignment> Assignments { get; } = [];
        public int RequestedCount { get; internal set; }
        public int DisconnectedCount { get; internal set; }
        public int MissingCount { get; internal set; }
        public bool HasViableAssignments => Assignments.Count > 0;
        public bool HasUnavailableLiveAssignments => DisconnectedCount > 0 || MissingCount > 0;
    }

    internal static class WallpaperApply
    {
        internal static readonly Logger _logger = LoggerHelper.GetLogger();
        internal static readonly uint[] _standardIntervalSeconds = [60, 600, 1800, 3600, 21600, 86400];
        internal const int SlideshowSettlementMaxAttempts = 5;
        internal const int SlideshowSettlementDelayMs = 50;

        internal sealed class SlideshowSourceHandle(string folder, IShellItem item, IShellItemArray array) : IDisposable
        {
            public string Folder { get; } = folder;
            public IShellItem Item { get; } = item;
            public IShellItemArray Array { get; } = array;

            public void Dispose()
            {
                if (Array != null) Marshal.ReleaseComObject(Array);
                if (Item != null) Marshal.ReleaseComObject(Item);
            }
        }

        internal static PicturePreflightResult PreflightPictureAssignments(WallpaperSettings snapshot, IReadOnlyDictionary<string, string> monitorMap, Func<string, bool> fileExists)
        {
            var result = new PicturePreflightResult();
            if (snapshot?.PerMonitor == null || monitorMap == null || fileExists == null)
            {
                return result;
            }

            var currentByMonitorId = monitorMap
                .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
                .Select(kvp => new
                {
                    DeviceName = kvp.Key,
                    MonitorId = kvp.Value,
                    NormalizedMonitorId = NormalizeMonitorDevicePath(kvp.Value)
                })
                .Where(item => item.NormalizedMonitorId.Length > 0)
                .GroupBy(item => item.NormalizedMonitorId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var storedMonitorIdCounts = snapshot.PerMonitor.Values
                .Where(value => value?.MonitorId != null)
                .Select(value => NormalizeMonitorDevicePath(value.MonitorId))
                .Where(monitorId => monitorId.Length > 0)
                .GroupBy(monitorId => monitorId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var claimedMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in snapshot.PerMonitor)
            {
                result.RequestedCount++;

                string deviceName;
                string monitorId;
                if (kvp.Value?.MonitorId != null)
                {
                    string storedMonitorId = NormalizeMonitorDevicePath(kvp.Value.MonitorId);
                    if (storedMonitorId.Length == 0 ||
                        !storedMonitorIdCounts.TryGetValue(storedMonitorId, out int storedCount) ||
                        storedCount != 1 ||
                        !currentByMonitorId.TryGetValue(storedMonitorId, out var currentMatches) ||
                        currentMatches.Count != 1 ||
                        !claimedMonitorIds.Add(storedMonitorId))
                    {
                        result.DisconnectedCount++;
                        continue;
                    }

                    deviceName = currentMatches[0].DeviceName;
                    monitorId = currentMatches[0].MonitorId;
                }
                else
                {
                    deviceName = kvp.Key;
                    if (!monitorMap.TryGetValue(deviceName, out monitorId) || string.IsNullOrEmpty(monitorId))
                    {
                        result.DisconnectedCount++;
                        continue;
                    }
                }

                string path = kvp.Value?.Path;
                if (string.IsNullOrEmpty(path) || !fileExists(path))
                {
                    result.MissingCount++;
                    continue;
                }

                result.Assignments.Add(new PictureAssignment(deviceName, monitorId, path));
            }

            return result;
        }

        internal static WallpaperApplyOutcome ClassifyPictureApplyOutcome(PicturePreflightResult preflight, int applied, int setterFailures)
        {
            if (preflight == null || applied == 0)
            {
                return WallpaperApplyOutcome.Failed;
            }

            return setterFailures > 0 || preflight.HasUnavailableLiveAssignments
                ? WallpaperApplyOutcome.Partial
                : WallpaperApplyOutcome.Success;
        }

        internal static bool TryCapturePictureRollbackSnapshot(IEnumerable<string> monitorIds, Func<string, string> getWallpaper, out PictureRollbackSnapshot snapshot)
        {
            snapshot = null;
            if (monitorIds == null || getWallpaper == null)
            {
                return false;
            }

            var ids = monitorIds.ToList();
            var normalizedIds = ids.Select(NormalizeMonitorDevicePath).ToList();
            if (ids.Count == 0 ||
                normalizedIds.Any(string.IsNullOrEmpty) ||
                normalizedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            {
                return false;
            }

            var captured = new PictureRollbackSnapshot();
            foreach (string monitorId in ids)
            {
                try
                {
                    string path = getWallpaper(monitorId);
                    if (string.IsNullOrEmpty(path))
                    {
                        _logger.Warn($"Could not capture prior Picture wallpaper for {monitorId}: rendered path is unavailable");
                        return false;
                    }

                    captured.Assignments.Add(new PictureRollbackAssignment(monitorId, path));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not capture prior Picture wallpaper for {monitorId}");
                    return false;
                }
            }

            snapshot = captured;
            return true;
        }

        internal static bool TryCapturePictureSharedRollbackState(
            Func<uint> getBackgroundColor,
            Func<DesktopWallpaperPosition> getPosition,
            out uint backgroundColor,
            out DesktopWallpaperPosition position)
        {
            backgroundColor = 0;
            position = DesktopWallpaperPosition.Fill;
            if (getBackgroundColor == null || getPosition == null)
            {
                return false;
            }

            try
            {
                uint capturedColor = getBackgroundColor();
                DesktopWallpaperPosition capturedPosition = getPosition();
                backgroundColor = capturedColor;
                position = capturedPosition;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Picture source shared rollback state could not be captured before mutation");
                return false;
            }
        }

        internal static bool PreparePictureSourceRecovery(
            WallpaperMode sourceMode,
            IEnumerable<string> monitorIds,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback)
        {
            if (sourceMode != WallpaperMode.Picture)
            {
                return true;
            }

            if (rememberPictureRollback == null ||
                !TryCapturePictureRollbackSnapshot(monitorIds, getWallpaper, out PictureRollbackSnapshot snapshot))
            {
                _logger.Warn("Picture source recovery state could not be captured before mutation");
                return false;
            }

            try
            {
                rememberPictureRollback(snapshot);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Picture source recovery state could not be retained before mutation");
                return false;
            }
        }

        internal static bool RestorePictureRollbackSnapshot(PictureRollbackSnapshot snapshot, Action<string, string> setWallpaper)
        {
            if (snapshot?.Assignments == null || snapshot.Assignments.Count == 0 || setWallpaper == null)
            {
                return false;
            }

            bool complete = true;
            foreach (var assignment in snapshot.Assignments)
            {
                try
                {
                    setWallpaper(assignment.MonitorId, assignment.Path);
                }
                catch (Exception ex)
                {
                    complete = false;
                    _logger.Warn(ex, $"Could not restore prior Picture wallpaper for {assignment.MonitorId}");
                }
            }

            return complete;
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            IEnumerable<DisplayConfigInfo> currentDisplays,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<IReadOnlyList<string>, WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null ||
                !WallpaperMonitorMapper.TryResolveCurrentActiveSpotlightMonitorIds(
                    currentDisplays,
                    getMonitorCount,
                    getMonitorDevicePathAt,
                    getMonitorRect,
                    out List<string> currentActiveMonitorIds))
            {
                _logger.Warn("Spotlight apply aborted before mutation: the current-active wallpaper monitor domain could not be established authoritatively.");
                return WallpaperApplyOutcome.Failed;
            }

            return PrepareSpotlightDestinationApply(
                sourceMode,
                currentActiveMonitorIds,
                getWallpaper,
                rememberPictureRollback,
                () => activateSpotlight(currentActiveMonitorIds));
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<IReadOnlyList<string>, WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null ||
                !TryGetAttachedDesktopMonitorIdsStrict(
                    getMonitorCount,
                    getMonitorDevicePathAt,
                    getMonitorRect,
                    out List<string> attachedMonitorIds) ||
                attachedMonitorIds.Count == 0)
            {
                _logger.Warn("Spotlight apply aborted before mutation: the attached wallpaper monitor domain could not be established authoritatively.");
                return WallpaperApplyOutcome.Failed;
            }

            return PrepareSpotlightDestinationApply(
                sourceMode,
                attachedMonitorIds,
                getWallpaper,
                rememberPictureRollback,
                () => activateSpotlight(attachedMonitorIds));
        }

        internal static WallpaperApplyOutcome PrepareSpotlightDestinationApply(
            WallpaperMode sourceMode,
            IEnumerable<string> attachedMonitorIds,
            Func<string, string> getWallpaper,
            Action<PictureRollbackSnapshot> rememberPictureRollback,
            Func<WallpaperApplyOutcome> activateSpotlight)
        {
            if (activateSpotlight == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            if (!PreparePictureSourceRecovery(sourceMode, attachedMonitorIds, getWallpaper, rememberPictureRollback))
            {
                _logger.Warn("Picture -> Spotlight aborted before mutation: exact per-monitor Picture rollback state could not be captured");
                return WallpaperApplyOutcome.Failed;
            }

            return activateSpotlight();
        }

        internal static string SelectSlideshowSource(IEnumerable<string> paths, Func<string, bool> directoryExists)
        {
            if (paths == null || directoryExists == null)
            {
                return null;
            }

            return paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && directoryExists(path));
        }

        internal static WallpaperApplyOutcome ExecuteTransitionSteps(
            WallpaperTransitionPlan plan,
            Func<WallpaperTransitionStep, WallpaperApplyOutcome> executeStep)
        {
            if (plan == null || executeStep == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            var finalOutcome = WallpaperApplyOutcome.Success;
            foreach (var step in plan.Steps)
            {
                var stepOutcome = executeStep(step);
                if (stepOutcome == WallpaperApplyOutcome.Failed)
                {
                    return WallpaperApplyOutcome.Failed;
                }
                if (stepOutcome == WallpaperApplyOutcome.Partial)
                    finalOutcome = WallpaperApplyOutcome.Partial;
            }

            return finalOutcome;
        }

        public static bool Apply(WallpaperSettings snapshot)
        {
            if (snapshot == null || snapshot.Mode == WallpaperMode.Unknown)
            {
                return false;
            }

            IDesktopWallpaper dw = null;
            SlideshowSourceHandle slideshowSource = null;
            WallpaperMode sourceMode = WallpaperMode.Unknown;
            bool mutationStarted = false;
            uint priorColor = 0;
            DesktopWallpaperPosition priorPosition = DesktopWallpaperPosition.Fill;
            IShellItemArray priorSlideshowSource = null;
            DesktopSlideshowOptions priorSlideshowOptions = DesktopSlideshowOptions.None;
            uint priorSlideshowTick = 0;
            bool legacyWallpaperCaptured = false;
            bool priorLegacyWallpaperExists = false;
            string priorLegacyWallpaper = null;
            PictureRollbackSnapshot priorPictureRollback = null;

            try
            {
                dw = WindowsWallpaperPlatform.CreateDesktopWallpaper();
                if (!WindowsWallpaperPlatform.TryDetectCurrentModeForApply(dw, out sourceMode))
                {
                    _logger.Warn("Wallpaper apply aborted before mutation: Slideshow ownership status could not be established");
                    return false;
                }
                if (sourceMode == WallpaperMode.Picture)
                {
                    if (!TryCapturePictureSharedRollbackState(
                        () => dw.GetBackgroundColor(),
                        () => dw.GetPosition(),
                        out priorColor,
                        out priorPosition))
                    {
                        _logger.Warn("Wallpaper transition from Picture aborted before mutation: prior background color or position could not be captured exactly.");
                        return false;
                    }
                }
                else
                {
                    priorColor = WindowsWallpaperPlatform.SafeGetBackgroundColor(dw);
                    priorPosition = WindowsWallpaperPlatform.SafeGetPosition(dw);
                }

                if ((snapshot.Mode == WallpaperMode.Spotlight && sourceMode != WallpaperMode.Spotlight) ||
                    (sourceMode == WallpaperMode.Slideshow && snapshot.Mode == WallpaperMode.Solid))
                {
                    if (!WindowsWallpaperPlatform.TryGetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, out priorLegacyWallpaperExists, out priorLegacyWallpaper))
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} aborted before mutation: prior legacy wallpaper registration could not be captured for rollback.");
                        return false;
                    }
                    legacyWallpaperCaptured = true;
                }

                if (sourceMode == WallpaperMode.Slideshow && snapshot.Mode != WallpaperMode.Slideshow)
                {
                    try
                    {
                        priorSlideshowSource = dw.GetSlideshow();
                        if (priorSlideshowSource != null)
                            dw.GetSlideshowOptions(out priorSlideshowOptions, out priorSlideshowTick);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Could not capture prior Slideshow ownership for rollback");
                    }

                    if ((snapshot.Mode == WallpaperMode.Solid || snapshot.Mode == WallpaperMode.Spotlight) && priorSlideshowSource == null)
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} aborted before mutation: prior Slideshow ownership could not be captured for rollback.");
                        return false;
                    }
                }

                PicturePreflightResult picturePreflight = null;
                if (snapshot.Mode == WallpaperMode.Picture)
                {
                    var monitorMap = WallpaperMonitorMapper.BuildMonitorMap();
                    picturePreflight = PreflightPictureAssignments(snapshot, monitorMap, System.IO.File.Exists);
                    if (!picturePreflight.HasViableAssignments)
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> Picture aborted before mutation: no viable live picture assignments ({picturePreflight.DisconnectedCount} disconnected, {picturePreflight.MissingCount} missing).");
                        return false;
                    }
                }
                else if (snapshot.Mode == WallpaperMode.Slideshow)
                {
                    if (!TryPrepareSlideshowSource(snapshot.SlideshowConfig?.SourcePaths, out slideshowSource))
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> Slideshow aborted before mutation: no resolvable slideshow source.");
                        return false;
                    }
                }

                if (sourceMode == WallpaperMode.Picture && snapshot.Mode != WallpaperMode.Spotlight)
                {
                    if (!TryGetAttachedDesktopMonitorIdsStrict(dw, out List<string> attachedMonitorIds) ||
                        !PreparePictureSourceRecovery(
                            sourceMode,
                            attachedMonitorIds,
                            monitorId => dw.GetWallpaper(monitorId),
                            captured => priorPictureRollback = captured))
                    {
                        _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} aborted before mutation: exact per-monitor Picture rollback state could not be captured.");
                        return false;
                    }
                }

                var plan = WallpaperTransitionPlanner.BuildTransitionPlan(sourceMode, snapshot.Mode);
                _logger.Info($"Wallpaper transition: {sourceMode} -> {snapshot.Mode}");

                var outcome = ExecuteTransitionSteps(plan, step =>
                {
                    if (step != WallpaperTransitionStep.NotifySettings &&
                        step != WallpaperTransitionStep.VerifyDestinationMode &&
                        step != WallpaperTransitionStep.ActivateSpotlight)
                    {
                        mutationStarted = true;
                    }

                    switch (step)
                    {
                        case WallpaperTransitionStep.DeactivateSpotlight:
                            return DeactivateSpotlight();
                        case WallpaperTransitionStep.SetBackgroundColor:
                            return ApplyBackgroundColor(dw, snapshot.SolidColorArgb);
                        case WallpaperTransitionStep.SupersedeSlideshowOwnership:
                            return SupersedeSlideshowOwnership(dw);
                        case WallpaperTransitionStep.SetPosition:
                            return ApplyPosition(dw, snapshot.Position);
                        case WallpaperTransitionStep.DisableDesktopBackground:
                            dw.Enable(false);
                            return WallpaperApplyOutcome.Success;
                        case WallpaperTransitionStep.SetPictureWallpapers:
                            return ApplyPictureWallpapers(dw, picturePreflight);
                        case WallpaperTransitionStep.SetSlideshowSource:
                            return ApplySlideshowSource(dw, slideshowSource)
                                ? WallpaperApplyOutcome.Success
                                : WallpaperApplyOutcome.Failed;
                        case WallpaperTransitionStep.SetSlideshowOptions:
                            return ApplySlideshowOptions(dw, snapshot.SlideshowConfig);
                        case WallpaperTransitionStep.EstablishDestinationMode:
                            return EstablishDestinationMode(snapshot.Mode);
                        case WallpaperTransitionStep.NotifySettings:
                            WindowsWallpaperPlatform.NotifyWallpaperSettingsChanged();
                            return WallpaperApplyOutcome.Success;
                        case WallpaperTransitionStep.VerifyDestinationMode:
                            if (snapshot.Mode == WallpaperMode.Slideshow)
                            {
                                return VerifySlideshowOwnershipSettled(
                                    () => WindowsWallpaperPlatform.HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                                    () => WindowsWallpaperPlatform.GetSlideshowActiveForOwnership(dw),
                                    milliseconds => System.Threading.Thread.Sleep(milliseconds));
                            }

                            return VerifyDestinationOwnership(
                                snapshot.Mode,
                                () => WindowsWallpaperPlatform.DetectCurrentMode(dw),
                                () => WindowsWallpaperPlatform.GetSlideshowActiveForOwnership(dw));
                        case WallpaperTransitionStep.ActivateSpotlight:
                            return PrepareSpotlightDestinationApply(
                                sourceMode,
                                DisplayConfigService.GetDisplayConfigs(),
                                () => dw.GetMonitorDevicePathCount(),
                                index => dw.GetMonitorDevicePathAt(index),
                                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                                monitorId => dw.GetWallpaper(monitorId),
                                captured => priorPictureRollback = captured,
                                monitorIds =>
                                {
                                    mutationStarted = true;
                                    return ActivateSpotlightDestination(dw, monitorIds);
                                });
                        default:
                            return WallpaperApplyOutcome.Failed;
                    }
                });

                RecoverPriorOwnerForOutcome(
                    sourceMode,
                    outcome,
                    mutationStarted,
                    () => RestorePriorOwner(dw, sourceMode, priorColor, priorPosition, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick, legacyWallpaperCaptured, priorLegacyWallpaperExists, priorLegacyWallpaper, priorPictureRollback));

                if (outcome == WallpaperApplyOutcome.Failed)
                {
                    _logger.Error($"Wallpaper transition {sourceMode} -> {snapshot.Mode} failed before the destination was fully established.");
                    return false;
                }

                if (outcome == WallpaperApplyOutcome.Partial)
                {
                    _logger.Warn($"Wallpaper transition {sourceMode} -> {snapshot.Mode} partially applied; one or more represented destination properties could not be established.");
                    return false;
                }

                _logger.Info($"Wallpaper applied: {WallpaperModeFormatter.Display(snapshot.Mode)}");
                return true;
            }
            catch (Exception ex)
            {
                if (dw != null)
                {
                    RecoverPriorOwnerForOutcome(
                        sourceMode,
                        WallpaperApplyOutcome.Failed,
                        mutationStarted,
                        () => RestorePriorOwner(dw, sourceMode, priorColor, priorPosition, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick, legacyWallpaperCaptured, priorLegacyWallpaperExists, priorLegacyWallpaper, priorPictureRollback));
                }
                _logger.Error(ex, "Wallpaper apply failed");
                return false;
            }
            finally
            {
                slideshowSource?.Dispose();
                if (priorSlideshowSource != null)
                    Marshal.ReleaseComObject(priorSlideshowSource);
            }
        }

        internal static WallpaperApplyOutcome ApplyBackgroundColor(Action setBackgroundColor)
        {
            if (setBackgroundColor == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                setBackgroundColor();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetBackgroundColor failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome ApplyBackgroundColor(IDesktopWallpaper dw, uint color)
        {
            return ApplyBackgroundColor(() => dw.SetBackgroundColor(color));
        }

        internal static WallpaperApplyOutcome ApplyPosition(Action setPosition)
        {
            if (setPosition == null)
            {
                return WallpaperApplyOutcome.Partial;
            }

            try
            {
                setPosition();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetPosition failed");
                return WallpaperApplyOutcome.Partial;
            }
        }

        internal static WallpaperApplyOutcome ApplyPosition(IDesktopWallpaper dw, string position)
        {
            var outcome = ApplyPosition(() => dw.SetPosition(WindowsWallpaperPlatform.StringToPosition(position)));
            if (outcome == WallpaperApplyOutcome.Success)
                _logger.Debug($"Wallpaper position set to '{position}'");
            return outcome;
        }

        internal static WallpaperApplyOutcome ApplyPictureWallpapers(IDesktopWallpaper dw, PicturePreflightResult preflight) =>
            ApplyPictureWallpapers(preflight, (monitorId, path) => dw.SetWallpaper(monitorId, path));

        internal static WallpaperApplyOutcome ApplyPictureWallpapers(PicturePreflightResult preflight, Action<string, string> setWallpaper)
        {
            if (preflight == null || !preflight.HasViableAssignments || setWallpaper == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            int applied = 0;
            int setterFailures = 0;
            foreach (var assignment in preflight.Assignments)
            {
                try
                {
                    setWallpaper(assignment.MonitorId, assignment.Path);
                    applied++;
                }
                catch (Exception ex)
                {
                    setterFailures++;
                    _logger.Warn(ex, $"SetWallpaper failed for {assignment.DeviceName}");
                }
            }

            _logger.Info($"Picture wallpaper assignment: {applied} applied, {setterFailures} setter failures, {preflight.DisconnectedCount} disconnected, {preflight.MissingCount} missing");
            return ClassifyPictureApplyOutcome(preflight, applied, setterFailures);
        }
        internal static bool TryPrepareSlideshowSource(List<string> paths, out SlideshowSourceHandle handle)
        {
            handle = null;
            string folder = SelectSlideshowSource(paths, System.IO.Directory.Exists);
            if (folder == null)
            {
                return false;
            }

            IShellItem item = null;
            IShellItemArray array = null;
            try
            {
                var itemGuid = typeof(IShellItem).GUID;
                var arrayGuid = typeof(IShellItemArray).GUID;
                SHCreateItemFromParsingName(folder, IntPtr.Zero, ref itemGuid, out item);
                SHCreateShellItemArrayFromShellItem(item, ref arrayGuid, out array);
                handle = new SlideshowSourceHandle(folder, item, array);
                return true;
            }
            catch (Exception ex)
            {
                if (array != null) Marshal.ReleaseComObject(array);
                if (item != null) Marshal.ReleaseComObject(item);
                _logger.Warn(ex, $"Could not resolve slideshow source {folder}");
                return false;
            }
        }
        internal static bool ApplySlideshowSource(IDesktopWallpaper dw, SlideshowSourceHandle source)
        {
            if (source?.Array == null)
            {
                return false;
            }

            try
            {
                dw.SetSlideshow(source.Array);
                _logger.Debug($"Slideshow source set to {source.Folder}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"SetSlideshow failed for {source.Folder}");
                return false;
            }
        }

        internal static WallpaperApplyOutcome ApplySlideshowOptions(Action setOptions)
        {
            if (setOptions == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                setOptions();
                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "SetSlideshowOptions failed");
                return WallpaperApplyOutcome.Partial;
            }
        }

        internal static WallpaperApplyOutcome ApplySlideshowOptions(IDesktopWallpaper dw, SlideshowConfig config)
        {
            if (config == null)
            {
                _logger.Warn("Wallpaper apply: Slideshow mode has no config");
                return WallpaperApplyOutcome.Failed;
            }

            var options = config.Shuffle
                ? DesktopSlideshowOptions.ShuffleImages
                : DesktopSlideshowOptions.None;
            var outcome = ApplySlideshowOptions(() =>
                dw.SetSlideshowOptions(options, config.IntervalSeconds * 1000));
            if (outcome == WallpaperApplyOutcome.Success)
                _logger.Debug($"Slideshow options: {config.IntervalSeconds}s, shuffle {config.Shuffle}");
            return outcome;
        }

        internal static WallpaperApplyOutcome SupersedeSlideshowOwnership(
            Func<bool?> readSlideshowActive,
            Func<bool> establishSolidOwnership)
        {
            if (readSlideshowActive == null || establishSolidOwnership == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool? slideshowActive = readSlideshowActive();
                if (!slideshowActive.HasValue)
                {
                    _logger.Warn("Slideshow ownership status is unavailable while leaving Slideshow");
                    return WallpaperApplyOutcome.Failed;
                }

                if (!slideshowActive.Value)
                {
                    return WallpaperApplyOutcome.Success;
                }

                if (!establishSolidOwnership())
                {
                    _logger.Warn("Could not invoke the classic Solid ownership bridge while leaving Slideshow");
                    return WallpaperApplyOutcome.Failed;
                }

                bool? slideshowStillActive = readSlideshowActive();
                if (!slideshowStillActive.HasValue || slideshowStillActive.Value)
                {
                    _logger.Warn("Slideshow ownership was not authoritatively cleared by the classic Solid ownership bridge");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not supersede Slideshow ownership");
                return WallpaperApplyOutcome.Failed;
            }
        }
        internal static WallpaperApplyOutcome SupersedeSlideshowOwnership(IDesktopWallpaper dw) =>
            SupersedeSlideshowOwnership(
                () => WindowsWallpaperPlatform.GetSlideshowActiveForOwnership(dw),
                ApplyClassicSolidOwnershipBridge);

        internal static bool ApplyClassicSolidOwnershipBridge(
            Func<bool> writeEmptyLegacyWallpaper,
            Func<bool> applyEmptyLegacyWallpaper)
        {
            if (writeEmptyLegacyWallpaper == null || applyEmptyLegacyWallpaper == null)
            {
                return false;
            }

            try
            {
                return writeEmptyLegacyWallpaper() && applyEmptyLegacyWallpaper();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Classic Solid ownership bridge failed");
                return false;
            }
        }

        internal static bool ApplyClassicSolidOwnershipBridge()
        {
            return ApplyClassicSolidOwnershipBridge(
                () => WindowsWallpaperPlatform.SetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, string.Empty),
                () => SystemParametersInfo(
                    SpiSetDeskWallpaper,
                    0,
                    string.Empty,
                    SpifUpdateIniFile | SpifSendChange));
        }

        internal static bool RecoverPriorOwnerAfterFailure(Func<bool> restorePriorOwner)
        {
            if (restorePriorOwner == null)
            {
                return false;
            }

            try
            {
                return restorePriorOwner();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Prior wallpaper owner recovery threw");
                return false;
            }
        }

        internal static bool RecoverPriorOwnerForOutcome(
            WallpaperMode sourceMode,
            WallpaperApplyOutcome outcome,
            bool mutationStarted,
            Func<bool> restorePriorOwner)
        {
            bool requiresRecovery =
                mutationStarted &&
                (outcome == WallpaperApplyOutcome.Failed ||
                 (sourceMode == WallpaperMode.Picture && outcome == WallpaperApplyOutcome.Partial));

            return !requiresRecovery || RecoverPriorOwnerAfterFailure(restorePriorOwner);
        }

        internal static bool RestorePriorOwner(
            WallpaperMode sourceMode,
            bool legacyWallpaperCaptured,
            bool priorLegacyWallpaperExists,
            string priorLegacyWallpaper,
            Func<bool> restoreSharedState,
            Func<bool> restoreSpotlightOwner,
            Func<bool> deactivateSpotlight,
            Func<string, bool> setLegacyWallpaper,
            Func<bool> deleteLegacyWallpaper,
            Func<bool> restoreSourceOwnership,
            Func<bool> establishSourceMode,
            Action notifySettings,
            Action<string> logComplete,
            Action<string> logIncomplete,
            Func<bool> restorePictureAssignments = null)
        {
            bool complete = true;

            try
            {
                complete &= restoreSharedState != null && restoreSharedState();

                if (sourceMode == WallpaperMode.Spotlight)
                {
                    complete &= restoreSpotlightOwner != null && restoreSpotlightOwner();
                }
                else
                {
                    complete &= deactivateSpotlight != null && deactivateSpotlight();

                    if (legacyWallpaperCaptured)
                    {
                        complete &= RestoreLegacyWallpaperRegistration(
                            priorLegacyWallpaperExists,
                            priorLegacyWallpaper,
                            setLegacyWallpaper,
                            deleteLegacyWallpaper);
                    }

                    complete &= restoreSourceOwnership != null && restoreSourceOwnership();

                    if (sourceMode == WallpaperMode.Picture)
                        complete &= restorePictureAssignments != null && restorePictureAssignments();

                    if (sourceMode == WallpaperMode.Solid || sourceMode == WallpaperMode.Picture || sourceMode == WallpaperMode.Slideshow)
                        complete &= establishSourceMode != null && establishSourceMode();

                    notifySettings?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Prior wallpaper owner restore threw for {sourceMode}");
                complete = false;
            }

            string message = $"prior wallpaper owner after failed destination establishment: {sourceMode}";
            if (complete)
                logComplete?.Invoke($"Restored {message}");
            else
                logIncomplete?.Invoke($"Incomplete restore of {message}");

            return complete;
        }
        internal static bool RestorePriorOwner(
            IDesktopWallpaper dw,
            WallpaperMode sourceMode,
            uint priorColor,
            DesktopWallpaperPosition priorPosition,
            IShellItemArray priorSlideshowSource,
            DesktopSlideshowOptions priorSlideshowOptions,
            uint priorSlideshowTick,
            bool legacyWallpaperCaptured,
            bool priorLegacyWallpaperExists,
            string priorLegacyWallpaper,
            PictureRollbackSnapshot priorPictureRollback)
        {
            return RestorePriorOwner(
                sourceMode,
                legacyWallpaperCaptured,
                priorLegacyWallpaperExists,
                priorLegacyWallpaper,
                () => RestoreSharedWallpaperState(dw, priorColor, priorPosition),
                () => ActivateSpotlightDestination(dw, normalizePresentation: false) == WallpaperApplyOutcome.Success,
                () => DeactivateSpotlight() == WallpaperApplyOutcome.Success,
                value => WindowsWallpaperPlatform.SetHkcuString(ControlPanelDesktopSubkey, WallpaperValue, value),
                () => WindowsWallpaperPlatform.DeleteHkcuValue(ControlPanelDesktopSubkey, WallpaperValue),
                () => RestoreSourceWallpaperOwnership(dw, sourceMode, priorSlideshowSource, priorSlideshowOptions, priorSlideshowTick),
                () => EstablishDestinationMode(sourceMode) == WallpaperApplyOutcome.Success,
                NotifyWallpaperSettingsChanged,
                message => _logger.Warn(message),
                message => _logger.Error(message),
                priorPictureRollback == null
                    ? null
                    : () => RestorePictureRollbackSnapshot(
                        priorPictureRollback,
                        (monitorId, path) => dw.SetWallpaper(monitorId, path)));
        }
        internal static bool RestoreSharedWallpaperState(IDesktopWallpaper dw, uint priorColor, DesktopWallpaperPosition priorPosition)
        {
            try
            {
                dw.SetBackgroundColor(priorColor);
                dw.SetPosition(priorPosition);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not restore prior wallpaper color or position");
                return false;
            }
        }

        internal static bool RestoreSourceWallpaperOwnership(
            IDesktopWallpaper dw,
            WallpaperMode sourceMode,
            IShellItemArray priorSlideshowSource,
            DesktopSlideshowOptions priorSlideshowOptions,
            uint priorSlideshowTick)
        {
            try
            {
                if (sourceMode == WallpaperMode.Slideshow)
                {
                    if (priorSlideshowSource == null)
                    {
                        return false;
                    }

                    dw.Enable(true);
                    dw.SetSlideshow(priorSlideshowSource);
                    dw.SetSlideshowOptions(priorSlideshowOptions, priorSlideshowTick);
                }
                else if (sourceMode == WallpaperMode.Solid)
                {
                    dw.Enable(false);
                }
                else
                {
                    dw.Enable(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Could not restore prior wallpaper rendering owner {sourceMode}");
                return false;
            }
        }

        internal static bool RestoreLegacyWallpaperRegistration(
            bool priorValueExisted,
            string priorValue,
            Func<string, bool> setValue,
            Func<bool> deleteValue)
        {
            if (priorValueExisted)
            {
                return setValue != null && setValue(priorValue ?? string.Empty);
            }

            return deleteValue != null && deleteValue();
        }

        internal static WallpaperApplyOutcome DeactivateSpotlight(
            bool modeIsSpotlight,
            bool providerEnabled,
            Func<bool> resetMode,
            Func<bool> disableProvider)
        {
            try
            {
                bool modeReset = true;
                bool providerDisabled = true;

                if (modeIsSpotlight)
                {
                    if (resetMode == null)
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                    modeReset = resetMode();
                }

                if (providerEnabled)
                {
                    if (disableProvider == null)
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                    providerDisabled = disableProvider();
                }

                if (!modeReset || !providerDisabled)
                {
                    _logger.Warn("Desktop Spotlight mode or provider state could not be deactivated completely");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight deactivation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome DeactivateSpotlight()
        {
            bool modeIsSpotlight = WindowsWallpaperPlatform.HkcuDword(WallpapersSubkey, BackgroundTypeValue) == BackgroundTypeSpotlight;
            bool providerEnabled = WindowsWallpaperPlatform.HkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue) == 1;
            return DeactivateSpotlight(
                modeIsSpotlight,
                providerEnabled,
                () => WindowsWallpaperPlatform.SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, BackgroundTypePicture, create: true),
                () => WindowsWallpaperPlatform.SetHkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue, 0, create: false));
        }

        internal static WallpaperApplyOutcome ActivateSpotlight(Func<bool> enableProvider, Func<bool> selectMode)
        {
            if (enableProvider == null || selectMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool providerEnabled = enableProvider();
                bool modeSelected = selectMode();
                if (!providerEnabled || !modeSelected)
                {
                    _logger.Warn("Desktop Spotlight provider or mode state could not be established completely");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight activation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static int? BackgroundTypeForMode(WallpaperMode mode)
        {
            return mode switch
            {
                WallpaperMode.Picture => BackgroundTypePicture,
                WallpaperMode.Solid => BackgroundTypeSolid,
                WallpaperMode.Slideshow => BackgroundTypeSlideshow,
                WallpaperMode.Spotlight => BackgroundTypeSpotlight,
                _ => null,
            };
        }

        internal static WallpaperApplyOutcome EstablishDestinationMode(
            WallpaperMode mode,
            Func<int, bool> writeMode,
            Func<int?> readMode)
        {
            int? backgroundType = BackgroundTypeForMode(mode);
            if (!backgroundType.HasValue || writeMode == null || readMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                if (!writeMode(backgroundType.Value) || readMode() != backgroundType.Value)
                {
                    _logger.Warn($"Wallpaper destination mode {mode} could not be established or verified");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper destination mode {mode} could not be established");
                return WallpaperApplyOutcome.Failed;
            }
        }
        internal static WallpaperApplyOutcome EstablishDestinationMode(WallpaperMode mode)
        {
            return EstablishDestinationMode(
                mode,
                value => WindowsWallpaperPlatform.SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, value, create: true),
                () => WindowsWallpaperPlatform.HkcuDword(WallpapersSubkey, BackgroundTypeValue));
        }

        internal static WallpaperApplyOutcome VerifyDestinationMode(WallpaperMode expectedMode, Func<WallpaperMode> detectMode)
        {
            if (detectMode == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                var actual = detectMode();
                if (actual != expectedMode)
                {
                    _logger.Warn($"Wallpaper destination mode verification failed: requested {expectedMode}, detected {actual}");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper destination mode verification failed for {expectedMode}");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome VerifyDestinationOwnership(
            WallpaperMode expectedMode,
            Func<WallpaperMode> detectMode,
            Func<bool?> slideshowActive)
        {
            var modeOutcome = VerifyDestinationMode(expectedMode, detectMode);
            if (modeOutcome != WallpaperApplyOutcome.Success || expectedMode == WallpaperMode.Spotlight)
            {
                return modeOutcome;
            }
            if (slideshowActive == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                bool? active = slideshowActive();
                if (!active.HasValue)
                {
                    _logger.Warn($"Wallpaper ownership verification failed for {expectedMode}: slideshow status is unavailable");
                    return WallpaperApplyOutcome.Failed;
                }

                bool expectedSlideshow = expectedMode == WallpaperMode.Slideshow;
                if (active.Value != expectedSlideshow)
                {
                    _logger.Warn($"Wallpaper ownership verification failed: requested {expectedMode}, slideshow active={active.Value}");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Wallpaper ownership verification failed for {expectedMode}");
                return WallpaperApplyOutcome.Failed;
            }
        }

        internal static WallpaperApplyOutcome VerifySlideshowOwnershipSettled(
            Func<int?> readBackgroundType,
            Func<bool?> readSlideshowActive,
            Action<int> delay,
            int maxAttempts = SlideshowSettlementMaxAttempts,
            int delayMs = SlideshowSettlementDelayMs)
        {
            if (readBackgroundType == null || readSlideshowActive == null || delay == null || maxAttempts <= 0 || delayMs < 0)
            {
                return WallpaperApplyOutcome.Failed;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                int? backgroundType;
                try
                {
                    backgroundType = readBackgroundType();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Slideshow settlement could not read destination mode");
                    return WallpaperApplyOutcome.Failed;
                }

                if (backgroundType != BackgroundTypeSlideshow)
                {
                    _logger.Warn($"Slideshow settlement contradicted by destination mode {backgroundType?.ToString() ?? "unavailable"}");
                    return WallpaperApplyOutcome.Failed;
                }

                bool? slideshowActive = null;
                try
                {
                    slideshowActive = readSlideshowActive();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Slideshow ownership status is unavailable during settlement attempt {attempt}/{maxAttempts}");
                }

                if (slideshowActive == true)
                {
                    return WallpaperApplyOutcome.Success;
                }

                if (attempt < maxAttempts)
                {
                    try
                    {
                        delay(delayMs);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Slideshow settlement delay failed");
                        return WallpaperApplyOutcome.Failed;
                    }
                }
            }

            _logger.Warn($"Slideshow ownership did not settle within {(maxAttempts - 1) * delayMs} ms");
            return WallpaperApplyOutcome.Failed;
        }

        internal static bool IsSpotlightDestinationEstablished(
            int? backgroundType,
            int? providerEnabled,
            bool? slideshowActive,
            IEnumerable<string> renderedWallpaperPaths,
            Func<string, bool> fileExists = null) =>
            IsSpotlightDestinationEstablished(
                backgroundType,
                providerEnabled,
                slideshowActive,
                positionIsFill: true,
                renderedWallpaperPaths,
                fileExists);

        internal static bool IsSpotlightDestinationEstablished(
            int? backgroundType,
            int? providerEnabled,
            bool? slideshowActive,
            bool positionIsFill,
            IEnumerable<string> renderedWallpaperPaths,
            Func<string, bool> fileExists = null)
        {
            if (backgroundType != BackgroundTypeSpotlight || providerEnabled != 1 ||
                !slideshowActive.HasValue || slideshowActive.Value || !positionIsFill)
            {
                return false;
            }

            var paths = renderedWallpaperPaths?.ToList() ?? [];
            return paths.Count > 0 && paths.All(path =>
                !string.IsNullOrWhiteSpace(path) &&
                SpotlightProvider.IsSpotlightProviderPath(path) &&
                (fileExists == null || fileExists(path)));
        }

        internal static List<string> GetRenderedWallpaperPaths(IDesktopWallpaper dw, IEnumerable<string> monitorIds)
        {
            var paths = new List<string>();
            foreach (string monitorId in monitorIds ?? [])
            {
                try
                {
                    paths.Add(dw.GetWallpaper(monitorId));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Could not read rendered wallpaper for {monitorId}");
                    paths.Add(string.Empty);
                }
            }

            return paths;
        }

        internal static bool TryEnableDesktopBackground(IDesktopWallpaper dw)
        {
            try
            {
                dw.Enable(true);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not hand desktop rendering ownership back before Spotlight activation");
                return false;
            }
        }

        internal static bool RefreshSpotlightSelection(Func<bool> refreshDesktop, Action notifySettings)
        {
            if (refreshDesktop == null || notifySettings == null)
            {
                return false;
            }

            try
            {
                bool refreshed = refreshDesktop();
                notifySettings();
                if (!refreshed)
                    _logger.Warn("Desktop Spotlight shell refresh failed");
                return refreshed;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight shell refresh failed");
                return false;
            }
        }

        internal static bool PaintSpotlightProviderImage(
            string path,
            Func<string, bool> isUsableImage,
            Action<string> paint)
        {
            if (string.IsNullOrWhiteSpace(path) || !SpotlightProvider.IsVerifiedSpotlightProviderPath(path) || isUsableImage == null || paint == null)
            {
                return false;
            }

            try
            {
                if (!isUsableImage(path))
                {
                    return false;
                }

                paint(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight provider-owned repaint failed");
                return false;
            }
        }

        internal static bool PaintSpotlightProviderImageForMonitors(
            string path,
            IEnumerable<string> monitorIds,
            Func<string, bool> isUsableImage,
            Action<string, string> paint)
        {
            if (string.IsNullOrWhiteSpace(path) || !SpotlightProvider.IsVerifiedSpotlightProviderPath(path) ||
                monitorIds == null || isUsableImage == null || paint == null)
            {
                return false;
            }

            List<string> ids = [.. monitorIds];
            if (ids.Count == 0 || ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            {
                return false;
            }

            try
            {
                if (!isUsableImage(path))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight provider-owned repaint validation failed");
                return false;
            }

            bool complete = true;
            foreach (string monitorId in ids)
            {
                try
                {
                    paint(monitorId, path);
                }
                catch (Exception ex)
                {
                    complete = false;
                    _logger.Warn(ex, $"Desktop Spotlight provider-owned repaint failed for {monitorId}");
                }
            }

            return complete;
        }

        internal static bool NormalizeSpotlightPositionToFill(Action setFillPosition)
        {
            if (setFillPosition == null)
            {
                return false;
            }

            try
            {
                setFillPosition();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight position could not be normalized to Fill");
                return false;
            }
        }

        internal static WallpaperApplyOutcome ActivateSpotlightDestination(
            Func<bool> enableDesktopBackground,
            Func<bool> relinquishPreviousOwner,
            Func<bool> enableProvider,
            Func<bool> selectMode,
            Func<bool> refreshSelection,
            Func<bool> verifyDestination,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage) =>
            ActivateSpotlightDestination(
                enableDesktopBackground,
                relinquishPreviousOwner,
                enableProvider,
                selectMode,
                refreshSelection,
                normalizePresentation: null,
                verifyDestination,
                selectRepaintPath,
                paintProviderImage);

        internal static WallpaperApplyOutcome ActivateSpotlightDestination(
            Func<bool> enableDesktopBackground,
            Func<bool> relinquishPreviousOwner,
            Func<bool> enableProvider,
            Func<bool> selectMode,
            Func<bool> refreshSelection,
            Func<bool> normalizePresentation,
            Func<bool> verifyDestination,
            Func<string> selectRepaintPath,
            Func<string, bool> paintProviderImage)
        {
            if (enableDesktopBackground == null || relinquishPreviousOwner == null || enableProvider == null || selectMode == null ||
                refreshSelection == null || verifyDestination == null || selectRepaintPath == null || paintProviderImage == null)
            {
                return WallpaperApplyOutcome.Failed;
            }

            try
            {
                if (!enableDesktopBackground() || !relinquishPreviousOwner())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                var providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                if (normalizePresentation != null)
                {
                    if (!normalizePresentation())
                    {
                        return WallpaperApplyOutcome.Failed;
                    }

                    providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                    if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                    {
                        return WallpaperApplyOutcome.Failed;
                    }
                }

                if (verifyDestination())
                {
                    return WallpaperApplyOutcome.Success;
                }

                string repaintPath = selectRepaintPath();
                if (string.IsNullOrWhiteSpace(repaintPath) || !SpotlightProvider.IsVerifiedSpotlightProviderPath(repaintPath))
                {
                    _logger.Warn("Desktop Spotlight mode/provider was selected, but no verified provider-owned repaint image is available");
                    return WallpaperApplyOutcome.Failed;
                }

                if (!paintProviderImage(repaintPath))
                {
                    return WallpaperApplyOutcome.Failed;
                }

                providerOutcome = ActivateSpotlight(enableProvider, selectMode);
                if (providerOutcome != WallpaperApplyOutcome.Success || !refreshSelection())
                {
                    return WallpaperApplyOutcome.Failed;
                }

                if (!verifyDestination())
                {
                    _logger.Warn("Desktop Spotlight presentation/repaint completed, but final provider/mode/rendered ownership verification failed");
                    return WallpaperApplyOutcome.Failed;
                }

                return WallpaperApplyOutcome.Success;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Desktop Spotlight destination activation failed");
                return WallpaperApplyOutcome.Failed;
            }
        }
        internal static WallpaperApplyOutcome ActivateSpotlightDestination(
            IDesktopWallpaper dw,
            IReadOnlyList<string> attachedMonitorIds = null,
            bool normalizePresentation = true)
        {
            if (WindowsWallpaperPlatform.HkcuDword(BackgroundAppsSubkey, BackgroundAppsDisabled) == 1)
            {
                _logger.Warn("Desktop Spotlight destination cannot be established while background apps are disabled globally");
                return WallpaperApplyOutcome.Failed;
            }

            List<string> monitorIds;
            if (attachedMonitorIds != null)
            {
                monitorIds = [.. attachedMonitorIds];
            }
            else if (!WallpaperMonitorMapper.TryResolveCurrentActiveSpotlightMonitorIds(
                DisplayConfigService.GetDisplayConfigs(),
                () => dw.GetMonitorDevicePathCount(),
                index => dw.GetMonitorDevicePathAt(index),
                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                out monitorIds))
            {
                _logger.Warn("Desktop Spotlight destination cannot be established because the current-active wallpaper monitor domain is incomplete");
                return WallpaperApplyOutcome.Failed;
            }

            if (monitorIds.Count == 0)
            {
                _logger.Warn("Desktop Spotlight destination cannot be verified because no attached wallpaper monitors are available");
                return WallpaperApplyOutcome.Failed;
            }

            bool VerifyDestination() => IsSpotlightDestinationEstablished(
                WindowsWallpaperPlatform.HkcuDword(WallpapersSubkey, BackgroundTypeValue),
                WindowsWallpaperPlatform.HkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue),
                WindowsWallpaperPlatform.GetSlideshowActiveForOwnership(dw),
                !normalizePresentation || dw.GetPosition() == DesktopWallpaperPosition.Fill,
                GetRenderedWallpaperPaths(dw, monitorIds),
                IsUsableSpotlightProviderImage);

            string SelectRepaintPath() => SpotlightProvider.SelectSpotlightRepaintPath(
                GetRenderedWallpaperPaths(dw, monitorIds),
                EnumerateSpotlightProviderImages(SpotlightIrisRoot, newestFirst: true),
                EnumerateSpotlightProviderImages(SpotlightBuiltInRoot, newestFirst: false),
                IsUsableSpotlightProviderImage,
                IsLandscapeSpotlightProviderImage);

            Func<bool> normalizeSpotlightPresentation = normalizePresentation
                ? () => NormalizeSpotlightPositionToFill(() => dw.SetPosition(DesktopWallpaperPosition.Fill))
                : null;

            return ActivateSpotlightDestination(
                () => TryEnableDesktopBackground(dw),
                () => RelinquishWallpaperForSpotlight(dw),
                () => WindowsWallpaperPlatform.SetHkcuDword(DesktopSpotlightSubkey, DesktopSpotlightValue, 1, create: true),
                () => WindowsWallpaperPlatform.SetHkcuDword(WallpapersSubkey, BackgroundTypeValue, BackgroundTypeSpotlight, create: true),
                () => RefreshSpotlightSelection(
                    () => SystemParametersInfo(
                        SpiSetDeskWallpaper,
                        0,
                        null,
                        SpifUpdateIniFile | SpifSendChange),
                    NotifyWallpaperSettingsChanged),
                normalizeSpotlightPresentation,
                VerifyDestination,
                SelectRepaintPath,
                path => PaintSpotlightProviderImageForMonitors(
                    path,
                    monitorIds,
                    IsUsableSpotlightProviderImage,
                    (monitorId, providerPath) => dw.SetWallpaper(monitorId, providerPath)));
        }

        internal static bool RelinquishWallpaperForSpotlight(
            Func<bool?> readSlideshowActive,
            Func<WallpaperApplyOutcome> supersedeSlideshow,
            Func<bool> deleteLegacyWallpaper)
        {
            if (readSlideshowActive == null || supersedeSlideshow == null || deleteLegacyWallpaper == null)
            {
                return false;
            }

            try
            {
                bool? slideshowActive = readSlideshowActive();
                if (!slideshowActive.HasValue)
                {
                    _logger.Warn("Could not establish Slideshow ownership state before Spotlight acquisition");
                    return false;
                }

                if (slideshowActive.Value && supersedeSlideshow() != WallpaperApplyOutcome.Success)
                {
                    return false;
                }

                return deleteLegacyWallpaper();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not relinquish prior wallpaper owner before Spotlight acquisition");
                return false;
            }
        }
        internal static bool RelinquishWallpaperForSpotlight(IDesktopWallpaper dw)
        {
            // Current Settings clears legacy wallpaper ownership before Spotlight provider selection; keep that OS-coupled handoff isolated here
            return RelinquishWallpaperForSpotlight(
                () => WindowsWallpaperPlatform.GetSlideshowActiveForOwnership(dw),
                () => SupersedeSlideshowOwnership(dw),
                () => WindowsWallpaperPlatform.DeleteHkcuValue(ControlPanelDesktopSubkey, WallpaperValue));
        }
    }
}
