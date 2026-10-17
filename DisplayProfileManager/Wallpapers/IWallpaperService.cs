using DisplayProfileManager.Wallpapers.Models;

namespace DisplayProfileManager.Wallpapers
{
    public interface IWallpaperService
    {
        WallpaperSettings Capture();
        bool Apply(WallpaperSettings snapshot);
    }
}
