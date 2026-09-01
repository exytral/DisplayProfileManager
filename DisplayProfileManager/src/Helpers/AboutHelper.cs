using DisplayProfileManager.Core;
using System;
using System.Diagnostics;
using System.Linq;
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
            {
                return fileVersion;
            }

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

        public static string GetSettingsPath() => SettingsManager.Instance.GetSettingsFilePath();

        public static class Libraries
        {
            public const string NewtonsoftName = "Newtonsoft.Json";
            public static string NewtonsoftVersion => GetLoadedVersion(NewtonsoftName);
            public const string NewtonsoftLicense = "MIT";
            public const string NewtonsoftUrl = "https://www.newtonsoft.com/json";

            public const string NLogName = "NLog";
            public static string NLogVersion => GetLoadedVersion(NLogName);
            public const string NLogLicense = "BSD-3-Clause";
            public const string NLogUrl = "https://nlog-project.org/";

            private static string GetLoadedVersion(string assemblyName)
            {
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == assemblyName);

                    if (asm == null) return string.Empty;

                    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    if (!string.IsNullOrEmpty(info))
                    {
                        return info.Split('+')[0];
                    }

                    return asm.GetName().Version?.ToString(3) ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public static class Contributors
        {
            // DPM-CS
            public const string ExytralName = "@exytral";
            public const string ExytralUrl = "https://github.com/exytral";
            public const string ExytralLinkUrl = "https://github.com/exytral/DisplayProfileManager";
            public const string ExytralLinkLabel = "DPM-CS";
            public const string ExytralDesc = "Display engine, audio, and CLI rewrite; wallpaper, scripts, and UI refresh";

            public const string VivittelName = "@vivittel";
            public const string VivittelUrl = "https://github.com/vivittel";
            public const string VivittelLinkUrl = "https://github.com/vivittel/DisplayProfileManager";
            public const string VivittelLinkLabel = "PR #1";
            public const string VivittelDesc = "HDR and advanced color state detection fixes";
            
            // Upstream
            public const string Zac15987Name = "@zac15987";
            public const string Zac15987Url = "https://github.com/zac15987";
            public const string Zac15987LinkUrl = "https://github.com/zac15987/DisplayProfileManager";
            public const string Zac15987LinkLabel = "Original Project";
            public const string Zac15987Desc = "Display profiles, system tray, auto-start, global hotkeys, initial audio switching support";

            public const string JarandalName = "@jarandal";
            public const string JarandalUrl = "https://github.com/jarandal";
            public const string JarandalLinkUrl = "https://github.com/zac15987/DisplayProfileManager/pull/8";
            public const string JarandalLinkLabel = "PR #8";
            public const string JarandalDesc = "Initial HDR and screen rotation support";

            public const string JonathanasdfName = "@jonathanasdf";
            public const string JonathanasdfUrl = "https://github.com/jonathanasdf";
            public const string JonathanasdfLinkUrl = "https://github.com/zac15987/DisplayProfileManager/pull/14";
            public const string JonathanasdfLinkLabel = "PR #14";
            public const string JonathanasdfDesc = "Initial clone display support";

            public const string RvahilarioName = "@rvahilario";
            public const string RvahilarioUrl = "https://github.com/rvahilario";
            public const string RvahilarioLinkUrl = "https://github.com/zac15987/DisplayProfileManager/pull/23";
            public const string RvahilarioLinkLabel = "PR #23";
            public const string RvahilarioDesc = "Partial clone fixes, clone UI, and test infrastructure";

            // Community requesters
            public const string CatriksUrl = "https://github.com/Catriks";
            public const string AlienmarioUrl = "https://github.com/Alienmario";
            public const string AnodynosUrl = "https://github.com/anodynos";
            public const string XtrillaUrl = "https://github.com/xtrilla";
            public const string FfgtthrUrl = "https://github.com/ffgtthr";
        }
    }
}