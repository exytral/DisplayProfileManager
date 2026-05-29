using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Core;

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
            Assert.IsTrue(script.IsEnabled,
                "Scripts must default to enabled so existing profiles without IsEnabled set remain active.");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_BareFilename_ReturnsFilename()
        {
            var script = new Script { FileName = "run.ps1", Arguments = string.Empty };

            Assert.AreEqual("run.ps1", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_WithArguments_AppendsArgs()
        {
            var script = new Script { FileName = "run.ps1", Arguments = "--flag" };

            Assert.AreEqual("run.ps1 --flag", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_FilenameWithSpaces_QuotesFilename()
        {
            var script = new Script { FileName = "my script.ps1", Arguments = string.Empty };

            Assert.AreEqual("\"my script.ps1\"", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_ToString_FilenameWithSpacesAndArgs()
        {
            var script = new Script { FileName = "my script.ps1", Arguments = "-x" };

            Assert.AreEqual("\"my script.ps1\" -x", script.ToString());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Script_IsEnabled_False_NotReflectedInToString()
        {
            var script = new Script { FileName = "run.ps1", Arguments = string.Empty, IsEnabled = false };

            Assert.AreEqual("run.ps1", script.ToString(),
                "IsEnabled is a runtime gate, not part of the display string.");
        }
    }
}