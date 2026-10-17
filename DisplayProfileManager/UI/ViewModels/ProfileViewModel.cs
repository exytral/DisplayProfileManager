using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Hotkeys.Models;
using DisplayProfileManager.Profiles.Models;

namespace DisplayProfileManager.UI.ViewModels
{
    public class ProfileViewModel(Profile profile) : INotifyPropertyChanged
    {
        private readonly Profile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        private bool _isDefault;
        private bool _isActive;

        public Profile Profile => _profile;

        public string Id => _profile.Id;
        public string Name => _profile.Name;
        public string Description => _profile.Description;
        public string Icon => _profile.Icon;
        public DateTime CreatedDate => _profile.CreatedDate;
        public DateTime LastModifiedDate => _profile.LastModifiedDate;
        public System.Collections.Generic.List<DisplaySetting> DisplaySettings => _profile.DisplaySettings;
        public int EnabledDisplayCount => _profile.DisplaySettings.Count(setting => setting.IsEnabled);
        public int TotalDisplayCount => _profile.DisplaySettings.Count;
        public string DisplayCountText => $"{EnabledDisplayCount}/{TotalDisplayCount} {(TotalDisplayCount == 1 ? "display" : "displays")}";
        public HotkeyConfig HotkeyConfig => _profile.HotkeyConfig;

        public bool IsDefault
        {
            get => _isDefault;
            set
            {
                if (_isDefault != value)
                {
                    _isDefault = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
