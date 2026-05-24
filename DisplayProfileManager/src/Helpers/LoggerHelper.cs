using NLog;
using System.Runtime.CompilerServices;

namespace DisplayProfileManager.Helpers
{
    public static class LoggerHelper
    {
        public static Logger GetLogger([CallerFilePath] string callerFilePath = "")
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(callerFilePath);
            return LogManager.GetLogger(className);
        }
    }
}
