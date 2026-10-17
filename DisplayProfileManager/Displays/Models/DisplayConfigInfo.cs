using static DisplayProfileManager.Displays.Interop.DisplayConfigNative;

namespace DisplayProfileManager.Displays.Models
{
    public class DisplayConfigInfo
    {
        // Identity
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string ManufacturerName { get; set; } = string.Empty;
        public string ProductCodeID { get; set; } = string.Empty;
        public string MonitorDevicePath { get; set; } = string.Empty;
        public AdapterLuid AdapterId { get; set; }
        public uint TargetId { get; set; }
        public uint RawTargetId { get; set; }
        public uint SourceId { get; set; }
        public uint PathIndex { get; set; }
        public DisplayConfigVideoOutputTechnology OutputTechnology { get; set; }
        // State
        public bool IsEnabled { get; set; }
        public bool IsPrimary { get; set; }
        // Layout
        public int DisplayPositionX { get; set; }
        public int DisplayPositionY { get; set; }
        // Active Configuration
        public int Width { get; set; }
        public int Height { get; set; }
        public double RefreshRate { get; set; }
        public DisplayConfigRotation Rotation { get; set; } = DisplayConfigRotation.Identity;
        public bool IsHdrSupported { get; set; } = false;
        public bool IsWcgSupported { get; set; } = false;
        public bool IsHdrEnabled { get; set; } = false;
        public bool IsWcgEnabled { get; set; } = false;
        public bool IsAdvancedColorInfoAvailable { get; set; } = true;
        // DRR Capability
        public bool SupportsDrr { get; set; } = false;
        public DisplayConfigColorEncoding ColorEncoding { get; set; } = DisplayConfigColorEncoding.Rgb;
        public uint BitsPerColorChannel { get; set; } = 8;
        public string ColorProfile { get; set; } = null;
        // Native
        public int NativeWidth { get; set; } = 0;
        public int NativeHeight { get; set; } = 0;
    }
}
