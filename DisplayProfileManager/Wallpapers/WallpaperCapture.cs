using System;
using System.Collections.Generic;
using System.Linq;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;
using static DisplayProfileManager.Wallpapers.Interop.DesktopWallpaperNative;
using static DisplayProfileManager.Wallpapers.Interop.WindowsWallpaperPlatform;
using static DisplayProfileManager.Wallpapers.Policy.WallpaperTransitionPlanner;
using static DisplayProfileManager.Wallpapers.SpotlightProvider;

namespace DisplayProfileManager.Wallpapers
{
    internal static class WallpaperCapture
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly uint[] _standardIntervalSeconds = [60, 600, 1800, 3600, 21600, 86400];

        public static WallpaperSettings Capture()
        {
            var snapshot = new WallpaperSettings();

            try
            {
                var dw = CreateDesktopWallpaper();
                uint backgroundColor = SafeGetBackgroundColor(dw);
                snapshot.Position = PositionToString(SafeGetPosition(dw));

                int? backgroundType = HkcuDword(WallpapersSubkey, BackgroundTypeValue);
                bool slideshowActive = SafeGetStatus(dw).HasFlag(DesktopSlideshowState.Slideshow);
                string liveWallpaper = HkcuString(ControlPanelDesktopSubkey, WallpaperValue);
                var monitorMap = WallpaperMonitorMapper.BuildMonitorMap();
                var capturedPictures = new Dictionary<string, MonitorWallpaper>(StringComparer.OrdinalIgnoreCase);
                bool providerPathSeen = IsSpotlightProviderPath(liveWallpaper);

                foreach (var kvp in monitorMap)
                {
                    try
                    {
                        string path = dw.GetWallpaper(kvp.Value);
                        if (string.IsNullOrEmpty(path)) continue;

                        providerPathSeen |= IsSpotlightProviderPath(path);
                        capturedPictures[kvp.Key] = new MonitorWallpaper
                        {
                            Path = path,
                            MonitorId = kvp.Value
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, $"GetWallpaper failed for {kvp.Key}");
                    }
                }

                snapshot.Mode = ClassifyWallpaperMode(
                    backgroundType,
                    slideshowActive,
                    capturedPictures.Count > 0,
                    providerPathSeen);
                if (ModeOwnsBackgroundColor(snapshot.Mode)) snapshot.SolidColorArgb = backgroundColor;

                switch (snapshot.Mode)
                {
                    case WallpaperMode.Picture:
                        foreach (var kvp in capturedPictures)
                            snapshot.PerMonitor[kvp.Key] = kvp.Value;
                        _logger.Info($"Wallpaper capture: Picture mode, {TextFormat.Plural(snapshot.PerMonitor.Count, "monitor")}");
                        break;
                    case WallpaperMode.Slideshow:
                        CaptureSlideshow(dw, snapshot);
                        _logger.Info("Wallpaper capture: Slideshow mode");
                        break;
                    case WallpaperMode.Spotlight:
                        _logger.Info("Wallpaper capture: Spotlight mode");
                        break;
                    default:
                        snapshot.Mode = WallpaperMode.Solid;
                        _logger.Info("Wallpaper capture: Solid Color mode");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Wallpaper capture failed");
                snapshot.Mode = WallpaperMode.Unknown;
            }

            return snapshot;
        }

        private static void CaptureSlideshow(IDesktopWallpaper dw, WallpaperSettings snapshot)
        {
            try
            {
                dw.GetSlideshowOptions(out var options, out uint tick);
                uint seconds = tick / 1000;
                if (!_standardIntervalSeconds.Contains(seconds))
                    _logger.Warn($"Slideshow interval reads {seconds}s, which Windows does not offer -> capturing it as-is");

                snapshot.SlideshowConfig = new SlideshowConfig
                {
                    IntervalSeconds = seconds,
                    Shuffle = options.HasFlag(DesktopSlideshowOptions.ShuffleImages),
                    SourcePaths = ReadSlideshowSourcePaths(dw),
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GetSlideshowOptions failed -> capturing without interval or shuffle");
                snapshot.SlideshowConfig = new SlideshowConfig();
            }
        }
    }
}
