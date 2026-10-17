using System;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed class DisplayRawEvent
    {
        public long Sequence { get; set; }
        public DateTime WallUtc { get; set; }
        public long MonotonicTicks { get; set; }
        public int MessageId { get; set; }
        public string Message { get; set; }
        public long WParam { get; set; }
        public string SubtypeName { get; set; }
        public int? BitsPerPixel { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public uint? DeviceType { get; set; }
        public Guid? DeviceClassGuid { get; set; }
        public string DeviceInterfacePath { get; set; }
        public Guid? PowerSettingGuid { get; set; }
        public int? PowerStateValue { get; set; }
        public string PowerStateName { get; set; }
    }
}
