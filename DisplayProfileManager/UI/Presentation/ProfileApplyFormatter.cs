using System.Collections.Generic;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.Presentation
{
    public static class ProfileApplyFormatter
    {

        internal static string GetApplyWarningSummary(ProfileApplyResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var failures = new List<string>();
            if (!result.AdvancedColorSuccess) failures.Add("Advanced Color");
            if (!result.ColorProfileSuccess) failures.Add("Color Profile");
            if (!result.DpiChanged) failures.Add("DPI");
            if (!result.WallpaperSuccess) failures.Add("Wallpaper");
            if (!result.AudioSuccess) failures.Add("Audio");
            if (!result.CurrentProfilePersisted) failures.Add("Current Profile Marker");

            return string.Join(", ", failures);
        }

        internal static string AppendApplyWarnings(string message, string warningSummary) => string.IsNullOrEmpty(warningSummary) ? message : $"{message} — {warningSummary} failed to apply";

        internal static string GetApplySourceLabel(ApplySource source) => source switch
        {
            ApplySource.Window => "Applied",
            ApplySource.Tray => "Applied from tray",
            ApplySource.Hotkey => "Applied by hotkey",
            ApplySource.QuickSwitch => "Applied by Quick Switch",
            ApplySource.CommandLine => "Applied via CLI",
            ApplySource.DesktopContextMenu => "Applied from desktop context menu",
            ApplySource.StartMenuShortcut => "Applied from Start menu",
            ApplySource.Startup => "Applied at startup",
            _ => "Applied"
        };

        public static string GetApplyResultErrorMessage(string profileName, ProfileApplyResult result)
        {
            string errorDetails =
                $"Failed to apply profile '{profileName}'.\n" +
                $"Some settings may not have been applied correctly.\n\n" +
                $"Display Layout: {result.DisplayConfigApplied},\n" +
                $"Advanced Color: {result.AdvancedColorSuccess},\n" +
                $"Color Profile: {result.ColorProfileSuccess},\n" +
                $"DPI: {result.DpiChanged},\n" +
                $"Wallpaper: {result.WallpaperSuccess},\n" +
                $"Audio: {result.AudioSuccess}";

            return errorDetails;
        }
    }
}
