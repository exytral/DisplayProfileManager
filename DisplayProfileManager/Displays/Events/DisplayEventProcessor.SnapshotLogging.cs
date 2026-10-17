using System;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed partial class DisplayEventProcessor
    {
        private void LogSnapshot(
            string reason,
            long burstId,
            long firstSequence,
            long lastSequence,
            long started,
            long completed,
            DisplayEventSnapshot snapshot)
        {
            WriteJson(new
            {
                schemaVersion = 1,
                monitorSessionId = _monitorSessionId,
                evidenceMode = _mode.ToString(),
                recordKind = "ccd",
                sequence = lastSequence == 0 ? (long?)null : lastSequence,
                burstId,
                wallUtc = DateTime.UtcNow,
                monotonicTicks = completed,
                monotonicMsFromStart = _sessionWatch.Elapsed.TotalMilliseconds,
                processId = DisplayEventProcessInfo.ProcessId,
                windowsSessionId = DisplayEventProcessInfo.SessionId,
                appVersion = _appVersion,
                sampleReason = reason,
                triggerFirstSequence = firstSequence,
                triggerLastSequence = lastSequence,
                captureStartedMonotonic = started,
                captureCompletedMonotonic = completed,
                captureDurationMs = (completed - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                activeQueryStatus = snapshot.ActiveQueryStatus,
                allPathsQueryStatus = snapshot.AllPathsQueryStatus,
                topologyDigest = snapshot.TopologyDigest,
                activeDisplays = snapshot.ActiveDisplays,
                allPaths = snapshot.AllPaths
            });
        }
    }
}
