using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

using Microsoft.Win32;
using NLog;

using DisplayProfileManager.Common;

namespace DisplayProfileManager.Integration
{
    public sealed class ShellExtensionRegistrar : IShellExtensionRegistrar
    {
        private static readonly Logger _logger = LoggerHelper.GetLogger();

        private const string ClsidString = "{58C9DBB4-174A-4BCA-88ED-54D760323400}";
        private const string DllName = "ShellExt.dll";
        private const string ClsidKeyPath = @"Software\Classes\CLSID\" + ClsidString;
        private const string HandlerKeyPath = @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\DisplayProfileManager";
        private const string RoutingModeValueName = "RoutingMode";
        private const int DeveloperRoutingValue = 1;
        private const uint ShcneAssocChanged = 0x08000000;
        private const uint ShcnfIdList = 0x0000;

        private const uint TokenQuery = 0x0008;
        private const int TokenElevationClass = 20;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle, int informationClass, out int information,
            int informationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(HandlerKeyPath, false);
                return key != null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking shell extension registration");
                return false;
            }
        }

        public ShellRegistrationMode GetRegistrationMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(HandlerKeyPath, false);
                return key?.GetValue(RoutingModeValueName) is int value && value == DeveloperRoutingValue
                    ? ShellRegistrationMode.Developer
                    : ShellRegistrationMode.Normal;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading shell extension registration mode");
                return ShellRegistrationMode.Normal;
            }
        }

        public bool Register() => Register(ShellRegistrationMode.Normal);

        public bool Register(ShellRegistrationMode mode)
        {
            try
            {
                var dllPath = ResolveDllPath();
                if (dllPath == null)
                {
                    _logger.Error("ShellExt.dll not found alongside executable");
                    return false;
                }

                using (var clsidKey = Registry.CurrentUser.CreateSubKey(ClsidKeyPath, writable: true))
                {
                    clsidKey.SetValue(string.Empty, "DPM Shell Extension", RegistryValueKind.String);

                    using var inproc = clsidKey.CreateSubKey("InprocServer32", writable: true);
                    inproc.SetValue(string.Empty, dllPath, RegistryValueKind.String);
                    inproc.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                }

                using (var handlerKey = Registry.CurrentUser.CreateSubKey(HandlerKeyPath, writable: true))
                {
                    handlerKey.SetValue(string.Empty, ClsidString, RegistryValueKind.String);
                    if (mode == ShellRegistrationMode.Developer)
                        handlerKey.SetValue(RoutingModeValueName, DeveloperRoutingValue, RegistryValueKind.DWord);
                    else
                        handlerKey.DeleteValue(RoutingModeValueName, throwOnMissingValue: false);
                }

                NotifyAssociationChanged();
                _logger.Info($"Registered DPM shell extension ({mode})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error registering shell extension");
                return false;
            }
        }

        public bool Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(ClsidKeyPath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(HandlerKeyPath, throwOnMissingSubKey: false);
                NotifyAssociationChanged();
                _logger.Info("Unregistered DPM shell extension");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error unregistering shell extension");
                return false;
            }
        }

        private static void NotifyAssociationChanged() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

        private string ResolveDllPath()
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetExecutingAssembly().Location);

            if (exeDir == null)
            {
                return null;
            }

            var path = Path.Combine(exeDir, DllName);
            return File.Exists(path) ? path : null;
        }

        internal static bool IsEligibleExplorer(
            int candidateSession, string candidateSid, bool candidateElevated,
            int currentSession, string currentSid, bool currentElevated) =>
            candidateSession == currentSession &&
            string.Equals(candidateSid, currentSid, StringComparison.OrdinalIgnoreCase) &&
            candidateElevated == currentElevated;

        private static bool TryGetProcessTokenIdentity(
            Process process, out string sid, out bool elevated)
        {
            sid = null;
            elevated = false;
            if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
                return false;
            try
            {
                using var identity = new WindowsIdentity(token);
                sid = identity.User?.Value;
                if (sid == null ||
                    !GetTokenInformation(token, TokenElevationClass, out var elevation, sizeof(int), out _))
                    return false;
                elevated = elevation != 0;
                return true;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        internal sealed class ExplorerCandidate
        {
            internal Func<bool> HasExited { get; set; }
            internal Func<int> SessionId { get; set; }
            internal Func<(bool Valid, string Sid, bool Elevated)> Identity { get; set; }
            internal Action Kill { get; set; }
            internal Action WaitForExit { get; set; }
            internal Action Dispose { get; set; }
        }

        // Injectable effect boundary: tests exercise the actual completion result
        // without enumerating, terminating, or starting any Windows shell process.
        internal static bool ExecuteExplorerRestart(
            System.Collections.Generic.IEnumerable<ExplorerCandidate> candidates,
            int session, string sid, bool elevated, Func<bool> restart)
        {
            bool stopped = false;
            bool failed = false;
            foreach (var candidate in candidates)
            {
                bool killReturned = false;
                try
                {
                    if (candidate.HasExited() || candidate.SessionId() != session)
                        continue;
                    var identity = candidate.Identity();
                    if (!identity.Valid)
                    {
                        failed = true;
                        continue;
                    }
                    if (!IsEligibleExplorer(candidate.SessionId(), identity.Sid, identity.Elevated,
                        session, sid, elevated))
                        continue;
                    candidate.Kill();
                    killReturned = true;
                    stopped = true;
                    try
                    {
                        candidate.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Explorer Kill succeeded but WaitForExit confirmation failed");
                        failed = true;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // Before Kill returns, an already-exited process is a benign skip.
                    // A successful Kill must never have its later wait failure hidden here.
                    if (killReturned)
                    {
                        _logger.Error(ex, "Explorer operation failed after Kill");
                        failed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error stopping eligible Explorer");
                    failed = true;
                }
                finally
                {
                    try { candidate.Dispose(); }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error disposing Explorer process");
                        failed = true;
                    }
                }
            }

            if (!stopped)
            {
                _logger.Warn("No eligible Explorer process stopped in current user session; restart not performed");
                return false;
            }

            try
            {
                if (!restart())
                {
                    _logger.Error("Explorer restart did not create a process");
                    return false;
                }
                _logger.Info("Requested Explorer restart in current user session");
                return !failed;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error restarting Explorer");
                return false;
            }
        }

        public bool RestartExplorer()
        {
            using var self = Process.GetCurrentProcess();
            try
            {
                if (!TryGetProcessTokenIdentity(self, out var currentSid, out var currentElevated))
                {
                    _logger.Error("Cannot establish current user token; refusing Explorer restart");
                    return false;
                }

                var candidates = new System.Collections.Generic.List<ExplorerCandidate>();
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    candidates.Add(new ExplorerCandidate
                    {
                        HasExited = () => process.HasExited,
                        SessionId = () => process.SessionId,
                        Identity = () =>
                        {
                            var valid = TryGetProcessTokenIdentity(process, out var sid, out var elevated);
                            return (valid, sid, elevated);
                        },
                        Kill = () => process.Kill(),
                        WaitForExit = () => process.WaitForExit(),
                        Dispose = () => process.Dispose()
                    });
                }

                return ExecuteExplorerRestart(candidates, self.SessionId, currentSid, currentElevated, () =>
                {
                    var explorerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    using var restarted = Process.Start(new ProcessStartInfo
                    {
                        FileName = explorerPath,
                        UseShellExecute = true
                    });
                    return restarted != null;
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error preparing Explorer restart");
                return false;
            }
        }
    }
}
