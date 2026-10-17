using System;

namespace DisplayProfileManager.Profiles.Models
{
    public enum ApplySource { Unknown, Window, Tray, Hotkey, QuickSwitch, CommandLine, DesktopContextMenu, StartMenuShortcut, Startup }

    public class ProfileAppliedEventArgs(Profile profile, ApplySource source, long durationMilliseconds, string warningSummary = null) : EventArgs
    {
        public Profile Profile { get; } = profile;
        public ApplySource Source { get; } = source;
        public long DurationMilliseconds { get; } = durationMilliseconds;
        public string WarningSummary { get; } = warningSummary ?? string.Empty;
    }

    public class ProfileApplyResult
    {
        public bool Success { get; set; }
        public bool PrimaryChanged { get; set; }
        public bool DisplayConfigApplied { get; set; }
        public bool ResolutionChanged { get; set; }
        public bool DpiChanged { get; set; }
        public bool AudioSuccess { get; set; }
        public bool WallpaperSuccess { get; set; } = true;
        public bool AdvancedColorSuccess { get; set; } = true;
        public bool ColorProfileSuccess { get; set; } = true;
        public bool CurrentProfilePersisted { get; set; } = true;
    }
}
