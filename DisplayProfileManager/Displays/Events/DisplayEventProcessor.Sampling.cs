using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed partial class DisplayEventProcessor
    {
        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    if (_beforeReadAsync != null)
                        await _beforeReadAsync(cancellationToken).ConfigureAwait(false);

                    if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                        break;

                    while (_channel.Reader.TryRead(out var item))
                    {
                        if (item.BarrierCompletion != null)
                        {
                            item.BarrierCompletion.TrySetResult(true);
                            continue;
                        }

                        ProcessRaw(item.RawEvent, item.ScopeGeneration);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogLifecycle("processorFault", false, detail: ex.ToString());
            }
        }

        private void ProcessRaw(DisplayRawEvent rawEvent, long scopeGeneration)
        {
            lock (_burstLock)
            {
                if (_mode == DisplayEventEvidenceMode.ApplyScoped && (scopeGeneration <= 0 || scopeGeneration != _applyScopeGeneration)) return;

                if (!_burstActive)
                {
                    _burstActive = true;
                    _burstId++;
                    _burstFirstSequence = rawEvent.Sequence;
                }

                long burstId = _burstId;
                long firstSequence = _burstFirstSequence;
                long generation = ++_burstGeneration;
                bool immediate = ShouldTakeImmediateSample(_lastImmediateTicks, rawEvent.MonotonicTicks, Stopwatch.Frequency);

                if (immediate)
                    _lastImmediateTicks = rawEvent.MonotonicTicks;

                CancellationToken evidenceToken =
                    _mode == DisplayEventEvidenceMode.ApplyScoped
                        ? _applyScope.Token
                        : _lifetime.Token;

                _tail?.Cancel();
                _tail?.Dispose();
                _tail = CancellationTokenSource.CreateLinkedTokenSource(evidenceToken);
                CancellationToken tailToken = _tail.Token;

                LogRaw(rawEvent, burstId);
                if (_traceEnabled)
                    LogOrdinaryRaw(rawEvent, scopeGeneration > 0 ? scopeGeneration : null);

                if (immediate && !evidenceToken.IsCancellationRequested)
                {
                    TrackOwned(CaptureAsync(
                        "event",
                        burstId,
                        firstSequence,
                        rawEvent.Sequence,
                        evidenceToken));
                }

                if (!tailToken.IsCancellationRequested)
                {
                    TrackOwned(RunTailAsync(
                        burstId,
                        generation,
                        firstSequence,
                        rawEvent.Sequence,
                        rawEvent.MonotonicTicks,
                        tailToken));
                }
            }
        }

        private async Task RunTailAsync(
            long burstId,
            long generation,
            long firstSequence,
            long lastSequence,
            long tailEpochTicks,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (int offset in TailOffsetsMs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    TimeSpan delay = GetTailDelay(
                        tailEpochTicks,
                        offset,
                        _timestampProvider(),
                        _timestampFrequency);
                    if (delay > TimeSpan.Zero)
                        await _delayAsync(delay, cancellationToken).ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();
                    await CaptureAsync(
                        $"tail+{offset}",
                        burstId,
                        firstSequence,
                        lastSequence,
                        cancellationToken).ConfigureAwait(false);
                }

                lock (_burstLock)
                {
                    if (_burstGeneration == generation)
                        _burstActive = false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }

        private async Task CaptureAsync(
            string reason,
            long burstId,
            long firstSequence,
            long lastSequence,
            CancellationToken cancellationToken)
        {
            await _sampleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            long started = Stopwatch.GetTimestamp();
            try
            {
                var snapshot = await Task.Run(_snapshotFactory, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                long completed = Stopwatch.GetTimestamp();
                LogSnapshot(reason, burstId, firstSequence, lastSequence, started, completed, snapshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogLifecycle("ccdSampleFailed", false, detail: ex.ToString());
            }
            finally
            {
                _sampleGate.Release();
            }
        }

        internal static TimeSpan GetTailDelay(
            long tailEpochTicks,
            int offsetMs,
            long currentTicks,
            long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));

            long targetTicks = tailEpochTicks + (long)Math.Round(offsetMs * (double)frequency / 1000.0, MidpointRounding.AwayFromZero);
            long remainingTicks = targetTicks - currentTicks;
            return remainingTicks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(remainingTicks / (double)frequency);
        }

        internal static bool ShouldTakeImmediateSample(
            long previousTicks,
            long currentTicks,
            long frequency) =>
            previousTicks == 0 || currentTicks - previousTicks >= frequency / 4;
    }
}
