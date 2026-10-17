using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed partial class DisplayEventProcessor : IDisposable
    {
        internal static readonly int[] TailOffsetsMs = [100, 250, 500, 1000, 2000, 5000, 10000];

        private readonly Channel<QueuedEvidenceItem> _channel;
        private readonly Func<DisplayEventSnapshot> _snapshotFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly Action<string> _logSink;
        private readonly Action<string> _ordinaryLogSink;
        private readonly Func<long> _timestampProvider;
        private readonly long _timestampFrequency;
        private readonly DisplayEventEvidenceMode _mode;
        private readonly bool _traceEnabled;
        private readonly Func<CancellationToken, Task> _beforeReadAsync;
        private readonly SemaphoreSlim _sampleGate = new(1, 1);
        private readonly object _burstLock = new();
        private readonly object _ownedWorkLock = new();
        private readonly HashSet<Task> _ownedWork = [];

        private CancellationTokenSource _lifetime;
        private CancellationTokenSource _tail;
        private CancellationTokenSource _applyScope;
        private Task _readerTask;
        private long _lastImmediateTicks;
        private long _burstId;
        private long _burstGeneration;
        private long _applyScopeGeneration;
        private long _burstFirstSequence;
        private int _applyScopeDepth;
        private bool _applyScopeActive;
        private TaskCompletionSource<bool> _pendingApplyBarrier;
        private bool _burstActive;
        private bool _stopping;
        private bool _disposed;

        internal DisplayEventProcessor(
            Func<DisplayEventSnapshot> snapshotFactory,
            Func<TimeSpan, CancellationToken, Task> delayAsync = null,
            Action<string> logSink = null,
            Func<long> timestampProvider = null,
            long? timestampFrequency = null,
            DisplayEventEvidenceMode mode = DisplayEventEvidenceMode.AppLifetime,
            Func<CancellationToken, Task> beforeReadAsync = null,
            Action<string> ordinaryLogSink = null,
            bool traceEnabled = false)
        {
            _snapshotFactory = snapshotFactory ?? throw new ArgumentNullException(nameof(snapshotFactory));
            _delayAsync = delayAsync ?? Task.Delay;
            _logSink = logSink ?? DefaultLog;
            _ordinaryLogSink = ordinaryLogSink ?? DefaultOrdinaryLog;
            _timestampProvider = timestampProvider ?? System.Diagnostics.Stopwatch.GetTimestamp;
            _timestampFrequency = timestampFrequency ?? System.Diagnostics.Stopwatch.Frequency;
            _mode = mode;
            _traceEnabled = traceEnabled;
            _beforeReadAsync = beforeReadAsync;
            if (_timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));

            _channel = Channel.CreateUnbounded<QueuedEvidenceItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        internal void Start()
        {
            if (_readerTask != null) return;

            _lifetime = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoopAsync(_lifetime.Token));
            LogLifecycle("processorStarted", true);
        }

        internal bool TryWrite(DisplayRawEvent rawEvent)
        {
            if (rawEvent == null || _stopping)
            {
                return false;
            }

            if (_mode == DisplayEventEvidenceMode.AppLifetime)
            {
                return _channel.Writer.TryWrite(QueuedEvidenceItem.Raw(rawEvent, 0));
            }

            bool scopedAccepted = false;
            lock (_burstLock)
            {
                if (!_stopping && _applyScopeActive)
                {
                    scopedAccepted = _channel.Writer.TryWrite(QueuedEvidenceItem.Raw(rawEvent, _applyScopeGeneration));
                }
            }

            return scopedAccepted;
        }

        internal void RequestBaselineSample()
        {
            var lifetime = _lifetime;
            if (_mode != DisplayEventEvidenceMode.AppLifetime || _stopping || lifetime == null || lifetime.IsCancellationRequested)
            {
                return;
            }

            TrackOwned(CaptureAsync("baseline", 0, 0, 0, lifetime.Token));
        }

        internal async Task<IAsyncDisposable> BeginApplyEvidenceScopeAsync()
        {
            if (_stopping || _lifetime == null || _lifetime.IsCancellationRequested)
            {
                return EvidenceLease.Empty;
            }

            if (_mode == DisplayEventEvidenceMode.AppLifetime)
            {
                LogLifecycle("applyScopeStarted", true);
                LogScopeHealth("armed");
                return new EvidenceLease(this, appLifetime: true);
            }

            CancellationToken scopeToken;
            lock (_burstLock)
            {
                if (_applyScopeActive)
                {
                    _applyScopeDepth++;
                    return new EvidenceLease(this, appLifetime: false);
                }

                _applyScopeActive = true;
                _applyScopeDepth = 1;
                _applyScopeGeneration++;
                _lastImmediateTicks = 0;
                _burstActive = false;
                _burstGeneration++;
                _applyScope?.Dispose();
                _applyScope = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                scopeToken = _applyScope.Token;
            }

            LogLifecycle("applyScopeStarted", true);
            LogScopeHealth("armed");
            await CaptureAsync("applyStart", 0, 0, 0, scopeToken).ConfigureAwait(false);
            return new EvidenceLease(this, appLifetime: false);
        }

        private async Task EndApplyEvidenceScopeAsync(bool appLifetime)
        {
            if (appLifetime)
            {
                LogLifecycle("applyScopeCompleted", true);
                LogScopeHealth("completed");
                return;
            }

            Task barrierTask;
            CancellationTokenSource scopeToDispose = null;
            lock (_burstLock)
            {
                if (!_applyScopeActive) return;

                _applyScopeDepth--;
                if (_applyScopeDepth > 0) return;

                _applyScopeActive = false;

                var barrier = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingApplyBarrier = barrier;
                if (!_channel.Writer.TryWrite(QueuedEvidenceItem.Barrier(_applyScopeGeneration, barrier)))
                {
                    barrier.TrySetCanceled();
                }

                barrierTask = barrier.Task;
            }

            try
            {
                bool barrierDrained = false;
                try
                {
                    await barrierTask.ConfigureAwait(false);
                    barrierDrained = true;
                }
                catch (OperationCanceledException) { }

                lock (_burstLock)
                {
                    _pendingApplyBarrier = null;
                    _burstActive = false;
                    _burstGeneration++;
                    _tail?.Cancel();
                    _tail?.Dispose();
                    _tail = null;
                    scopeToDispose = _applyScope;
                    _applyScope = null;
                    scopeToDispose?.Cancel();
                }

                if (!barrierDrained && !_stopping)
                    LogLifecycle("applyScopeDrainFailed", false);

                await WaitForOwnedWorkAsync().ConfigureAwait(false);

                var lifetime = _lifetime;
                if (!_stopping && lifetime != null && !lifetime.IsCancellationRequested)
                    await CaptureAsync("applyCompletion", 0, 0, 0, lifetime.Token).ConfigureAwait(false);

                LogLifecycle("applyScopeCompleted", true);
                LogScopeHealth("completed");
            }
            catch (Exception ex)
            {
                LogLifecycle("applyScopeCloseFailed", false, detail: ex.ToString());
            }
            finally
            {
                scopeToDispose?.Dispose();
            }
        }

        private void TrackOwned(Task task)
        {
            lock (_ownedWorkLock)
                _ownedWork.Add(task);

            _ = task.ContinueWith(
                completed =>
                {
                    lock (_ownedWorkLock)
                        _ownedWork.Remove(completed);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task WaitForOwnedWorkAsync()
        {
            while (true)
            {
                Task[] pending;
                lock (_ownedWorkLock)
                {
                    if (_ownedWork.Count == 0) return;

                    pending = new Task[_ownedWork.Count];
                    _ownedWork.CopyTo(pending);
                }

                try
                {
                    await Task.WhenAll(pending).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }

        private void WaitForOwnedWork()
        {
            while (true)
            {
                Task[] pending;
                lock (_ownedWorkLock)
                {
                    if (_ownedWork.Count == 0) return;

                    pending = new Task[_ownedWork.Count];
                    _ownedWork.CopyTo(pending);
                }

                try
                {
                    Task.WaitAll(pending);
                }
                catch (AggregateException ex)
                {
                    ex.Handle(inner => inner is OperationCanceledException);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopping = true;

            _channel.Writer.TryComplete();
            lock (_burstLock)
            {
                _applyScopeActive = false;
                _pendingApplyBarrier?.TrySetCanceled();
                _tail?.Cancel();
                _applyScope?.Cancel();
            }
            _lifetime?.Cancel();

            try
            {
                _readerTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }

            WaitForOwnedWork();

            LogLifecycle("processorStopped", true);
            _tail?.Dispose();
            _applyScope?.Dispose();
            _lifetime?.Dispose();
            _sampleGate.Dispose();
        }

        private sealed class QueuedEvidenceItem
        {
            private QueuedEvidenceItem(
                DisplayRawEvent rawEvent,
                long scopeGeneration,
                TaskCompletionSource<bool> barrierCompletion)
            {
                RawEvent = rawEvent;
                ScopeGeneration = scopeGeneration;
                BarrierCompletion = barrierCompletion;
            }

            internal DisplayRawEvent RawEvent { get; }
            internal long ScopeGeneration { get; }
            internal TaskCompletionSource<bool> BarrierCompletion { get; }

            internal static QueuedEvidenceItem Raw(DisplayRawEvent rawEvent, long scopeGeneration) => new(rawEvent, scopeGeneration, null);

            internal static QueuedEvidenceItem Barrier(long scopeGeneration, TaskCompletionSource<bool> completion) => new(null, scopeGeneration, completion);
        }

        private sealed class EvidenceLease : IAsyncDisposable
        {
            internal static readonly EvidenceLease Empty = new(null, appLifetime: false);

            private DisplayEventProcessor _owner;
            private readonly bool _appLifetime;

            internal EvidenceLease(DisplayEventProcessor owner, bool appLifetime)
            {
                _owner = owner;
                _appLifetime = appLifetime;
            }

            public ValueTask DisposeAsync()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                return owner == null
                    ? ValueTask.CompletedTask
                    : new ValueTask(owner.EndApplyEvidenceScopeAsync(_appLifetime));
            }
        }
    }
}
