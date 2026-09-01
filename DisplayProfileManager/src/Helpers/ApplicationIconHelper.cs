using NLog;
using System;
using System.Diagnostics;
using System.Drawing;

namespace DisplayProfileManager.Helpers
{
    internal static class ApplicationIconHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        public static Icon LoadIcon()
        {
            try
            {
                string path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    var icon = Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to load application icon from executable");
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}