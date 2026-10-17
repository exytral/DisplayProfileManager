using System;
using System.Threading.Tasks;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.Windows
{
    internal sealed class QuickSwitchInteractionController
    {
        private readonly Func<Profile, ApplySource, Task<ProfileApplyResult>> _apply;
        private readonly Action _close;
        private readonly Action<Profile, ProfileApplyResult> _applyCompleted;
        private bool _applying;
        private bool _dismissed;

        internal bool IsApplying => _applying;
        internal bool IsDismissed => _dismissed;

        internal QuickSwitchInteractionController(
            Func<Profile, ApplySource, Task<ProfileApplyResult>> apply,
            Action close,
            Action<Profile, ProfileApplyResult> applyCompleted)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _close = close ?? throw new ArgumentNullException(nameof(close));
            _applyCompleted = applyCompleted;
        }

        internal async Task<bool> ApplyAsync(Profile profile)
        {
            if (_applying || _dismissed || profile == null)
            {
                return false;
            }

            _applying = true;
            _dismissed = true;
            _close();

            var result = await _apply(profile, ApplySource.QuickSwitch);
            _applyCompleted?.Invoke(profile, result);
            return true;
        }

        internal bool Dismiss() => Dismiss(_close);

        internal bool Dismiss(Action closeAction)
        {
            if (_applying || _dismissed)
            {
                return false;
            }

            _dismissed = true;
            (closeAction ?? _close)();
            return true;
        }

        internal bool DismissAndExecute(Action action)
        {
            if (!Dismiss())
            {
                return false;
            }

            action?.Invoke();
            return true;
        }
    }
}
