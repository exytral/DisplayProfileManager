using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace DisplayProfileManager.UI.Windows
{
    public static class TitleBarLayout
    {
        private const double NormalHeight = 32;
        private const double MaximizedHeight = 40;

        public static void UpdateMargin(Window window, Grid titleBarGrid, RowDefinition titleBarRow)
        {
            // Maximized window loses its border, so caption needs inset and extra height
            if (titleBarGrid == null) return;

            bool maximized = window.WindowState == WindowState.Maximized;

            titleBarGrid.Margin = maximized ? new Thickness(8, 8, 6, 0) : new Thickness(0);
            UpdateHeight(window, titleBarRow, maximized ? MaximizedHeight : NormalHeight);
        }

        public static void UpdateHeight(Window window, RowDefinition titleBarRow, double height)
        {
            // CaptionHeight has to track row or draggable area stops matching what is drawn
            titleBarRow?.Height = new GridLength(height);

            var chrome = WindowChrome.GetWindowChrome(window);
            chrome?.CaptionHeight = height;
        }
    }
}
