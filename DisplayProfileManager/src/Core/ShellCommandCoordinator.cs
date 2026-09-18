using System;
using System.Threading.Tasks;

namespace DisplayProfileManager.Core
{
    internal static class ShellCommandCoordinator
    {
        public static async Task<int> ExecuteAsync(
            ShellAction action,
            Func<bool> isRegistered,
            Func<bool> register,
            Func<bool> unregister,
            Func<Task<bool>> loadSettings,
            Func<bool, Task<bool>> persistEnabled,
            Func<bool> restartExplorer,
            Action<string> warn)
        {
            bool wasRegistered = isRegistered();

            if (action == ShellAction.Register)
            {
                if (!register())
                {
                    return 1;
                }

                bool loaded = await loadSettings();
                bool persisted = loaded && await persistEnabled(true);
                if (!persisted)
                {
                    warn(loaded
                        ? "Shell extension registered, but DesktopContextMenuEnabled could not be persisted"
                        : "Shell extension registered, but settings could not be loaded for DesktopContextMenuEnabled persistence");

                    if (!wasRegistered)
                    {
                        if (!unregister())
                            warn("Shell registration persistence failed and compensating unregistration also failed");
                    }
                    else
                        warn("Shell registration was already present; leaving pre-existing external state intact despite metadata drift");

                    return 1;
                }

                return wasRegistered ? 2 : 0;
            }

            if (action == ShellAction.Unregister)
            {
                if (!unregister())
                {
                    return 1;
                }

                bool loaded = await loadSettings();
                bool persisted = loaded && await persistEnabled(false);
                if (!persisted)
                {
                    warn(loaded
                        ? "Shell extension was removed, but DesktopContextMenuEnabled=false could not be persisted; external teardown remains authoritative"
                        : "Shell extension was removed, but settings could not be loaded; external teardown remains authoritative");
                }

                if (wasRegistered && !restartExplorer())
                {
                    return 1;
                }

                return wasRegistered ? 0 : 2;
            }

            return 2;
        }
    }
}
