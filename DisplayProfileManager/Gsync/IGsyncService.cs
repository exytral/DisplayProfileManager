using DisplayProfileManager.Gsync.Models;

namespace DisplayProfileManager.Gsync
{
    public interface IGsyncService
    {
        bool IsAvailable();
        GsyncMode? GetCurrentMode();
        bool ApplyGsyncSettings(GsyncSetting settings);
    }
}
