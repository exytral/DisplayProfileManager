using DisplayProfileManager.Core;
using System.Diagnostics;
using System.Reflection;

namespace DisplayProfileManager.Helpers
{
    public static class AboutHelper
    {
        public static string GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
            if (!string.IsNullOrEmpty(fileVersion))
                return fileVersion;

            return assembly.GetName().Version?.ToString() ?? "Error";
        }

        public static string GetInformationalVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrEmpty(informationalVersion))
                informationalVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;

            return informationalVersion ?? GetVersion();
        }

        public static string GetSettingsPath()
        {
            return SettingsManager.Instance.GetSettingsFilePath();
        }

        public static class Libraries
        {
            public const string NLogName = "NLog";
            public const string NLogVersion = "6.0.4";
            public const string NLogLicense = "BSD-3-Clause";
            public const string NLogUrl = "https://nlog-project.org/";

            public const string NewtonsoftName = "Newtonsoft.Json";
            public const string NewtonsoftVersion = "13.0.3";
            public const string NewtonsoftLicense = "MIT";
            public const string NewtonsoftUrl = "https://www.newtonsoft.com/json";

            public const string AudioSwitcherName = "AudioSwitcher.AudioApi";
            public const string AudioSwitcherVersion = "3.0.0";
            public const string AudioSwitcherLicense = "Ms-PL";
            public const string AudioSwitcherUrl = "https://github.com/xenolightning/AudioSwitcher";

            public const string AudioSwitcherCoreAudioName = "AudioSwitcher.AudioApi.CoreAudio";
            public const string AudioSwitcherCoreAudioVersion = "3.0.3";
            public const string AudioSwitcherCoreAudioLicense = "Ms-PL";
            public const string AudioSwitcherCoreAudioUrl = "https://github.com/xenolightning/AudioSwitcher";
        }

        public static class Contributors
        {
            public const string Zac15987Name = "@zac15987";
            public const string Zac15987Url = "https://github.com/zac15987";
            public const string Zac15987Desc = "Original project — profiles, system tray, themes, hotkeys, audio switching, EDID monitor identification, auto-start, and more";

            public const string JarandalName = "@jarandal";
            public const string JarandalUrl = "https://github.com/jarandal";
            public const string JarandalPrUrl = "https://github.com/zac15987/DisplayProfileManager/pull/8";
            public const string JarandalDesc = "Initial HDR support and screen rotation (PR #8)";

            public const string JonathanasdfName = "@jonathanasdf";
            public const string JonathanasdfUrl = "https://github.com/jonathanasdf";
            public const string JonathanasdfPrUrl = "https://github.com/zac15987/DisplayProfileManager/pull/14";
            public const string JonathanasdfDesc = "Initial clone display support (PR #14)";

            public const string RvahilarioName = "@rvahilario";
            public const string RvahilarioUrl = "https://github.com/rvahilario";
            public const string RvahilarioPrUrl = "https://github.com/zac15987/DisplayProfileManager/pull/23";
            public const string RvahilarioDesc = "Partial clone fixes, clone UI, and test infrastructure (PR #23)";

            public const string XtrillaName = "@xtrilla";
            public const string XtrillaUrl = "https://github.com/xtrilla";
            public const string XtrillaRepoUrl = "https://github.com/xtrilla/DisplayProfileManager";
            public const string XtrillaDesc = "Audio resource leak fix, safe file saves, crash fixes (fork)";

            public const string ExytralName = "@exytral";
            public const string ExytralUrl = "https://github.com/exytral";
            public const string ExytralRepoUrl = "https://github.com/exytral/DisplayProfileManager";
            public const string ExytralDesc = "Display engine rewrite, full clone display support, scripts, CLI, theme system overhaul, and UI refresh";

            // Community requesters — credited inline in zac15987's contributor entry
            public const string CatriksUrl = "https://github.com/Catriks";
            public const string AlienmarioUrl = "https://github.com/Alienmario";
            public const string AnodynosUrl = "https://github.com/anodynos";
        }
    }
}