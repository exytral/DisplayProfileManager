using DisplayProfileManager.Settings.Models;

namespace DisplayProfileManager.Integration
{
    public interface IAutoStartService
    {
        bool IsAutoStartEnabled();
        AutoStartOperationResult EnableAutoStart(AutoStartMode mode, bool startInTray = false);
        AutoStartOperationResult DisableAutoStart();
    }
}
