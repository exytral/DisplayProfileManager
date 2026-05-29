using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DisplayProfileManager.UI.Windows
{
    public partial class MonitorIdentifyWindow : Window
    {
        private DispatcherTimer _closeTimer;
        private double _targetLeft;
        private double _targetTop;

        public int MonitorIndex { get; private set; }

        #region P/Invoke

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SwpNosize = 0x0001;
        private const uint SwpNozorder = 0x0004;
        private const uint SwpNoactivate = 0x0010;

        #endregion

        public MonitorIdentifyWindow(int monitorIndex, double left, double top)
        {
            InitializeComponent();

            MonitorIndex = monitorIndex;
            IndexTextBlock.Text = monitorIndex.ToString();

            _targetLeft = left;
            _targetTop = top;
            this.Left = left;
            this.Top = top;

            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromSeconds(3);
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                this.Close();
            };

            this.Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            SetWindowPos(hwnd, IntPtr.Zero, (int)_targetLeft, (int)_targetTop, 0, 0, SwpNosize | SwpNozorder | SwpNoactivate);

            _closeTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closeTimer?.Stop();
            base.OnClosed(e);
        }
    }
}