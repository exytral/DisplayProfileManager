using System;
using System.Collections.Generic;

namespace DisplayProfileManager.Core
{
    public enum ShellAction
    {
        None,
        Register,
        Unregister
    }

    public class CliOptions
    {
        public bool DevMode { get; set; }
        public bool StartInTray { get; set; }
        public bool IsRefresh { get; set; }
        public bool IsTheme { get; set; }
        public bool IsProfile { get; set; }
        public bool IsHeadless { get; set; }
        public bool IsExit { get; set; }

        public string Theme { get; set; }
        public string Profile { get; set; }

        public ShellAction ShellAction { get; set; }

        public List<string> CommandQueue { get; } = new List<string>();

        public bool WantsExistingInstance => IsRefresh || IsTheme || IsProfile || IsHeadless || IsExit;
    }

    public static class CliParser
    {
        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            if (args == null || args.Length == 0) return options;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = Normalize(args[i]);

                // Return shell actions to application layer
                if (arg == "unshell") { options.ShellAction = ShellAction.Unregister; return options; }
                if (arg == "shell") { options.ShellAction = ShellAction.Register; return options; }

                if (arg == "dev") { options.DevMode = true; continue; }
                if (arg == "tray") { options.StartInTray = true; continue; }
                if (arg == "exit") {options.IsExit = true; options.CommandQueue.Add("CMD:EXIT"); continue; }

                bool HasValue() => i + 1 < args.Length && IsValueFor(args[i + 1]);
                if (IsRefresh(arg))
                {
                    options.IsRefresh = true;
                    options.CommandQueue.Add("CMD:REFRESH");
                }
                else if (Matches(arg, "theme"))
                {
                    options.IsTheme = true;
                    if (HasValue()) options.Theme = args[++i];
                    options.CommandQueue.Add($"THEME:{options.Theme ?? ""}");
                }
                else if (Matches(arg, "profile"))
                {
                    options.IsProfile = true;
                    if (HasValue()) options.Profile = args[++i];
                }
                else if (Matches(arg, "headless"))
                {
                    options.IsHeadless = true;
                    if (HasValue()) options.Profile = args[++i];
                }
            }

            if (options.IsProfile || options.IsHeadless)
                options.CommandQueue.Add($"PROFILE:{options.Profile ?? ""}");

            return options;
        }

        public static string Normalize(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg)) return string.Empty;

            return arg.ToLowerInvariant().TrimStart('-', '/');
        }

        public static bool IsRefresh(string normalized) =>
            normalized.StartsWith("ref", StringComparison.Ordinal)
            || normalized.StartsWith("rel", StringComparison.Ordinal)
            || normalized == "r";

        public static bool Matches(string normalized, string flag)
        {
            if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(flag))
            {
                return false;
            }

            return normalized[0] == flag[0] && flag.StartsWith(normalized, StringComparison.Ordinal);
        }

        public static bool IsValueFor(string nextArg) => !string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith("-", StringComparison.Ordinal);
    }
}