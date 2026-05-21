using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Helpers;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ScriptHelperTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_SimpleName_ReturnsNameWithNoArgs()
        {
            var (path, args) = ScriptHelper.ParseScriptString("myscript.ps1");

            Assert.AreEqual("myscript.ps1", path);
            Assert.AreEqual(string.Empty, args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_NameWithArgs_SplitsCorrectly()
        {
            var (path, args) = ScriptHelper.ParseScriptString("myscript.ps1 -on");

            Assert.AreEqual("myscript.ps1", path);
            Assert.AreEqual("-on", args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_QuotedNameWithSpaces_ReturnsFullName()
        {
            var (path, args) = ScriptHelper.ParseScriptString("\"my script.ps1\"");

            Assert.AreEqual("my script.ps1", path);
            Assert.AreEqual(string.Empty, args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_QuotedNameWithSpacesAndArgs_SplitsCorrectly()
        {
            var (path, args) = ScriptHelper.ParseScriptString("\"my script.ps1\" -on");

            Assert.AreEqual("my script.ps1", path);
            Assert.AreEqual("-on", args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_MultipleArgs_PreservesAllArgs()
        {
            var (path, args) = ScriptHelper.ParseScriptString("myscript.ps1 -arg1 value1 -arg2 value2");

            Assert.AreEqual("myscript.ps1", path);
            Assert.AreEqual("-arg1 value1 -arg2 value2", args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_EmptyString_ReturnsEmpty()
        {
            var (path, args) = ScriptHelper.ParseScriptString(string.Empty);

            Assert.AreEqual(string.Empty, path);
            Assert.AreEqual(string.Empty, args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_NullInput_ReturnsEmpty()
        {
            var (path, args) = ScriptHelper.ParseScriptString(null);

            Assert.AreEqual(string.Empty, path);
            Assert.AreEqual(string.Empty, args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_WhitespaceOnly_ReturnsEmpty()
        {
            var (path, args) = ScriptHelper.ParseScriptString("   ");

            Assert.AreEqual(string.Empty, path);
            Assert.AreEqual(string.Empty, args);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ParseScriptString_UnclosedQuote_ReturnsEmptyArgs()
        {
            var (_, args) = ScriptHelper.ParseScriptString("\"unclosed.ps1");

            Assert.AreEqual(string.Empty, args,
                "An unclosed quote produces no args — the whole string is treated as a malformed path.");
        }
    }
}
