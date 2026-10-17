using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Wallpapers.Interop;
using static DisplayProfileManager.Wallpapers.Interop.DesktopWallpaperNative;
using static DisplayProfileManager.Wallpapers.Interop.WindowsWallpaperPlatform;

namespace DisplayProfileManager.Wallpapers.Policy
{
    internal static class WallpaperMonitorMapper
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        internal static string NormalizeMonitorDevicePath(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().TrimEnd('\0');

        internal static Dictionary<string, string> BuildMonitorMapFromCcd(IEnumerable<DisplayConfigInfo> activeDisplays, IEnumerable<string> attachedDesktopMonitorIds)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (activeDisplays == null || attachedDesktopMonitorIds == null)
            {
                return map;
            }

            var desktopByPath = attachedDesktopMonitorIds
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new { Original = path, Normalized = NormalizeMonitorDevicePath(path) })
                .Where(item => item.Normalized.Length > 0)
                .GroupBy(item => item.Normalized, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Original, StringComparer.OrdinalIgnoreCase);

            var usedMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in activeDisplays
                .Where(display => display != null && display.IsEnabled && !string.IsNullOrWhiteSpace(display.DeviceName))
                .GroupBy(display => display.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                var targetPaths = group
                    .Select(display => NormalizeMonitorDevicePath(display.MonitorDevicePath))
                    .Where(path => path.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (targetPaths.Count != 1) continue;

                if (!desktopByPath.TryGetValue(targetPaths[0], out string monitorId)) continue;

                if (!usedMonitorIds.Add(monitorId)) continue;

                map[group.Key] = monitorId;
            }

            return map;
        }

        internal static bool TryGetAttachedDesktopMonitorIdsStrict(
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            out List<string> attached)
        {
            attached = [];
            if (getMonitorCount == null || getMonitorDevicePathAt == null || getMonitorRect == null)
            {
                return false;
            }

            uint count;
            try
            {
                count = getMonitorCount();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not enumerate the Spotlight wallpaper monitor domain");
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                string monitorId;
                try
                {
                    monitorId = getMonitorDevicePathAt(i);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not resolve Spotlight wallpaper monitor index {i}");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(monitorId) || !seen.Add(monitorId))
                {
                    _logger.Warn($"Spotlight wallpaper monitor index {i} did not resolve to a unique monitor ID");
                    return false;
                }

                DesktopMonitorRectResult rect;
                try
                {
                    rect = getMonitorRect(monitorId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not classify Spotlight wallpaper monitor {monitorId}");
                    return false;
                }

                if (rect.HResult == HResultSFalse)
                {
                    _logger.Debug($"IDesktopWallpaper monitor is definitively detached: {monitorId}");
                    continue;
                }

                if (rect.HResult != HResultSOk)
                {
                    _logger.Warn($"Spotlight wallpaper monitor {monitorId} attachment state is unknown (HRESULT 0x{rect.HResult:X8})");
                    return false;
                }

                if (!rect.HasPositiveArea)
                {
                    _logger.Warn($"Spotlight wallpaper monitor {monitorId} returned S_OK without a valid display rectangle");
                    return false;
                }

                attached.Add(monitorId);
            }

            return true;
        }

        internal static bool TryGetAttachedDesktopMonitorIdsStrict(IDesktopWallpaper dw, out List<string> attached)
        {
            return TryGetAttachedDesktopMonitorIdsStrict(
                () => dw.GetMonitorDevicePathCount(),
                index => dw.GetMonitorDevicePathAt(index),
                monitorId => ReadDesktopMonitorRect(dw, monitorId),
                out attached);
        }

        internal static bool TryResolveCurrentActiveSpotlightMonitorIds(
            IEnumerable<DisplayConfigInfo> currentDisplays,
            Func<uint> getMonitorCount,
            Func<uint, string> getMonitorDevicePathAt,
            Func<string, DesktopMonitorRectResult> getMonitorRect,
            out List<string> monitorIds)
        {
            monitorIds = [];
            if (currentDisplays == null || getMonitorCount == null || getMonitorDevicePathAt == null || getMonitorRect == null)
            {
                return false;
            }

            var expectedOrder = new List<string>();
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var display in currentDisplays.Where(display => display != null && display.IsEnabled))
            {
                string expectedPath = NormalizeMonitorDevicePath(display.MonitorDevicePath);
                if (expectedPath.Length == 0)
                {
                    _logger.Warn($"Spotlight current-active domain is incomplete for {display.DeviceName}: CCD monitor path is unavailable");
                    return false;
                }

                if (!expected.Add(expectedPath))
                {
                    _logger.Warn($"Spotlight current-active domain is ambiguous: CCD monitor path '{expectedPath}' is not unique");
                    return false;
                }

                expectedOrder.Add(expectedPath);
            }

            if (expectedOrder.Count == 0)
            {
                _logger.Warn("Spotlight current-active domain is empty: no enabled CCD targets are available");
                return false;
            }

            uint count;
            try
            {
                count = getMonitorCount();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not enumerate wallpaper monitor identities for the current-active Spotlight domain");
                return false;
            }

            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                string rawMonitorId;
                try
                {
                    rawMonitorId = getMonitorDevicePathAt(i);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Ignoring unaddressable historical wallpaper monitor index {i}");
                    continue;
                }

                string monitorId = NormalizeMonitorDevicePath(rawMonitorId);
                if (monitorId.Length == 0)
                {
                    _logger.Debug($"Ignoring blank historical wallpaper monitor index {i}");
                    continue;
                }

                if (expected.Contains(monitorId))
                {
                    if (resolved.ContainsKey(monitorId)) continue;

                    DesktopMonitorRectResult liveRect;
                    try
                    {
                        liveRect = getMonitorRect(monitorId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"Could not classify expected live Spotlight monitor {monitorId}");
                        return false;
                    }

                    if (liveRect.HResult != HResultSOk || !liveRect.HasPositiveArea)
                    {
                        string classification = liveRect.HResult == HResultSFalse
                            ? "detached"
                            : $"unavailable (HRESULT 0x{liveRect.HResult:X8})";
                        _logger.Warn($"Expected live Spotlight monitor {monitorId} is {classification} or has an invalid rectangle");
                        return false;
                    }

                    resolved[monitorId] = monitorId;
                    continue;
                }

                DesktopMonitorRectResult extraRect;
                try
                {
                    extraRect = getMonitorRect(monitorId);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"Ignoring unclassifiable historical wallpaper monitor {monitorId}");
                    continue;
                }

                if (extraRect.HResult == HResultSOk && extraRect.HasPositiveArea)
                {
                    _logger.Warn($"Spotlight monitor authority mismatch: attached wallpaper monitor {monitorId} is absent from the enabled CCD target set");
                    return false;
                }

                if (extraRect.HResult == HResultSFalse)
                    _logger.Debug($"Ignoring detached historical wallpaper monitor {monitorId}");
                else
                    _logger.Debug($"Ignoring historical wallpaper monitor {monitorId} with non-authoritative attachment state 0x{extraRect.HResult:X8}");
            }

            if (resolved.Count != expected.Count)
            {
                var missing = expectedOrder.Where(path => !resolved.ContainsKey(path));
                _logger.Warn($"Spotlight current-active domain is incomplete: unresolved CCD monitor paths {string.Join(", ", missing)}");
                return false;
            }

            foreach (string expectedPath in expectedOrder)
                monitorIds.Add(resolved[expectedPath]);

            return monitorIds.Count == expectedOrder.Count && monitorIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == monitorIds.Count;
        }

        internal static List<string> GetAttachedDesktopMonitorIds(IDesktopWallpaper dw)
        {
            var attached = new List<string>();
            uint count = dw.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    string monitorId = dw.GetMonitorDevicePathAt(i);
                    var rect = ReadDesktopMonitorRect(dw, monitorId);
                    if (rect.HResult == HResultSOk && rect.HasPositiveArea)
                        attached.Add(monitorId);
                    else if (rect.HResult == HResultSFalse)
                        _logger.Debug($"IDesktopWallpaper monitor is detached: {monitorId}");
                    else
                        _logger.Debug($"IDesktopWallpaper monitor {i} attachment state is unavailable (HRESULT 0x{rect.HResult:X8})");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, $"IDesktopWallpaper monitor {i} is not currently attached");
                }
            }

            return attached;
        }

        internal static Dictionary<string, string> BuildMonitorMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dw = CreateDesktopWallpaper();
                var desktopMonitorIds = GetAttachedDesktopMonitorIds(dw);
                var displayConfigs = DisplayConfigService.GetDisplayConfigs();
                map = BuildMonitorMapFromCcd(displayConfigs, desktopMonitorIds);

                foreach (var display in displayConfigs.Where(display => display.IsEnabled))
                {
                    if (string.IsNullOrWhiteSpace(display.DeviceName)) continue;

                    if (map.TryGetValue(display.DeviceName, out string monitorId))
                    {
                        _logger.Debug($"Wallpaper CCD identity: {display.DeviceName} target {display.TargetId} -> {display.MonitorDevicePath} -> {monitorId}");
                    }
                    else
                    {
                        _logger.Warn($"Wallpaper CCD identity unresolved for {display.DeviceName} target {display.TargetId} path '{display.MonitorDevicePath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "BuildMonitorMap failed");
            }

            if (map.Count == 0)
                _logger.Warn("Wallpaper monitor map is empty -> every picture assignment will be unavailable");

            return map;
        }
    }
}
