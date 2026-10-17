using System;
using System.Diagnostics;
using System.Reflection;

using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Displays.Events
{
    internal sealed partial class DisplayEventProcessor
    {
        internal const string EvidenceLoggerName = "DisplayProfileManager.DisplayEventEvidence";

        private static readonly Logger _logger = LoggerHelper.GetLogger();
        private static readonly Logger _evidenceLogger = LogManager.GetLogger(EvidenceLoggerName);
        private readonly Stopwatch _sessionWatch = Stopwatch.StartNew();
        private readonly string _monitorSessionId = Guid.NewGuid().ToString("N");
        private readonly string _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        private static void DefaultLog(string message) => _evidenceLogger.Info(message);
        private static void DefaultOrdinaryLog(string message) => _logger.Debug(message);

        internal void LogLifecycle(
            string action,
            bool success,
            int? error = null,
            string detail = null)
        {
            WriteJson(new
            {
                schemaVersion = 1,
                monitorSessionId = _monitorSessionId,
                evidenceMode = _mode.ToString(),
                recordKind = "lifecycle",
                sequence = (long?)null,
                burstId = 0,
                wallUtc = DateTime.UtcNow,
                monotonicTicks = Stopwatch.GetTimestamp(),
                monotonicMsFromStart = _sessionWatch.Elapsed.TotalMilliseconds,
                processId = DisplayEventProcessInfo.ProcessId,
                windowsSessionId = DisplayEventProcessInfo.SessionId,
                appVersion = _appVersion,
                action,
                success,
                error,
                detail
            });

            if (!success)
            {
                string errorText = error.HasValue ? $" (error {error.Value})" : "";
                _logger.Warn($"Display event evidence {action} failed{errorText}");
            }
        }

        private void LogScopeHealth(string state) => _logger.Info($"Display event apply evidence {state} ({_mode})");

        private void LogRaw(DisplayRawEvent rawEvent, long burstId)
        {
            WriteJson(new
            {
                schemaVersion = 1,
                monitorSessionId = _monitorSessionId,
                evidenceMode = _mode.ToString(),
                recordKind = "raw",
                sequence = rawEvent.Sequence,
                burstId,
                wallUtc = rawEvent.WallUtc,
                monotonicTicks = rawEvent.MonotonicTicks,
                monotonicMsFromStart = _sessionWatch.Elapsed.TotalMilliseconds,
                processId = DisplayEventProcessInfo.ProcessId,
                windowsSessionId = DisplayEventProcessInfo.SessionId,
                appVersion = _appVersion,
                message = rawEvent.Message,
                messageId = rawEvent.MessageId,
                subtypeName = rawEvent.SubtypeName,
                wParam = rawEvent.WParam,
                displayChange = rawEvent.BitsPerPixel.HasValue
                    ? new { bitsPerPixel = rawEvent.BitsPerPixel, width = rawEvent.Width, height = rawEvent.Height }
                    : null,
                device = rawEvent.DeviceType.HasValue
                    ? new { devType = rawEvent.DeviceType, classGuid = rawEvent.DeviceClassGuid, interfacePath = rawEvent.DeviceInterfacePath }
                    : null,
                power = rawEvent.PowerSettingGuid.HasValue
                    ? new { settingGuid = rawEvent.PowerSettingGuid, stateName = rawEvent.PowerStateName, stateValue = rawEvent.PowerStateValue }
                    : null
            });

        }

        private void LogOrdinaryRaw(DisplayRawEvent rawEvent, long? scopeGeneration)
        {
            string subtype = string.IsNullOrEmpty(rawEvent.SubtypeName) ? "-" : rawEvent.SubtypeName;
            string detail = rawEvent.BitsPerPixel.HasValue
                ? $" bpp={rawEvent.BitsPerPixel} size={rawEvent.Width}x{rawEvent.Height}"
                : rawEvent.DeviceType.HasValue
                    ? $" deviceType={rawEvent.DeviceType} interface={rawEvent.DeviceInterfacePath ?? "-"}"
                    : rawEvent.PowerSettingGuid.HasValue
                        ? $" power={rawEvent.PowerStateName ?? rawEvent.PowerStateValue?.ToString() ?? "-"}"
                        : "";
            string generation = scopeGeneration.HasValue
                ? $" generation={scopeGeneration.Value}"
                : "";
            _ordinaryLogSink(
                $"Display event raw: sequence={rawEvent.Sequence}{generation} " +
                $"family={rawEvent.Message ?? "unknown"} type={subtype} wParam={rawEvent.WParam}{detail}");
        }

        private void WriteJson(object value) => _logSink("DPM_DISPLAY_EVENT " + System.Text.Json.JsonSerializer.Serialize(value));
    }
}
