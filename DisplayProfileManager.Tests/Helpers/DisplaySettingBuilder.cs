using System.Collections.Generic;
using DisplayProfileManager.Core;

namespace DisplayProfileManager.Tests.Helpers
{
    internal sealed class DisplaySettingBuilder
    {
        private readonly DisplaySetting _setting = new DisplaySetting
        {
            DeviceName = "\\\\.\\DISPLAY1",
            ReadableDeviceName = "Test Monitor",
            SourceId = 0,
            CloneGroupId = string.Empty,
            IsEnabled = true,
            DisplayPositionX = 0,
            DisplayPositionY = 0,
            Width = 1920,
            Height = 1080,
            Frequency = 60,
            Rotation = 0,
            DpiScaling = 100
        };

        public DisplaySettingBuilder WithDeviceName(string deviceName)
        {
            _setting.DeviceName = deviceName;
            return this;
        }

        public DisplaySettingBuilder WithName(string name)
        {
            _setting.ReadableDeviceName = name;
            return this;
        }

        public DisplaySettingBuilder WithEdid(string manufacturer, string productCode)
        {
            _setting.ManufacturerName = manufacturer;
            _setting.ProductCodeID = productCode;
            return this;
        }

        public DisplaySettingBuilder WithTargetId(uint id)
        {
            _setting.TargetId = id;
            return this;
        }

        public DisplaySettingBuilder WithSourceId(uint id)
        {
            _setting.SourceId = id;
            return this;
        }

        public DisplaySettingBuilder WithCloneGroup(string id)
        {
            _setting.CloneGroupId = id;
            return this;
        }

        public DisplaySettingBuilder AsCloneSource(bool isSource = true)
        {
            _setting.IsCloneSource = isSource;
            return this;
        }

        public DisplaySettingBuilder Enabled(bool enabled = true)
        {
            _setting.IsEnabled = enabled;
            return this;
        }

        public DisplaySettingBuilder Primary(bool isPrimary = true)
        {
            _setting.IsPrimary = isPrimary;
            return this;
        }

        public DisplaySettingBuilder WithPosition(int x, int y)
        {
            _setting.DisplayPositionX = x;
            _setting.DisplayPositionY = y;
            return this;
        }

        public DisplaySettingBuilder WithResolution(int width, int height)
        {
            _setting.Width = width;
            _setting.Height = height;
            return this;
        }

        public DisplaySettingBuilder WithFrequency(int hz)
        {
            _setting.Frequency = hz;
            return this;
        }
        public DisplaySettingBuilder WithRotation(int rotation)
        {
            _setting.Rotation = rotation;
            return this;
        }

        public DisplaySettingBuilder WithDpi(int dpi)
        {
            _setting.DpiScaling = (uint)dpi;
            return this;
        }

        public DisplaySettingBuilder WithHdr(bool supported, bool enabled)
        {
            _setting.IsHdrSupported = supported;
            _setting.IsHdrEnabled = enabled;
            return this;
        }

        public DisplaySettingBuilder WithAcm(bool enabled)
        {
            _setting.IsAcmEnabled = enabled;
            return this;
        }

        public DisplaySettingBuilder WithColorProfile(string colorProfile)
        {
            _setting.ColorProfile = colorProfile;
            return this;
        }

        // Sets coherent set of Original* fields as one semantic unit rather than dozen setters
        public DisplaySettingBuilder WithSavedPreCloneState(
            int positionX, int positionY, uint sourceId, bool isPrimary,
            int width, int height, int frequency, int rotation, uint dpiScaling,
            bool hdrEnabled, bool acmEnabled, string colorProfile)
        {
            _setting.OriginalSettings = true;
            _setting.OriginalPositionX = positionX;
            _setting.OriginalPositionY = positionY;
            _setting.OriginalSourceId = sourceId;
            _setting.OriginalIsPrimary = isPrimary;
            _setting.OriginalWidth = width;
            _setting.OriginalHeight = height;
            _setting.OriginalFrequency = frequency;
            _setting.OriginalRotation = rotation;
            _setting.OriginalDpiScaling = dpiScaling;
            _setting.OriginalIsHdrEnabled = hdrEnabled;
            _setting.OriginalIsAcmEnabled = acmEnabled;
            _setting.OriginalColorProfile = colorProfile;
            return this;
        }

        public DisplaySettingBuilder WithNativeResolution(int width, int height)
        {
            _setting.NativeWidth = width;
            _setting.NativeHeight = height;
            return this;
        }

        public DisplaySettingBuilder WithAvailableRefreshRates(Dictionary<string, List<int>> refreshRates)
        {
            _setting.AvailableRefreshRates = refreshRates;
            return this;
        }

        public DisplaySettingBuilder WithAvailableDpiScaling(List<uint> dpiScaling)
        {
            _setting.AvailableDpiScaling = dpiScaling;
            return this;
        }

        public DisplaySetting Build() => _setting;
    }
}