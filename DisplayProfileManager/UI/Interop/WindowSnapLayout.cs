using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DisplayProfileManager.UI.Interop
{
    internal sealed class WindowSnapLayout : IDisposable
    {
        private readonly Window _window;
        private readonly Action _refreshSystemTheme;
        private readonly HwndSource _source;
        private readonly DispatcherTimer _timer;
        private bool _hoveringMaxButton;
        private DateTime _hoverStartTime;

        public WindowSnapLayout(Window window, Action refreshSystemTheme)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _refreshSystemTheme = refreshSystemTheme;
            _source = (HwndSource)PresentationSource.FromVisual(window);
            _source?.AddHook(WndProc);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += OnTimerTick;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _timer.Stop();
            if (!_hoveringMaxButton) return;

            GetCursorPos(out var pos);
            SetCursorPos(pos.X, pos.Y);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0320)
                _refreshSystemTheme?.Invoke();

            const int WmNcHitTest = 0x0084;
            const int WmMouseLeave = 0x02A3;
            const int HtMaxButton = 9;

            if (msg == WmNcHitTest)
            {
                var point = new Point
                {
                    X = (short)((int)lParam & 0xFFFF),
                    Y = (short)(((int)lParam >> 16) & 0xFFFF)
                };
                ScreenToClient(hwnd, ref point);

                var buttonRect = GetMaximizeButtonRect();
                if (PtInRect(ref buttonRect, point))
                {
                    if (!_hoveringMaxButton)
                    {
                        _hoveringMaxButton = true;
                        _hoverStartTime = DateTime.Now;
                        _timer.Start();
                    }
                    else if ((DateTime.Now - _hoverStartTime).TotalMilliseconds >= 150)
                    {
                        handled = true;
                        return new IntPtr(HtMaxButton);
                    }
                }
                else if (_hoveringMaxButton)
                {
                    _hoveringMaxButton = false;
                    _timer.Stop();
                }
            }
            else if (msg == WmMouseLeave)
            {
                _hoveringMaxButton = false;
                _timer.Stop();
            }

            return IntPtr.Zero;
        }

        private Rect GetMaximizeButtonRect()
        {
            int windowWidth = (int)_window.ActualWidth;
            const int buttonWidth = 46;
            const int titleBarHeight = 32;

            return new Rect
            {
                left = windowWidth - (buttonWidth * 2),
                top = 0,
                right = windowWidth - buttonWidth,
                bottom = titleBarHeight
            };
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        private struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PtInRect([In] ref Rect lprc, Point pt);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
    }
}
