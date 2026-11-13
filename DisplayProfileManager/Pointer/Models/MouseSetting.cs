using System.Collections.Generic;

using Newtonsoft.Json;

namespace DisplayProfileManager.Pointer.Models
{
    public class MouseSetting
    {
        public const int MinPointerSpeed = 1;
        public const int MaxPointerSpeed = 20;
        public const int MinScrollAmount = 1;
        public const int MaxScrollAmount = 100;

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        // Pointer
        [JsonProperty("applyPointerSpeed")]
        public bool ApplyPointerSpeed { get; set; } = false;
        [JsonProperty("pointerSpeed")]
        public int PointerSpeed { get; set; } = 10;
        [JsonProperty("applyPointerPrecision")]
        public bool ApplyPointerPrecision { get; set; } = false;
        [JsonProperty("enhancePointerPrecision")]
        public bool EnhancePointerPrecision { get; set; } = true;

        // Wheel
        [JsonProperty("applyVerticalScroll")]
        public bool ApplyVerticalScroll { get; set; } = false;
        [JsonProperty("verticalScrollLines")]
        public int VerticalScrollLines { get; set; } = 3;
        [JsonProperty("applyHorizontalScroll")]
        public bool ApplyHorizontalScroll { get; set; } = false;
        [JsonProperty("horizontalScrollChars")]
        public int HorizontalScrollChars { get; set; } = 3;

        // Buttons
        [JsonProperty("applySwapButtons")]
        public bool ApplySwapButtons { get; set; } = false;
        [JsonProperty("swapButtons")]
        public bool SwapButtons { get; set; } = false;

        public bool AppliesAny() => ApplyPointerSpeed || ApplyPointerPrecision || ApplyVerticalScroll || ApplyHorizontalScroll || ApplySwapButtons;

        public MouseSetting Clone() => (MouseSetting)MemberwiseClone();

        public override string ToString()
        {
            var parts = new List<string>();
            if (ApplyPointerSpeed)
                parts.Add($"Speed: {PointerSpeed}");
            if (ApplyPointerPrecision)
                parts.Add($"Precision: {(EnhancePointerPrecision ? "On" : "Off")}");
            if (ApplyVerticalScroll)
                parts.Add($"Scroll: {VerticalScrollLines} lines");
            if (ApplyHorizontalScroll)
                parts.Add($"Horizontal scroll: {HorizontalScrollChars} characters");
            if (ApplySwapButtons)
                parts.Add($"Primary button: {(SwapButtons ? "Right" : "Left")}");

            return parts.Count > 0 ? string.Join(", ", parts) : "No mouse settings configured";
        }
    }
}
