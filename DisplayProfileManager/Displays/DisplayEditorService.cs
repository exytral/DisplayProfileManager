using System.Collections.Generic;
using System.Windows;

using DisplayProfileManager.Displays.ColorProfiles;
using DisplayProfileManager.Displays.Interop;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;

namespace DisplayProfileManager.Displays
{
    public sealed class DisplayEditorService
    {
        public List<DisplayConfigInfo> GetLiveDisplays() => DisplayConfigService.GetDisplayConfigs();
        public List<DisplayInfo> GetGdiDisplays() => GdiDisplayAdapter.GetDisplays();
        public DisplayCapabilities GetCapabilities(string deviceName) => GdiDisplayAdapter.GetDisplayCapabilities(deviceName);
        public List<ResolutionInfo> GetAvailableResolutions(string deviceName) => GdiDisplayAdapter.GetAvailableResolutions(deviceName);
        public List<string> GetSupportedResolutionsOnly(string deviceName) => GdiDisplayAdapter.GetSupportedResolutionsOnly(deviceName);
        public List<int> GetAvailableRefreshRates(string deviceName, int width, int height) => GdiDisplayAdapter.GetAvailableRefreshRates(deviceName, width, height);
        public DpiScalingInfo GetDpiScaling(DisplayConfigInfo display) => DpiScalingAdapter.GetDPIScalingInfo(display.DeviceName, display);
        public List<uint> GetSupportedDpiScaling(string deviceName) => [.. DpiScalingAdapter.GetSupportedDpiScalingOnly(deviceName)];
        public IReadOnlyList<string> GetColorProfiles(bool hdrOnly) => ColorProfileCatalog.GetInstalledColorProfilesFiltered(hdrOnly);
        public string GetDefaultColorProfile(DisplayConfigInfo display) => ColorProfileAdapter.GetDisplayDefaultColorProfile(display.AdapterId, display.SourceId);
        public bool TryGetMonitorBounds(string deviceName, out Rect bounds) => MonitorBoundsProvider.TryGetMonitorBounds(deviceName, out bounds);
        public bool IsMonitorConnected(string deviceName) => GdiDisplayAdapter.IsMonitorConnected(deviceName);
        public DisplayConfigInfo ResolveLiveDisplay(DisplaySetting setting, List<DisplayConfigInfo> liveConfigs) => DisplayAddressResolver.ResolveLiveDisplay(setting, liveConfigs);
    }
}
