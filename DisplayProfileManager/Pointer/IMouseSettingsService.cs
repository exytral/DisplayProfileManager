using DisplayProfileManager.Pointer.Models;

namespace DisplayProfileManager.Pointer
{
    public interface IMouseSettingsService
    {
        MouseSetting CaptureCurrent();
        bool ApplyMouseSettings(MouseSetting settings);
    }
}
