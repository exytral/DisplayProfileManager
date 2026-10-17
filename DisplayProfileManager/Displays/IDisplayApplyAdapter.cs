using System.Collections.Generic;
using System.Threading.Tasks;

using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays
{
    internal interface IDisplayApplyAdapter
    {
        List<DisplayConfigInfo> GetActiveDisplays();
        List<DisplayConfigInfo> GetDisplayAddresses();
        bool ApplyTopology(List<DisplayConfigInfo> displays);
        Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays);
        bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay);
        bool ApplyAdvancedColor(List<DisplayConfigInfo> displays);
        bool ApplyColorProfiles(List<DisplayConfigInfo> displays);
    }
}
