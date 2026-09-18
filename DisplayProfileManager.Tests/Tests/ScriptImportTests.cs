using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ScriptImportTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task ImportScriptsAsync_PreservesSelectionOrderAndPartialFailures()
        {
            var inputs = new[] { "first.ps1", "broken.cmd", "third.exe" };
            var observed = new List<string>();

            var batch = await ScriptManager.ImportScriptsAsync(inputs, path =>
            {
                observed.Add(path);
                string imported = path == "broken.cmd" ? null : path.Replace(".exe", ".lnk");
                return Task.FromResult(imported);
            });

            CollectionAssert.AreEqual(inputs, observed);
            Assert.AreEqual(2, batch.ImportedCount);
            Assert.AreEqual(1, batch.FailedCount);
            Assert.AreEqual(3, batch.Results.Count);
            Assert.AreEqual("first.ps1", batch.Results[0].ImportedFileName);
            Assert.IsFalse(batch.Results[1].Success);
            Assert.AreEqual("third.lnk", batch.Results[2].ImportedFileName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ImportScriptsAsync_SingleSelection_RemainsOneItemCase()
        {
            var batch = await ScriptManager.ImportScriptsAsync(new[] { "only.ps1" }, path => Task.FromResult("only.ps1"));

            Assert.AreEqual(1, batch.ImportedCount);
            Assert.AreEqual(0, batch.FailedCount);
            Assert.AreEqual(1, batch.Results.Count);
            Assert.AreEqual("only.ps1", batch.Results.Single().ImportedFileName);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task ImportScriptsAsync_ImporterException_FailsOnlyThatItemAndContinues()
        {
            var batch = await ScriptManager.ImportScriptsAsync(
                new[] { "first.ps1", "throws.ps1", "last.ps1" },
                path => path == "throws.ps1"
                    ? Task.FromException<string>(new InvalidOperationException("test failure"))
                    : Task.FromResult(path));

            Assert.AreEqual(2, batch.ImportedCount);
            Assert.AreEqual(1, batch.FailedCount);
            Assert.AreEqual("last.ps1", batch.Results[2].ImportedFileName);
        }
    }
}
