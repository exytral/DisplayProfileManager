using System;
using System.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Scripts;

namespace DisplayProfileManager.Tests.Scripts
{
    [TestClass]
    public class ScriptRunnerTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_Executable_PreservesRawQuotedArgumentsWithoutRedirects()
        {
            const string arguments = "--name \"A B\" --mode=test";

            var startInfo = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\app.exe", arguments);

            Assert.AreEqual(@"C:\Temp Folder\app.exe", startInfo.FileName);
            Assert.AreEqual(arguments, startInfo.Arguments);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.IsTrue(startInfo.CreateNoWindow);
            Assert.IsFalse(startInfo.RedirectStandardError);
            Assert.IsFalse(startInfo.RedirectStandardOutput);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_PowerShell_QuotesScriptPathAndPreservesArguments()
        {
            var startInfo = ScriptRunner.BuildProcessStartInfo(
                @"C:\Temp Folder\script.ps1",
                "-Name \"A B\" -Literal '\"quoted\"'");

            Assert.AreEqual("powershell.exe", startInfo.FileName);
            Assert.AreEqual(
                "-ExecutionPolicy Bypass -File \"C:\\Temp Folder\\script.ps1\" -Name \"A B\" -Literal '\"quoted\"'",
                startInfo.Arguments);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.IsFalse(startInfo.RedirectStandardError);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_Command_QuotesScriptPathAndPreservesArguments()
        {
            var startInfo = ScriptRunner.BuildProcessStartInfo(
                @"C:\Temp Folder\script.cmd",
                "/flag \"A B\"");

            Assert.AreEqual("cmd.exe", startInfo.FileName);
            Assert.AreEqual("/c \"C:\\Temp Folder\\script.cmd\" /flag \"A B\"", startInfo.Arguments);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.IsFalse(startInfo.RedirectStandardError);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_Python_QuotesScriptPathAndPreservesArguments()
        {
            var startInfo = ScriptRunner.BuildProcessStartInfo(
                @"C:\Temp Folder\script.py",
                "--name \"A B\"");

            Assert.AreEqual("python.exe", startInfo.FileName);
            Assert.AreEqual("\"C:\\Temp Folder\\script.py\" --name \"A B\"", startInfo.Arguments);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.IsFalse(startInfo.RedirectStandardError);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_OtherScriptModes_PreserveInterpreterMappings()
        {
            var batch = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\script.bat", "--flag");
            var visualBasic = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\script.vbs", "--flag");
            var javaScript = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\script.js", "--flag");
            var autoHotkey = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\script.ahk", "--flag");

            Assert.AreEqual("cmd.exe", batch.FileName);
            Assert.AreEqual("/c \"C:\\Temp Folder\\script.bat\" --flag", batch.Arguments);
            Assert.AreEqual("cscript.exe", visualBasic.FileName);
            Assert.AreEqual("/nologo \"C:\\Temp Folder\\script.vbs\" --flag", visualBasic.Arguments);
            Assert.AreEqual("cscript.exe", javaScript.FileName);
            Assert.AreEqual("/nologo \"C:\\Temp Folder\\script.js\" --flag", javaScript.Arguments);
            Assert.AreEqual("autohotkey.exe", autoHotkey.FileName);
            Assert.AreEqual("\"C:\\Temp Folder\\script.ahk\" --flag", autoHotkey.Arguments);
            Assert.IsFalse(batch.RedirectStandardError);
            Assert.IsFalse(visualBasic.RedirectStandardError);
            Assert.IsFalse(javaScript.RedirectStandardError);
            Assert.IsFalse(autoHotkey.RedirectStandardError);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BuildProcessStartInfo_ShellLink_UsesShellWithoutRedirects()
        {
            const string arguments = "--name \"A B\"";

            var startInfo = ScriptRunner.BuildProcessStartInfo(@"C:\Temp Folder\app.lnk", arguments);

            Assert.AreEqual(@"C:\Temp Folder\app.lnk", startInfo.FileName);
            Assert.AreEqual(arguments, startInfo.Arguments);
            Assert.IsTrue(startInfo.UseShellExecute);
            Assert.IsFalse(startInfo.CreateNoWindow);
            Assert.IsFalse(startInfo.RedirectStandardError);
            Assert.IsFalse(startInfo.RedirectStandardOutput);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void StartProcess_Success_DisposesProcessHandleExactlyOnce()
        {
            var disposable = new TrackingDisposable();
            ProcessStartInfo observed = null;
            var startInfo = new ProcessStartInfo("synthetic.exe");

            ScriptRunner.StartProcess(
                startInfo,
                candidate =>
                {
                    observed = candidate;
                    return disposable;
                });

            Assert.AreSame(startInfo, observed);
            Assert.AreEqual(1, disposable.DisposeCount);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void StartProcess_StartFailure_PropagatesToExistingCallerCatch()
        {
            var startInfo = new ProcessStartInfo("synthetic.exe");

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ScriptRunner.StartProcess(
                    startInfo,
                    _ => throw new InvalidOperationException("synthetic start failure")));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void StartProcess_NullProcessResult_Completes()
        {
            var startInfo = new ProcessStartInfo("synthetic.lnk");

            ScriptRunner.StartProcess(startInfo, _ => null);
        }

        private sealed class TrackingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
