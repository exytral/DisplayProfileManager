using NLog;
using System.Runtime.CompilerServices;

namespace DisplayProfileManager.Helpers
{
    public static class LoggerHelper
    {
        // Derives logger name from the caller's file name via CallerFilePath
        public static Logger GetLogger([CallerFilePath] string callerFilePath = "")
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(callerFilePath);
            return LogManager.GetLogger(className);
        }
    }
}
