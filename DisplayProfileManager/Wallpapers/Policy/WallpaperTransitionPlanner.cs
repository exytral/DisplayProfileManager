using System.Collections.Generic;

using DisplayProfileManager.Wallpapers.Interop;
using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Wallpapers.Policy
{
        internal enum WallpaperTransitionStep
        {
            DeactivateSpotlight,
            SetBackgroundColor,
            SupersedeSlideshowOwnership,
            SetPosition,
            DisableDesktopBackground,
            SetPictureWallpapers,
            SetSlideshowSource,
            SetSlideshowOptions,
            EstablishDestinationMode,
            NotifySettings,
            VerifyDestinationMode,
            ActivateSpotlight,
        }

        internal sealed class WallpaperTransitionPlan(WallpaperMode sourceMode, WallpaperMode destinationMode, IReadOnlyList<WallpaperTransitionStep> steps)
        {
            public WallpaperMode SourceMode { get; } = sourceMode;
            public WallpaperMode DestinationMode { get; } = destinationMode;
            public IReadOnlyList<WallpaperTransitionStep> Steps { get; } = steps;
        }

    internal static class WallpaperTransitionPlanner
    {
        internal static string NormalizePosition(string pos) => WindowsWallpaperPlatform.NormalizePosition(pos);

        internal static bool ModeOwnsBackgroundColor(WallpaperMode mode) => mode == WallpaperMode.Solid || mode == WallpaperMode.Picture || mode == WallpaperMode.Slideshow;

        internal static WallpaperTransitionPlan BuildTransitionPlan(WallpaperMode sourceMode, WallpaperMode destinationMode)
        {
            if (destinationMode == WallpaperMode.Unknown)
            {
                return new WallpaperTransitionPlan(sourceMode, destinationMode, []);
            }

            var steps = new List<WallpaperTransitionStep>();
            if (destinationMode == WallpaperMode.Spotlight)
            {
                steps.Add(WallpaperTransitionStep.ActivateSpotlight);
                return new WallpaperTransitionPlan(sourceMode, destinationMode, steps);
            }

            steps.Add(WallpaperTransitionStep.DeactivateSpotlight);
            steps.Add(WallpaperTransitionStep.SetBackgroundColor);

            if (destinationMode == WallpaperMode.Solid)
            {
                if (sourceMode == WallpaperMode.Slideshow)
                    steps.Add(WallpaperTransitionStep.SupersedeSlideshowOwnership);
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
            }
            else if (destinationMode == WallpaperMode.Picture)
            {
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
                steps.Add(WallpaperTransitionStep.SetPictureWallpapers);
                steps.Add(WallpaperTransitionStep.SetPosition);
            }
            else if (destinationMode == WallpaperMode.Slideshow)
            {
                steps.Add(WallpaperTransitionStep.SetPosition);
                steps.Add(WallpaperTransitionStep.DisableDesktopBackground);
                steps.Add(WallpaperTransitionStep.SetSlideshowSource);
                steps.Add(WallpaperTransitionStep.SetSlideshowOptions);
            }

            steps.Add(WallpaperTransitionStep.EstablishDestinationMode);
            steps.Add(WallpaperTransitionStep.NotifySettings);
            steps.Add(WallpaperTransitionStep.VerifyDestinationMode);
            return new WallpaperTransitionPlan(sourceMode, destinationMode, steps);
        }
    }
}
