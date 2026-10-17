using System;
using System.Windows;

using DisplayProfileManager.Settings;

namespace DisplayProfileManager.UI.Windows
{
    public partial class CloseConfirmationDialog : Window
    {
        private readonly SettingsService _settingsService;

        public bool ShouldCloseToTray { get; private set; }
        public bool RememberChoice { get; private set; }

        internal CloseConfirmationDialog(SettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            InitializeComponent();

            if (_settingsService.ShouldCloseToTray())
            {
                MinimizeToTrayRadioButton.IsChecked = true;
                CloseApplicationRadioButton.IsChecked = false;
            }
            else
            {
                MinimizeToTrayRadioButton.IsChecked = false;
                CloseApplicationRadioButton.IsChecked = true;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldCloseToTray = MinimizeToTrayRadioButton.IsChecked == true;
            RememberChoice = RememberChoiceCheckBox.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
