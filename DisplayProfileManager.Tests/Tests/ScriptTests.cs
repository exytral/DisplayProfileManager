using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;
using System.IO;
using System.Reflection;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ScriptTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Script_DefaultValues_AreCorrect()
        {
            var script = new Script();

            Assert.AreEqual(string.Empty, script.FileName);
            Assert.AreEqual(string.Empty, script.Arguments);
            Assert.IsTrue(script.IsEnabled, "Individual scripts must default to enabled so existing profiles without IsEnabled set remain active.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_BareFilename_ReturnsFilename()
        {
            var script = new Script { FileName = "script.ps1", Arguments = string.Empty };

            Assert.AreEqual("script.ps1", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_WithArguments_AppendsArgs()
        {
            var script = new Script { FileName = "script.ps1", Arguments = "--flag" };

            Assert.AreEqual("script.ps1 --flag", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_FilenameWithSpaces_QuotesFilename()
        {
            var script = new Script { FileName = "test script.ps1", Arguments = string.Empty };

            Assert.AreEqual("\"test script.ps1\"", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_FilenameWithSpacesAndArgs()
        {
            var script = new Script { FileName = "test script.ps1", Arguments = "-x" };

            Assert.AreEqual("\"test script.ps1\" -x", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_IsEnabled_False_NotReflectedInToString()
        {
            var script = new Script { FileName = "script.ps1", Arguments = string.Empty, IsEnabled = false };

            Assert.AreEqual("script.ps1", script.ToString(), "IsEnabled is a runtime gate, not part of the display string.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ScriptSandboxResolver_AcceptsCanonicalPathInsideSandbox()
        {
            string root = Path.Combine(Path.GetTempPath(), "DpmScriptSandbox");
            Assert.IsTrue(TryResolveSandboxedPath(root, @"nested\..\safe.ps1", out string resolved));
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "safe.ps1")), resolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ScriptSandboxResolver_RejectsBackslashTraversalOutsideSandbox()
        {
            string root = Path.Combine(Path.GetTempPath(), "DpmScriptSandbox");
            Assert.IsFalse(TryResolveSandboxedPath(root, @"..\outside.ps1", out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ScriptSandboxResolver_RejectsForwardSlashTraversalOutsideSandbox()
        {
            string root = Path.Combine(Path.GetTempPath(), "DpmScriptSandbox");
            Assert.IsFalse(TryResolveSandboxedPath(root, "../outside.ps1", out _));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ScriptSandboxResolver_RejectsRootedPathOutsideSandbox()
        {
            string root = Path.Combine(Path.GetTempPath(), "DpmScriptSandbox");
            string outside = Path.GetFullPath(Path.Combine(root, "..", "outside.ps1"));
            Assert.IsFalse(TryResolveSandboxedPath(root, outside, out _));
        }

        private static bool TryResolveSandboxedPath(string root, string fileName, out string resolved)
        {
            var method = typeof(ScriptManager).GetMethod("TryResolveSandboxedScriptPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            object[] args = { root, fileName, null };
            bool result = (bool)method.Invoke(null, args);
            resolved = args[2] as string;
            return result;
        }
    }
}