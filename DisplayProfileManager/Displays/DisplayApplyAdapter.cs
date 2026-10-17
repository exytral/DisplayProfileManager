using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using DisplayProfileManager.Displays.ColorProfiles;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;

namespace DisplayProfileManager.Displays
{
    internal sealed class DisplayApplyAdapter(Func<bool> forceTopologyRecovery = null, bool displayEventTraceEnabled = false) : IDisplayApplyAdapter
    {
        private readonly Func<bool> _forceTopologyRecovery = forceTopologyRecovery;
        private readonly bool _displayEventTraceEnabled = displayEventTraceEnabled;

        public List<DisplayConfigInfo> GetActiveDisplays() => DisplayConfigService.GetDisplayConfigs();
        public List<DisplayConfigInfo> GetDisplayAddresses() => DisplayConfigService.GetAllPathDisplayAddresses();
        public bool ApplyTopology(List<DisplayConfigInfo> displays) => DisplayConfigService.ApplyDisplayTopology(displays, _forceTopologyRecovery?.Invoke() == true);
        public Task<DisplayConfigService.DisplayConfigApplyResult> ApplyDisplayConfigDetailed(List<DisplayConfigInfo> displays) => DisplayConfigService.ApplyDisplayConfigDetailed(displays, _displayEventTraceEnabled);
        public bool ApplyDpi(DisplaySetting setting, DisplayConfigInfo liveDisplay) => DpiScalingAdapter.SetDPIScaling(setting.DeviceName, setting.DpiScaling, liveDisplay);
        public bool ApplyAdvancedColor(List<DisplayConfigInfo> displays) => AdvancedColorService.ApplyAdvancedColorState(displays);
        public bool ApplyColorProfiles(List<DisplayConfigInfo> displays) => ColorProfileAdapter.ApplyColorProfiles(displays);
    }
}
