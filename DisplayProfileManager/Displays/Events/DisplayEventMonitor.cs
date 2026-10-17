using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;

using DisplayProfileManager.Displays.Interop;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed class DisplayEventMonitor : IDisposable, IDisplayEventEvidenceScopeProvider
    {
        private readonly DisplayEventProcessor _processor;
        private readonly TaskCompletionSource<bool> _ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Thread _thread;
        private Dispatcher _dispatcher;
        private HwndSource _source;
        private DisplayEventRegistrations _registrations;
        private long _sequence;
        private bool _started;
        private bool _disposed;

        internal DisplayEventMonitor(
            Func<DisplayEventSnapshot> snapshotFactory,
            DisplayEventEvidenceMode mode = DisplayEventEvidenceMode.AppLifetime,
            bool traceEnabled = false)
        {
            if (mode == DisplayEventEvidenceMode.Disabled) throw new ArgumentOutOfRangeException(nameof(mode));

            _processor = new DisplayEventProcessor(snapshotFactory, mode: mode, traceEnabled: traceEnabled);
        }

        internal async Task StartAsync()
        {
            if (_started || _disposed) return;

            _started = true;
            _processor.Start();

            _thread = new Thread(ReceiverMain)
            {
                IsBackground = true,
                Name = "DPM Display Event Monitor"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (await _ready.Task.ConfigureAwait(false))
                _processor.RequestBaselineSample();
        }

        public Task<IAsyncDisposable> BeginApplyEvidenceScopeAsync() =>
            _processor.BeginApplyEvidenceScopeAsync();

        private void ReceiverMain()
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                var parameters = new HwndSourceParameters("DPM_DisplayEventMonitor")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = -32000,
                    PositionY = -32000,
                    WindowStyle = 0
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);
                _processor.LogLifecycle("receiverCreated", true);

                _registrations = new DisplayEventRegistrations(_source.Handle);
                _processor.LogLifecycle(
                    "monitorInterfaceRegistration",
                    _registrations.DeviceHandle != IntPtr.Zero,
                    _registrations.DeviceError == 0 ? null : _registrations.DeviceError);
                _processor.LogLifecycle(
                    "sessionDisplayPowerRegistration",
                    _registrations.PowerHandle != IntPtr.Zero,
                    _registrations.PowerError == 0 ? null : _registrations.PowerError);

                _ready.TrySetResult(true);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _processor.LogLifecycle("receiverFailed", false, detail: ex.ToString());
                _ready.TrySetResult(false);
            }
            finally
            {
                _registrations?.Dispose();
                if (_source != null)
                {
                    _source.RemoveHook(WndProc);
                    _source.Dispose();
                    _source = null;
                }

                _processor.LogLifecycle("receiverStopped", true);
            }
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            handled = false;
            if (!IsSupportedMessage(message))
            {
                return IntPtr.Zero;
            }

            long sequence = Interlocked.Increment(ref _sequence);
            var rawEvent = DisplayEventParser.Parse(
                sequence,
                DateTime.UtcNow,
                Stopwatch.GetTimestamp(),
                message,
                wParam,
                lParam);

            _processor.TryWrite(rawEvent);
            return IntPtr.Zero;
        }

        internal static bool IsSupportedMessage(int message) =>
            message == DisplayEventNative.WmDisplayChange ||
            message == DisplayEventNative.WmDeviceChange ||
            message == DisplayEventNative.WmPowerBroadcast;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_dispatcher != null && !_dispatcher.HasShutdownStarted)
                _dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);

            if (_thread != null && _thread.IsAlive &&
                !_thread.Join(TimeSpan.FromSeconds(2)))
            {
                _processor.LogLifecycle(
                    "receiverShutdownDelayed",
                    false,
                    detail: "Receiver thread did not terminate within two seconds; waiting for confirmed termination.");
                _thread.Join();
            }

            _processor.Dispose();
        }
    }
}
