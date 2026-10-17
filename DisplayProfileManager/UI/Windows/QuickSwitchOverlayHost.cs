using System;

namespace DisplayProfileManager.UI.Windows
{
    internal interface IQuickSwitchOverlay
    {
        event EventHandler Closed;
        void RefreshAndActivate();
        void Close();
    }

    internal sealed class QuickSwitchOverlayHost
    {
        private readonly Func<IQuickSwitchOverlay> _factory;
        private IQuickSwitchOverlay _current;

        internal bool HasOpenOverlay => _current != null;

        internal QuickSwitchOverlayHost(Func<IQuickSwitchOverlay> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal void OpenOrActivate()
        {
            if (_current == null)
            {
                _current = _factory() ?? throw new InvalidOperationException("Quick Switch overlay factory returned null");
                _current.Closed += OnOverlayClosed;
            }

            _current.RefreshAndActivate();
        }

        internal void CloseCurrent()
        {
            _current?.Close();
        }

        private void OnOverlayClosed(object sender, EventArgs e)
        {
            _current?.Closed -= OnOverlayClosed;

            _current = null;
        }
    }
}
