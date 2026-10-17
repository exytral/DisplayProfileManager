using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace DisplayProfileManager.UI.Presentation
{
    internal static class GenericFocusVisualPolicy
    {
        private static int _installed;

        internal static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0) return;

            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnPreviewGotKeyboardFocus),
                handledEventsToo: true);

            EventManager.RegisterClassHandler(
                typeof(FrameworkContentElement),
                Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnPreviewGotKeyboardFocus),
                handledEventsToo: true);
        }

        private static void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.NewFocus)) return;

            if (sender is FrameworkElement element)
                element.FocusVisualStyle = null;
            else if (sender is FrameworkContentElement content)
                content.FocusVisualStyle = null;
        }
    }
}
