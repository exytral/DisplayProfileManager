using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Helpers
{
    internal sealed class DisplayConfigInfoBuilder
    {
        private readonly DisplayConfigHelper.DisplayConfigInfo _info = new DisplayConfigHelper.DisplayConfigInfo
        {

            DeviceName       = "\\\\.\\DISPLAY1",
            FriendlyName     = "Test Monitor",
            TargetId         = 0,
            SourceId         = 0,
            IsEnabled        = true,
            DisplayPositionX = 0,
            DisplayPositionY = 0,
            Width            = 1920,
            Height           = 1080,
            RefreshRate      = 60
        };

        public DisplayConfigInfoBuilder WithDeviceName(string deviceName)
        {
            _info.DeviceName = deviceName;
            return this;
        }

        public DisplayConfigInfoBuilder WithFriendlyName(string name)
        {
            _info.FriendlyName = name;
            return this;
        }

        public DisplayConfigInfoBuilder WithEdid(string manufacturer, string productCode)
        {
            _info.ManufacturerName = manufacturer;
            _info.ProductCodeID = productCode;
            return this;
        }

        public DisplayConfigInfoBuilder WithTargetId(uint id)
        {
            _info.TargetId = id;
            return this;
        }

        public DisplayConfigInfoBuilder WithRawTargetId(uint id)
        {
            _info.RawTargetId = id;
            return this;
        }

        public DisplayConfigInfoBuilder WithSourceId(uint id)
        {
            _info.SourceId = id;
            return this;
        }

        public DisplayConfigInfoBuilder Enabled(bool enabled = true)
        {
            _info.IsEnabled = enabled;
            return this;
        }

        public DisplayConfigInfoBuilder Disabled()
        {
            _info.IsEnabled = false;
            return this;
        }

        public DisplayConfigInfoBuilder WithPosition(int x, int y)
        {
            _info.DisplayPositionX = x;
            _info.DisplayPositionY = y;
            return this;
        }

        public DisplayConfigInfoBuilder WithResolution(int width, int height)
        {
            _info.Width  = width;
            _info.Height = height;
            return this;
        }

        public DisplayConfigInfoBuilder WithRefreshRate(double hz)
        {
            _info.RefreshRate = hz;
            return this;
        }

        public DisplayConfigInfoBuilder WithRotation(DisplayConfigHelper.DisplayConfigRotation rotation)
        {
            _info.Rotation = rotation;
            return this;
        }

        public DisplayConfigInfoBuilder WithHdr(bool supported, bool enabled)
        {
            _info.IsHdrSupported = supported;
            _info.IsHdrEnabled = enabled;
            return this;
        }

        public DisplayConfigInfoBuilder WithAcm(bool enabled)
        {
            _info.IsAcmEnabled = enabled;
            return this;
        }

        public DisplayConfigInfoBuilder WithColorProfile(string colorProfile)
        {
            _info.ColorProfile = colorProfile;
            return this;
        }

        public DisplayConfigInfoBuilder WithNativeResolution(int width, int height)
        {
            _info.NativeWidth = width;
            _info.NativeHeight = height;
            return this;
        }

        public DisplayConfigHelper.DisplayConfigInfo Build() => _info;
    }
}