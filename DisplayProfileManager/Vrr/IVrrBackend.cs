using DisplayProfileManager.Vrr.Models;

namespace DisplayProfileManager.Vrr
{
    // One vendor implementation behind VrrService
    internal interface IVrrBackend
    {
        VrrCapability GetCapability();
        bool Apply(VrrMode mode);
    }
}
