using System;

using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.Windows
{
    internal sealed class QuickSwitchProfileRefreshCoordinator
    {
        private readonly Action _refresh;

        internal QuickSwitchProfileRefreshCoordinator(Action refresh)
        {
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        internal void OnProfilesLoaded(object sender, EventArgs e) => _refresh();
        internal void OnProfileAdded(object sender, Profile profile) => _refresh();
        internal void OnProfileUpdated(object sender, Profile profile) => _refresh();
        internal void OnProfileDeleted(object sender, string profileId) => _refresh();
    }
}
