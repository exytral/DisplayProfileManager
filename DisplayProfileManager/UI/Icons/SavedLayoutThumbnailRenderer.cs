using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Displays;

namespace DisplayProfileManager.UI.Icons
{
    internal static class SavedLayoutThumbnailRenderer
    {
        internal static ImageSource Create(IReadOnlyList<DisplaySetting> displaySettings, double size)
        {
            if (!double.IsFinite(size) || size <= 0)
            {
                return null;
            }

            var geometry = SavedLayoutProjector.Build(displaySettings ?? []);
            var projection = SavedLayoutProjector.Fit(geometry, size, size, Math.Max(1.0, size * 0.08));
            if (projection.Footprints.Count == 0)
            {
                return null;
            }

            Brush fill = CreateFillBrush();
            var drawing = new DrawingGroup();
            using (DrawingContext context = drawing.Open())
            {
                foreach (var footprint in projection.Footprints)
                {
                    var rect = new Rect(
                        footprint.X,
                        footprint.Y,
                        Math.Max(1.0, footprint.Width),
                        Math.Max(1.0, footprint.Height));
                    double radius = Math.Max(0.75, size * 0.045);
                    context.DrawRoundedRectangle(fill, null, rect, radius, radius);
                }
            }

            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        private static Brush CreateFillBrush()
        {
            Brush source = System.Windows.Application.Current?.TryFindResource("PrimaryTextBrush") as Brush
                ?? SystemColors.WindowTextBrush;
            Brush fill = source.CloneCurrentValue();
            fill.Opacity = 0.9;
            if (fill.CanFreeze)
                fill.Freeze();

            return fill;
        }
    }
}
