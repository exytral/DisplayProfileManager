using System;
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
        public const int MinDoubleClickTime = 200;
        public const int MaxDoubleClickTime = 900;
        public const int MaxMouseTrails = 16;

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

        // Double-click speed
        [JsonProperty("applyDoubleClickTime")]
        public bool ApplyDoubleClickTime { get; set; } = false;
        [JsonProperty("doubleClickTime")]
        public int DoubleClickTime { get; set; } = 500;

        // Pointer trails, 0 = off
        [JsonProperty("applyMouseTrails")]
        public bool ApplyMouseTrails { get; set; } = false;
        [JsonProperty("mouseTrails")]
        public int MouseTrails { get; set; } = 0;

        public bool AppliesAny() => ApplyPointerSpeed || ApplyPointerPrecision || ApplyVerticalScroll || ApplyHorizontalScroll || ApplySwapButtons
            || ApplyDoubleClickTime || ApplyMouseTrails;

        public static int ClampDoubleClickTime(int milliseconds) => Math.Clamp(milliseconds, MinDoubleClickTime, MaxDoubleClickTime);

        // Windows treats 0 and 1 as off; anything else is a trail length
        public static int NormalizeMouseTrails(int length) => length <= 1 ? 0 : Math.Min(length, MaxMouseTrails);

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
            if (ApplyDoubleClickTime)
                parts.Add($"Double-click: {DoubleClickTime} ms");
            if (ApplyMouseTrails)
                parts.Add($"Trails: {(MouseTrails > 1 ? MouseTrails.ToString() : "Off")}");

            return parts.Count > 0 ? string.Join(", ", parts) : "No mouse settings configured";
        }
    }
}
