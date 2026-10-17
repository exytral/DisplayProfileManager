using System.Diagnostics;

namespace DisplayProfileManager.Displays.Events
{
    internal static class DisplayEventProcessInfo
    {
        internal static int ProcessId => System.Environment.ProcessId;
        internal static int SessionId => Process.GetCurrentProcess().SessionId;
    }
}
