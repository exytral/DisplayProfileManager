using System;
using System.Threading;
using System.Threading.Tasks;

using NLog;

using DisplayProfileManager.Common;
using DisplayProfileManager.Integration;

namespace DisplayProfileManager.Application
{
    internal sealed class SingleInstanceCoordinator : IDisposable
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        internal const string MutexName = "DPM_Mutex";
        internal const string ShowWindowEventName = "DPM_ShowWindow";
        internal const int IpcAuthorityReadyTimeoutMs = 10000;

        private Mutex _instanceMutex;
        private EventWaitHandle _showWindowEvent;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _ownsInstanceMutex;

        public IpcAuthorityKind Authority { get; private set; } = IpcAuthorityKind.Normal;
        public CancellationToken CancellationToken => EnsureCancellationSource().Token;

        public void ConfigureAuthority(bool devMode)
        {
            Authority = devMode ? IpcAuthorityKind.Dev : IpcAuthorityKind.Normal;
            EnsureCancellationSource();
        }

        public bool AcquireNormalAuthority(Func<Task> showWindowAsync)
        {
            _instanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                return false;
            }

            _ownsInstanceMutex = true;
            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShowWindowEventName);
                StartShowWindowListener(showWindowAsync);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting up show window event");
            }

            return true;
        }

        public void StartIpcListener(Func<string, Task> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            NamedPipeIpcTransport.StartListening(CancellationToken, handler, Authority);
        }

        public Task<bool> SendAsync(string message, IpcAuthorityKind authority, int readyTimeoutMs = 0) =>
            readyTimeoutMs > 0
                ? NamedPipeIpcTransport.SendAsync(message, authority, readyTimeoutMs)
                : NamedPipeIpcTransport.SendAsync(message, authority);

        public void BringExistingInstanceToFront() =>
            WindowActivator.BringExistingInstanceToFront("Display Profile Manager", ShowWindowEventName);

        private CancellationTokenSource EnsureCancellationSource() =>
            _cancellationTokenSource ??= new CancellationTokenSource();

        private void StartShowWindowListener(Func<Task> showWindowAsync)
        {
            if (showWindowAsync == null) throw new ArgumentNullException(nameof(showWindowAsync));

            CancellationToken cancellationToken = CancellationToken;
            EventWaitHandle showWindowEvent = _showWindowEvent;
            Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (showWindowEvent == null)
                            await Task.Delay(1000, cancellationToken);
                        else if (showWindowEvent.WaitOne(1000))
                        {
                            showWindowEvent.Reset();
                            await showWindowAsync();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in show window listener");
                }
            }, cancellationToken);
        }

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _showWindowEvent?.Dispose();
            }
            finally
            {
                try
                {
                    if (_ownsInstanceMutex)
                        _instanceMutex?.ReleaseMutex();
                    _instanceMutex?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error releasing single-instance authority");
                }
            }
        }
    }
}
