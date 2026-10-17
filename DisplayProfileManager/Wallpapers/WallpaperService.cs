using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Wallpapers
{
    public sealed class WallpaperService : IWallpaperService
    {
        public WallpaperSettings Capture() => WallpaperCapture.Capture();
        public bool Apply(WallpaperSettings snapshot) => WallpaperApply.Apply(snapshot);
    }
}
