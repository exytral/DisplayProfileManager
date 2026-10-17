using System;
using System.Threading.Tasks;

using DisplayProfileManager.Integration;
using DisplayProfileManager.Settings;

namespace DisplayProfileManager.Application
{
    internal sealed class ShellCommandCoordinator(SettingsService settings, IShellExtensionRegistrar shell)
    {
        private readonly SettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        private readonly IShellExtensionRegistrar _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        public async Task<int> ExecuteAsync(ShellAction action, Action<string> warn)
        {
            bool wasRegistered = _shell.IsRegistered();

            if (action == ShellAction.Register || action == ShellAction.RegisterDeveloper)
            {
                ShellRegistrationMode previousMode = wasRegistered
                    ? _shell.GetRegistrationMode()
                    : ShellRegistrationMode.Normal;
                ShellRegistrationMode requestedMode = action == ShellAction.RegisterDeveloper
                    ? ShellRegistrationMode.Developer
                    : ShellRegistrationMode.Normal;

                if (!_shell.Register(requestedMode))
                {
                    return 1;
                }

                bool loaded = await _settings.LoadSettingsAsync();
                bool persisted = loaded && await _settings.SetDesktopContextMenuAsync(true);
                if (!persisted)
                {
                    warn(loaded
                        ? "Shell extension registered, but DesktopContextMenuEnabled could not be persisted"
                        : "Shell extension registered, but settings could not be loaded for DesktopContextMenuEnabled persistence");

                    if (!wasRegistered)
                    {
                        if (!_shell.Unregister())
                            warn("Shell registration persistence failed and compensating unregistration also failed");
                    }
                    else if (!_shell.Register(previousMode))
                    {
                        warn("Shell registration persistence failed and the previous routing mode could not be restored");
                    }

                    return 1;
                }

                return wasRegistered ? 2 : 0;
            }

            if (action == ShellAction.Unregister)
            {
                if (!_shell.Unregister())
                {
                    return 1;
                }

                bool loaded = await _settings.LoadSettingsAsync();
                bool persisted = loaded && await _settings.SetDesktopContextMenuAsync(false);
                if (!persisted)
                {
                    warn(loaded
                        ? "Shell extension was removed, but DesktopContextMenuEnabled=false could not be persisted; external teardown remains authoritative"
                        : "Shell extension was removed, but settings could not be loaded; external teardown remains authoritative");
                }

                if (wasRegistered && !_shell.RestartExplorer())
                {
                    return 1;
                }
                return wasRegistered ? 0 : 2;
            }

            return 2;
        }
    }
}
