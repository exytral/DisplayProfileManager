using DisplayProfileManager.Wallpapers.Models;
using DisplayProfileManager.Wallpapers.Policy;

namespace DisplayProfileManager.UI.Presentation
{
    public static class WallpaperModeFormatter
    {
        public static readonly string[] AllPositions = ["fill", "fit", "stretch", "tile", "center", "span"];

        public static string Display(WallpaperMode mode) => mode == WallpaperMode.Solid ? "Solid Color" : mode.ToString();
        public static string NormalizePosition(string position) => WallpaperTransitionPlanner.NormalizePosition(position);
    }
}
