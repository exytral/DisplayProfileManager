using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr
{
    public interface IVrrService
    {
        VrrCapability GetCapability();
        bool ApplyVrrSettings(VrrSetting settings);
    }
}
