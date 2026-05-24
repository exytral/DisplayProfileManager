using DisplayProfileManager.Core;
using DisplayProfileManager.Helpers;
using NLog;
using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace DisplayProfileManager.UI
{
    public class TrayIcon : IDisposable
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ProfileManager _profileManager;
        private bool _disposed = false;
        private Icon _defaultIcon;

        public event EventHandler ShowMainWindow;
        public event EventHandler ShowSettingsWindow;
        public event EventHandler ExitApplication;

        public TrayIcon()
        {
            _profileManager = ProfileManager.Instance;
            SetupEventHandlers();
            InitializeTrayIcon();

        }

        private void SetupEventHandlers()
        {
            _profileManager.ProfileAdded += OnProfileChanged;
            _profileManager.ProfileUpdated += OnProfileChanged;
            _profileManager.ProfileDeleted += OnProfileDeleted;
            _profileManager.ProfilesLoaded += OnProfilesLoaded;
            _profileManager.ProfileApplied += OnProfileApplied;
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = CreateTrayIcon();
            _defaultIcon = _notifyIcon.Icon;
            _notifyIcon.Text = "Display Profile Manager";
            _notifyIcon.Visible = true;

            _contextMenu = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip = _contextMenu;

            _notifyIcon.DoubleClick += OnTrayIconDoubleClick;

            BuildContextMenu();
            UpdateTrayIconTooltip();
        }

        private Icon CreateTrayIcon()
        {
            try
            {
                var icon = Properties.Resources.AppIcon;
                if (icon != null)
                {
                    return icon;
                }
                else
                {
                    return SystemIcons.Application;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to load icon from resources");
                return SystemIcons.Application;
            }
        }

        private void UpdateTrayIconTooltip()
        {
            var currentProfile = _profileManager.GetCurrentProfile();
            if (currentProfile != null)
            {
                string prefix = "Display Profile Manager - ";
                string currentProfileName = currentProfile.Name;
                string fullTooltip = $"{prefix}{currentProfileName}";

                if (fullTooltip.Length >= 64)
                {
                    int availableSpace = 63 - prefix.Length - 3;
                    if (availableSpace > 0)
                    {
                        fullTooltip = $"{prefix}{currentProfileName.Substring(0, availableSpace)}...";
                    }
                    else
                    {
                        fullTooltip = fullTooltip.Substring(0, 60) + "...";
                    }
                }

                _notifyIcon.Text = fullTooltip;
            }
        }

        private void UpdateTrayIcon(Profile profile)
        {
            var icon = IconHelper.LoadIcon(profile?.Icon);
            _notifyIcon.Icon = icon ?? _defaultIcon;
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            var profiles = _profileManager.GetAllProfiles();

            if (profiles.Count > 0)
            {
                var profilesMenuItem = new ToolStripMenuItem("Profiles");

                foreach (var profile in profiles.OrderBy(p => p.Name))
                {
                    var profileDisplayName = profile.Name;

                    if (profile.HotkeyConfig?.IsEnabled == true &&
                        profile.HotkeyConfig.Key != System.Windows.Input.Key.None)
                    {
                        profileDisplayName += $" ({profile.HotkeyConfig})";
                    }

                    var profileItem = new ToolStripMenuItem(profileDisplayName);
                    profileItem.Tag = profile;
                    profileItem.Click += OnProfileMenuItemClick;

                    if (profile.Id == _profileManager.CurrentProfileId)
                    {
                        profileItem.Checked = true;
                    }

                    profilesMenuItem.DropDownItems.Add(profileItem);
                }

                _contextMenu.Items.Add(profilesMenuItem);
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            var manageProfilesItem = new ToolStripMenuItem("Manage Profiles...");
            manageProfilesItem.Click += OnManageProfilesClick;
            _contextMenu.Items.Add(manageProfilesItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += OnSettingsClick;
            _contextMenu.Items.Add(settingsItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += OnExitClick;
            _contextMenu.Items.Add(exitItem);
        }

        private async void OnProfileMenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem && menuItem.Tag is Profile profile)
            {
                try
                {
                    logger.Info($"Applying profile '{profile.Name}' via TrayIcon");

                    var applyResult = await _profileManager.ApplyProfileAsync(profile);

                    if (applyResult.Success)
                    {
                        string message = $"Profile '{profile.Name}' successfully applied.";
                        logger.Info(message);

                        _notifyIcon.ShowBalloonTip(3000, "Display Profile Manager", message, ToolTipIcon.Info);
                    }
                    else
                    {
                        string errorDetails = _profileManager.GetApplyResultErrorMessage(profile.Name, applyResult);
                        logger.Warn(errorDetails);

                        _notifyIcon.ShowBalloonTip(5000, "Display Profile Manager", errorDetails, ToolTipIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error applying profile via tray");
                    try
                    {
                        _notifyIcon?.ShowBalloonTip(5000, "Display Profile Manager",
                            $"Error applying profile: {ex.Message}", ToolTipIcon.Error);
                    }
                    catch { }
                }
            }
        }

        private void OnTrayIconDoubleClick(object sender, EventArgs e)
        {
            ShowMainWindow?.Invoke(this, EventArgs.Empty);
        }

        private void OnManageProfilesClick(object sender, EventArgs e)
        {
            ShowMainWindow?.Invoke(this, EventArgs.Empty);
        }

        private void OnSettingsClick(object sender, EventArgs e)
        {
            ShowSettingsWindow?.Invoke(this, EventArgs.Empty);
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            ExitApplication?.Invoke(this, EventArgs.Empty);
        }

        private void OnProfileChanged(object sender, Profile profile)
        {
            BuildContextMenu();
        }

        private void OnProfileDeleted(object sender, string profileId)
        {
            BuildContextMenu();
        }

        private void OnProfilesLoaded(object sender, EventArgs e)
        {
            BuildContextMenu();
        }

        private void OnProfileApplied(object sender, Profile e)
        {
            BuildContextMenu();
            UpdateTrayIconTooltip();
            UpdateTrayIcon(e);
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.None, int timeout = 3000)
        {
            _notifyIcon.ShowBalloonTip(timeout, title, message, icon);
        }

        public void UpdateTooltip(string text)
        {
            _notifyIcon.Text = text;
        }

        public void Hide()
        {
            _notifyIcon.Visible = false;
        }

        public void Show()
        {
            _notifyIcon.Visible = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_profileManager != null)
                    {
                        _profileManager.ProfileAdded -= OnProfileChanged;
                        _profileManager.ProfileUpdated -= OnProfileChanged;
                        _profileManager.ProfileDeleted -= OnProfileDeleted;
                        _profileManager.ProfilesLoaded -= OnProfilesLoaded;
                        _profileManager.ProfileApplied -= OnProfileApplied;
                    }

                    _contextMenu?.Dispose();
                    _notifyIcon?.Dispose();
                }

                _disposed = true;
            }
        }

        ~TrayIcon()
        {
            Dispose(false);
        }
    }
}