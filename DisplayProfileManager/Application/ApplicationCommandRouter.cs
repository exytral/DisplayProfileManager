using System;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Application
{
    internal sealed class ApplicationCommandRouter(ProfileService profiles, ProfileApplyService apply, SettingsService settings, IThemeService themes)
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private readonly ProfileService _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        private readonly ProfileApplyService _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        private readonly SettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        private readonly IThemeService _themes = themes ?? throw new ArgumentNullException(nameof(themes));

        public event Action<Profile, ProfileApplyResult> ProfileApplyFailed;

        public async Task<ProfileApplyResult> ApplyProfileAsync(string nameOrId, ApplySource source)
        {
            if (string.IsNullOrEmpty(nameOrId))
                nameOrId = _settings.GetCurrentProfileId();

            var profile = _profiles.ResolveProfileNameOrId(nameOrId);
            if (profile == null)
            {
                _logger.Warn($"Profile '{nameOrId}' not found");
                return new ProfileApplyResult { Success = false };
            }

            var result = await _apply.ApplyProfileAsync(profile, source);
            if (!result.Success)
                ProfileApplyFailed?.Invoke(profile, result);

            return result;
        }

        public async Task<bool> HandleIpcMessageAsync(string receivedValue, Action requestExit)
        {
            if (receivedValue == "CMD:EXIT")
            {
                _logger.Info("--exit received. Shutting down.");
                requestExit?.Invoke();
                return true;
            }

            if (receivedValue == "CMD:REFRESH")
            {
                await _profiles.LoadProfilesAsync();
                _themes.RefreshThemes();
                _themes.ApplyTheme(_settings.Settings.Theme);
                return true;
            }

            if (receivedValue.StartsWith("THEME:", StringComparison.Ordinal))
            {
                string targetTheme = receivedValue[6..];
                if (!string.IsNullOrEmpty(targetTheme))
                {
                    _themes.RefreshThemes();
                    if (!_themes.ThemeExists(targetTheme))
                    {
                        _logger.Error($"IPC: '{targetTheme}' is not available theme. Ignored.");
                        return false;
                    }

                    if (!await _settings.SetThemeAsync(targetTheme))
                    {
                        return false;
                    }
                }

                _themes.RefreshThemes();
                return true;
            }

            if (CliParser.TryParseProfileCommand(receivedValue, out string targetProfile, out ApplySource source))
            {
                return (await ApplyProfileAsync(targetProfile, source)).Success;
            }

            return false;
        }
    }
}
