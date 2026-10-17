using System.Collections.Generic;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed class DisplayEventSnapshot
    {
        public int ActiveQueryStatus { get; set; }
        public int AllPathsQueryStatus { get; set; }
        public string TopologyDigest { get; set; } = string.Empty;
        public List<DisplayEventActiveDisplay> ActiveDisplays { get; set; } = [];
        public List<DisplayEventPathState> AllPaths { get; set; } = [];
    }

    internal sealed class DisplayEventActiveDisplay
    {
        public string AdapterLuid { get; set; } = string.Empty;
        public uint SourceId { get; set; }
        public uint TargetId { get; set; }
        public uint RawTargetId { get; set; }
        public uint PathIndex { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string ManufacturerName { get; set; } = string.Empty;
        public string ProductCodeId { get; set; } = string.Empty;
        public string MonitorDevicePath { get; set; } = string.Empty;
        public string SourceGroup { get; set; } = string.Empty;
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double RefreshRate { get; set; }
        public uint Rotation { get; set; }
        public uint OutputTechnology { get; set; }
        public bool SupportsDrr { get; set; }
        public bool AdvancedColorInfoAvailable { get; set; }
        public bool HdrSupported { get; set; }
        public bool HdrEnabled { get; set; }
        public bool WcgSupported { get; set; }
        public bool WcgEnabled { get; set; }
    }

    internal sealed class DisplayEventPathState
    {
        public uint PathIndex { get; set; }
        public string AdapterLuid { get; set; } = string.Empty;
        public uint SourceId { get; set; }
        public uint TargetId { get; set; }
        public uint RawTargetId { get; set; }
        public string SourceGroup { get; set; } = string.Empty;
        public bool Active { get; set; }
        public bool TargetAvailable { get; set; }
        public uint PathFlags { get; set; }
        public uint SourceStatusFlags { get; set; }
        public uint TargetStatusFlags { get; set; }
        public uint OutputTechnology { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string ManufacturerName { get; set; } = string.Empty;
        public string ProductCodeId { get; set; } = string.Empty;
        public string MonitorDevicePath { get; set; } = string.Empty;
    }
}
