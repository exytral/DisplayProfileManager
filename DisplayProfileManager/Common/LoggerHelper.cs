using System.Runtime.CompilerServices;

using NLog;

namespace DisplayProfileManager.Common
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
