using Microsoft.Win32;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DisplayProfileManager.Helpers
{
    public static class ShellContextMenuHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private const string ClsidString = "{58C9DBB4-174A-4BCA-88ED-54D760323400}";
        private const string DllName = "ShellExt.dll";
        private const string ClsidKeyPath = @"Software\Classes\CLSID\" + ClsidString;
        private const string HandlerKeyPath = @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\DisplayProfileManager";

        public static bool IsRegistered()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HandlerKeyPath, false))
                {
                    return key != null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking shell extension registration");
                return false;
            }
        }

        public static bool Register()
        {
            try
            {
                var dllPath = ResolveDllPath();
                if (dllPath == null)
                {
                    logger.Error("ShellExt.dll not found alongside executable");
                    return false;
                }

                using (var clsidKey = Registry.CurrentUser.CreateSubKey(ClsidKeyPath, writable: true))
                {
                    clsidKey.SetValue(string.Empty, "DPM Shell Extension", RegistryValueKind.String);

                    using (var inproc = clsidKey.CreateSubKey("InprocServer32", writable: true))
                    {
                        inproc.SetValue(string.Empty, dllPath, RegistryValueKind.String);
                        inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                    }
                }

                using (var handlerKey = Registry.CurrentUser.CreateSubKey(HandlerKeyPath, writable: true))
                    handlerKey.SetValue(string.Empty, ClsidString, RegistryValueKind.String);

                logger.Info("Registered DPM shell extension");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error registering shell extension");
                return false;
            }
        }

        public static bool Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(ClsidKeyPath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(HandlerKeyPath, throwOnMissingSubKey: false);
                logger.Info("Unregistered DPM shell extension");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error unregistering shell extension");
                return false;
            }
        }

        private static string ResolveDllPath()
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetExecutingAssembly().Location);

            if (exeDir == null) return null;

            var path = Path.Combine(exeDir, DllName);
            return File.Exists(path) ? path : null;
        }

        public static bool RestartExplorer()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Error stopping Explorer");
                        return false;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = true
                });

                logger.Info("Restarted Explorer");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error restarting Explorer");
                return false;
            }
        }
    }
}