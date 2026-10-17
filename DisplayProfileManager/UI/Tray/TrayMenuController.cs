using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DisplayProfileManager.Common;
using DisplayProfileManager.Profiles;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.Tray
{
    public sealed class TrayMenuController
    {
        private readonly ProfileService _profiles;
        private readonly ProfileApplyService _apply;

        public event EventHandler<Profile> ProfileAdded
        {
            add => _profiles.ProfileAdded += value;
            remove => _profiles.ProfileAdded -= value;
        }

        public event EventHandler<Profile> ProfileUpdated
        {
            add => _profiles.ProfileUpdated += value;
            remove => _profiles.ProfileUpdated -= value;
        }

        public event EventHandler<string> ProfileDeleted
        {
            add => _profiles.ProfileDeleted += value;
            remove => _profiles.ProfileDeleted -= value;
        }

        public event EventHandler ProfilesLoaded
        {
            add => _profiles.ProfilesLoaded += value;
            remove => _profiles.ProfilesLoaded -= value;
        }

        public event EventHandler<ProfileAppliedEventArgs> ProfileApplied
        {
            add => _profiles.ProfileApplied += value;
            remove => _profiles.ProfileApplied -= value;
        }

        public string CurrentProfileId => _profiles.CurrentProfileId;

        internal TrayMenuController(ProfileService profiles, ProfileApplyService apply)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public List<Profile> GetProfiles() => [.. _profiles.GetAllProfiles().OrderBy(p => p.Name, NaturalStringComparer.Instance)];

        public Profile GetCurrentProfile() => _profiles.GetCurrentProfile();

        public Task<ProfileApplyResult> ApplyProfileAsync(Profile profile) => _apply.ApplyProfileAsync(profile, ApplySource.Tray);
    }
}
