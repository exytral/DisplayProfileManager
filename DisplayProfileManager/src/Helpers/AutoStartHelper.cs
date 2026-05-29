using DisplayProfileManager.Core;
using Microsoft.Win32;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace DisplayProfileManager.Helpers
{
    public class AutoStartHelper
    {
        private static readonly Logger logger = LoggerHelper.GetLogger();

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryValueName = "DisplayProfileManager";

        private const string TaskName = "DisplayProfileManager_Startup";
        private const string TaskFolder = "\\DisplayProfileManager";
        private const string FullTaskPath = TaskFolder + "\\" + TaskName;

        public bool EnableAutoStart(AutoStartMode mode, bool startInTray = false)
        {
            try
            {
                return mode == AutoStartMode.Registry ? EnableAutoStartRegistry(startInTray) : EnableAutoStartTaskScheduler(startInTray);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error enabling auto start");
                return false;
            }
        }

        public bool DisableAutoStart()
        {
            try
            {
                bool registryResult = true;
                if (IsAutoStartEnabledRegistry())
                    registryResult = DisableAutoStartRegistry();

                bool taskSchedulerResult = true;
                if (IsAutoStartEnabledTaskScheduler())
                    taskSchedulerResult = DisableAutoStartTaskScheduler();

                return registryResult || taskSchedulerResult;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disabling auto start");
                return false;
            }
        }

        #region Registry Implementation

        private bool IsAutoStartEnabledRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(RegistryValueName);
                        bool isEnabled = value != null;

                        logger.Debug($"Auto-start registry value {(isEnabled ? "found" : "not found")}");

                        return isEnabled;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking registry auto start");
                return false;
            }
        }

        private bool EnableAutoStartRegistry(bool startInTray = false)
        {
            try
            {
                var executablePath = GetExecutablePath();
                if (string.IsNullOrEmpty(executablePath))
                {
                    logger.Error("Could not determine executable path");
                    return false;
                }

                if (!File.Exists(executablePath))
                {
                    logger.Error($"Executable path does not exist: {executablePath}");
                    return false;
                }

                var command = startInTray ? $"\"{executablePath}\" --tray" : $"\"{executablePath}\"";
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue(RegistryValueName, command, RegistryValueKind.String);

                        logger.Info($"Successfully enabled registry auto-start: {command}");
                        return true;
                    }
                    else
                    {
                        logger.Error("Could not open registry key for writing");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error enabling registry auto start");
                return false;
            }
        }

        private bool DisableAutoStartRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key != null)
                    {
                        var value = key.GetValue(RegistryValueName);
                        if (value != null)
                        {
                            key.DeleteValue(RegistryValueName, false);
                            logger.Info("Successfully disabled registry auto-start");
                        }

                        return true;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disabling registry auto start");
                return false;
            }
        }

        #endregion

        #region Task Scheduler Implementation

        private bool IsAutoStartEnabledTaskScheduler()
        {
            try
            {
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Query /TN \"{FullTaskPath}\" /FO LIST /V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                })
                {
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    bool isEnabled = process.ExitCode == 0 && output.Contains(TaskName) && output.Contains("Enabled") && !output.Contains("Disabled");

                    logger.Debug($"Task Scheduler auto-start {(isEnabled ? "found" : "not found")}");
                    return isEnabled;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking Task Scheduler auto start");
                return false;
            }
        }

        private bool EnableAutoStartTaskScheduler(bool startInTray = false)
        {
            try
            {
                var executablePath = GetExecutablePath();
                if (string.IsNullOrEmpty(executablePath))
                {
                    logger.Error("Could not determine executable path");
                    return false;
                }

                if (!File.Exists(executablePath))
                {
                    logger.Error($"Executable path does not exist: {executablePath}");
                    return false;
                }

                var xmlContent = GenerateTaskXml(executablePath, startInTray);
                var tempXmlPath = Path.Combine(Path.GetTempPath(), "DisplayProfileManager_Task.xml");
                try
                {
                    File.WriteAllText(tempXmlPath, xmlContent, Encoding.Unicode);

                    using (var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = $"/Create /TN \"{FullTaskPath}\" /XML \"{tempXmlPath}\" /F",
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = false
                        }
                    })
                    {
                        process.Start();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            logger.Info("Successfully created Task Scheduler auto-start (elevated)");
                            return true;
                        }
                        else
                        {
                            logger.Error($"Failed to create Task Scheduler auto-start (elevated). Exit code: {process.ExitCode}");
                            return false;
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tempXmlPath))
                    {
                        try { File.Delete(tempXmlPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error enabling Task Scheduler auto start");
                return false;
            }
        }

        private bool DisableAutoStartTaskScheduler()
        {
            try
            {
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Delete /TN \"{FullTaskPath}\" /F",
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = false
                    }
                })
                {
                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        logger.Info("Successfully deleted Task Scheduler auto-start (elevated)");
                        return true;
                    }
                    else
                    {
                        logger.Error($"Failed to delete Task Scheduler auto-start (elevated). Exit code: {process.ExitCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error disabling Task Scheduler auto start");
                return false;
            }
        }

        private string GenerateTaskXml(string executablePath, bool startInTray = false)
        {
            var currentUser = Environment.UserDomainName + "\\" + Environment.UserName;
            var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            var description = startInTray ? "Starts Display Profile Manager minimized to system tray when user logs on" : "Starts Display Profile Manager when user logs on";
            var argumentsElement = startInTray ? $"\n      <Arguments>--tray</Arguments>" : "";

            string xmlText =
                $@"<?xml version=""1.0"" encoding=""UTF-16""?>
                <Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
                    <RegistrationInfo>
                    <Date>{timestamp}</Date>
                    <Author>{currentUser}</Author>
                    <Description>{description}</Description>
                    <URI>{FullTaskPath}</URI>
                    </RegistrationInfo>
                    <Triggers>
                    <LogonTrigger>
                        <Enabled>true</Enabled>
                        <UserId>{currentUser}</UserId>
                    </LogonTrigger>
                    </Triggers>
                    <Principals>
                    <Principal id=""Author"">
                        <UserId>{currentUser}</UserId>
                        <LogonType>InteractiveToken</LogonType>
                        <RunLevel>LeastPrivilege</RunLevel>
                    </Principal>
                    </Principals>
                    <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <AllowHardTerminate>true</AllowHardTerminate>
                    <StartWhenAvailable>false</StartWhenAvailable>
                    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                    <IdleSettings>
                        <StopOnIdleEnd>false</StopOnIdleEnd>
                        <RestartOnIdle>false</RestartOnIdle>
                    </IdleSettings>
                    <AllowStartOnDemand>true</AllowStartOnDemand>
                    <Enabled>true</Enabled>
                    <Hidden>false</Hidden>
                    <RunOnlyIfIdle>false</RunOnlyIfIdle>
                    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
                    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
                    <WakeToRun>false</WakeToRun>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>0</Priority>
                    </Settings>
                    <Actions Context=""Author"">
                    <Exec>
                        <Command>{executablePath}</Command>{argumentsElement}
                        <WorkingDirectory>{Path.GetDirectoryName(executablePath)}</WorkingDirectory>
                    </Exec>
                    </Actions>
                </Task>";

            return xmlText;
        }

        #endregion

        #region Helper Methods

        private string GetExecutablePath()
        {
            try
            {
                var processPath = Process.GetCurrentProcess().MainModule.FileName;

                if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                {
                    return processPath;
                }

                var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyLocation) && File.Exists(assemblyLocation))
                {
                    return assemblyLocation;
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting executable path");
                return string.Empty;
            }
        }

        public static bool IsRunningAsAdmin()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error checking admin status");
                return false;
            }
        }

        #endregion
    }

    public class AutoStartInfo
    {
        public bool IsEnabled { get; set; }
        public string Command { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string TaskStatus { get; set; } = string.Empty;
        public DateTime? LastRunTime { get; set; }

        public override string ToString()
        {
            return $"Enabled: {IsEnabled}, Valid: {IsValid}, Status: {TaskStatus}, Command: {Command}, LastRun: {LastRunTime}";
        }
    }
}