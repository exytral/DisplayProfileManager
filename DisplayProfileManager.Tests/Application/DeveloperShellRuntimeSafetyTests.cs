using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class DeveloperShellRuntimeSafetyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void DevBuild_UnshellFailureAbortsBeforeRestoreReregisterAndLaunch()
        {
            string script = File.ReadAllText(FindRepositoryFile("dev-build.ps1"));

            int exitCapture = script.IndexOf("$unshellExitCode = $LASTEXITCODE", StringComparison.Ordinal);
            int failureGuard = script.IndexOf("$unshellExitCode -ne 0 -and $unshellExitCode -ne 2", StringComparison.Ordinal);
            int restore = script.IndexOf("Restoring NuGet packages...", StringComparison.Ordinal);
            int developerRegistration = script.IndexOf("& $exe --shell-dev", StringComparison.Ordinal);
            int launch = script.IndexOf("Launching dev instance...", StringComparison.Ordinal);

            Assert.IsTrue(exitCapture >= 0);
            Assert.IsTrue(failureGuard > exitCapture);
            Assert.IsTrue(restore > failureGuard);
            Assert.IsTrue(developerRegistration > failureGuard);
            Assert.IsTrue(launch > failureGuard);

            string guardedSection = script[failureGuard..restore];

            StringAssert.Contains(guardedSection, "abort");
            StringAssert.Contains(guardedSection, "exit 1");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ShellRegistrar_RegistrationChangesNotifyAssociationChanged()
        {
            string source = File.ReadAllText(FindRepositoryFile(Path.Combine("DisplayProfileManager",  "Integration", "ShellExtensionRegistrar.cs")));

            StringAssert.Contains(source, "SHChangeNotify");
            StringAssert.Contains(source, "ShcneAssocChanged = 0x08000000");

            int first = source.IndexOf("NotifyAssociationChanged();", StringComparison.Ordinal);
            int second = source.IndexOf("NotifyAssociationChanged();", first + 1, StringComparison.Ordinal);

            Assert.IsTrue(first >= 0, "registration notification missing");
            Assert.IsTrue(second > first, "unregistration notification missing");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExternalInvocationTrace_RecordsRoutingIdentityAndDevFallbackOutcome()
        {
            string source = File.ReadAllText(FindRepositoryFile(Path.Combine("DisplayProfileManager",  "App.xaml.cs")));

            StringAssert.Contains(source, "External invocation | ");
            StringAssert.Contains(source, "pid={Environment.ProcessId} | ");
            StringAssert.Contains(source, "DevMode={devMode}");
            StringAssert.Contains(source, "IsHeadless={isHeadless}");
            StringAssert.Contains(source, "source={profileSource}");
            StringAssert.Contains(source, "profile='{profile ?? \"<current>\"}'");
            StringAssert.Contains(source, "Dev IPC first send failed; falling through to local execution.");
            StringAssert.Contains(source, "All commands passed to active dev instance. Exiting.");

            int sendFailure = source.IndexOf("Dev IPC first send failed; falling through to local execution.", StringComparison.Ordinal);
            int localExecution = source.IndexOf("if (!devMode && commandQueue.Count > 0)", sendFailure, StringComparison.Ordinal);

            Assert.IsTrue(sendFailure >= 0);
            Assert.IsTrue(localExecution > sendFailure, "first dev send failure must be observed before local fallback proceeds");
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException($"Repository file not found: {relativePath}");
        }
    }
}
