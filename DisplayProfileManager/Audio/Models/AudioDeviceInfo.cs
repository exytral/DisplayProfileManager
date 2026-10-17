namespace DisplayProfileManager.Audio.Models
{
    public enum DeviceType
    {
        Playback,
        Capture
    }

    public class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SystemName { get; set; }
        public bool IsActive { get; set; }
        public bool IsAvailable { get; set; } = true;
        public DeviceType Type { get; set; }

        public override string ToString()
        {
            var name = SystemName ?? Name ?? "Unknown Device";
            return IsAvailable ? name : $"{name} (Unavailable)";
        }
    }
}
