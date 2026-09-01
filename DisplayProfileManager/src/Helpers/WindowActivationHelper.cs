using NLog;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DisplayProfileManager.Helpers
{
    public static class WindowActivationHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        #endregion

        #region Constants

        private const int SW_RESTORE = 9;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr _hwndTop = IntPtr.Zero;

        #endregion

        #region Public Methods

        public static void BringExistingInstanceToFront(string windowTitle, string showWindowEventName)
        {
            try
            {
                IntPtr hWnd = FindWindow(null, windowTitle);
                if (hWnd != IntPtr.Zero)
                    ActivateWindow(hWnd);

                try
                {
                    Thread.Sleep(100);
                    using (var showEvent = EventWaitHandle.OpenExisting(showWindowEventName))
                        showEvent.Set();
                }
                catch (Exception eventEx)
                {
                    logger.Error(eventEx, "Error signaling show window event");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error bringing existing instance to front");
            }
        }

        public static void ActivateWindow(IntPtr hWnd)
        {
            try
            {
                uint currentThreadId = GetCurrentThreadId();
                uint windowThreadId = GetWindowThreadProcessId(hWnd, out _);

                bool attached = false;
                if (currentThreadId != windowThreadId)
                {
                    attached = AttachThreadInput(currentThreadId, windowThreadId, true);
                }

                try
                {
                    if (IsIconic(hWnd))
                        ShowWindow(hWnd, SW_RESTORE);

                    SetWindowPos(hWnd, _hwndTop, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(hWnd);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(currentThreadId, windowThreadId, false);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error activating window");
            }
        }

        #endregion
    }
}