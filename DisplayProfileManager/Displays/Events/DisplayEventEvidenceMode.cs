using System;
using System.Threading.Tasks;

namespace DisplayProfileManager.Displays.Events
{
    internal enum DisplayEventEvidenceMode
    {
        Disabled,
        ApplyScoped,
        AppLifetime
    }

    internal sealed class DisplayEventEngineeringOptions
    {
        internal const string EvidenceEnvironmentVariableName = "DPM_DISPLAY_EVENT_EVIDENCE";
        internal const string TraceEnvironmentVariableName = "DPM_DISPLAY_EVENT_TRACE";
        internal const string ApplyScopedValue = "apply-scoped";
        internal const string AppLifetimeValue = "app-lifetime";

        private DisplayEventEngineeringOptions(DisplayEventEvidenceMode evidenceMode, bool traceEnabled)
        {
            EvidenceMode = evidenceMode;
            TraceEnabled = traceEnabled;
        }

        internal DisplayEventEvidenceMode EvidenceMode { get; }
        internal bool TraceEnabled { get; }

        internal static DisplayEventEngineeringOptions Resolve(
            Func<string, string> readVariable,
            Action<string> warningSink = null)
        {
            if (readVariable == null) throw new ArgumentNullException(nameof(readVariable));

            string evidenceValue = readVariable(EvidenceEnvironmentVariableName)?.Trim();
            string traceValue = readVariable(TraceEnvironmentVariableName)?.Trim();

            return new DisplayEventEngineeringOptions(
                ResolveEvidenceMode(evidenceValue, warningSink),
                ResolveTrace(traceValue, warningSink));
        }

        private static DisplayEventEvidenceMode ResolveEvidenceMode(string value, Action<string> warningSink)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(value, "off", StringComparison.Ordinal))
            {
                return DisplayEventEvidenceMode.Disabled;
            }

            if (string.Equals(value, ApplyScopedValue, StringComparison.Ordinal))
            {
                return DisplayEventEvidenceMode.ApplyScoped;
            }

            if (string.Equals(value, AppLifetimeValue, StringComparison.Ordinal))
            {
                return DisplayEventEvidenceMode.AppLifetime;
            }

            warningSink?.Invoke(
                $"event=DisplayEventControlInvalid variable={EvidenceEnvironmentVariableName} value='{value}' -> disabled");
            return DisplayEventEvidenceMode.Disabled;
        }

        private static bool ResolveTrace(string value, Action<string> warningSink)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(value, "0", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                return true;
            }

            warningSink?.Invoke(
                $"event=DisplayEventControlInvalid variable={TraceEnvironmentVariableName} value='{value}' -> disabled");
            return false;
        }
    }

    internal interface IDisplayEventEvidenceScopeProvider
    {
        Task<IAsyncDisposable> BeginApplyEvidenceScopeAsync();
    }
}
