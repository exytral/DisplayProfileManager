using System.Collections.Generic;

using DisplayProfileManager.Displays.Interop;

namespace DisplayProfileManager.Displays.Models
{
    public class DisplayInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceString { get; set; } = string.Empty;
        public string ReadableDeviceName { get; set; } = string.Empty;
        public string DeviceInstanceId { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Frequency { get; set; }
        public int BitsPerPixel { get; set; }
        public bool IsInterlaced { get; set; }
        public bool IsPrimary { get; set; }
        public GdiDisplayAdapter.DEVMODE DevMode { get; set; }
    }

    public class DisplayCapabilities
    {
        public List<string> Resolutions { get; set; } = [];
        public Dictionary<string, List<int>> RefreshRates { get; set; } = [];
    }
}
