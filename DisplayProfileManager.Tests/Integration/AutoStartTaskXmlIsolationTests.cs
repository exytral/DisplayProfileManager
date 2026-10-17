using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.Integration;

namespace DisplayProfileManager.Tests.Integration
{
    [TestClass]
    public class AutoStartTaskXmlIsolationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void Overlap_UsesDistinctContentsAndOwnedCleanup()
        {
            var files = new Dictionary<string, string>();
            var removed = new List<string>();
            var result = AutoStartService.StageTaskXml("A", () => "fake-A",
                (p, xml) => files[p] = xml,
                p =>
                {
                    var nested = AutoStartService.StageTaskXml("B", () => "fake-B",
                        (q, xml) => files[q] = xml,
                        q =>
                        {
                            Assert.AreEqual("A", files[p]);
                            Assert.AreEqual("B", files[q]);
                            return AutoStartOperationResult.Success;
                        },
                        q => { removed.Add(q); files.Remove(q); });
                    Assert.AreEqual(AutoStartOperationResult.Success, nested);
                    Assert.AreEqual("A", files[p]);
                    return AutoStartOperationResult.Success;
                },
                p => { removed.Add(p); files.Remove(p); });
            Assert.AreEqual(AutoStartOperationResult.Success, result);
            CollectionAssert.AreEquivalent(new[] { "fake-A", "fake-B" }, removed);
            Assert.AreEqual(0, files.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExceptionsAndCancellation_AlwaysCleanup()
        {
            foreach (bool duringWrite in new[] { true, false })
            {
                int cleaned = 0;
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    AutoStartService.StageTaskXml("xml", () => "synthetic",
                        (p, xml) => { if (duringWrite) throw new InvalidOperationException(); },
                        p => throw new InvalidOperationException(),
                        p => cleaned++));
                Assert.AreEqual(1, cleaned);
            }
            foreach (var exit in new[] { AutoStartOperationResult.Canceled, AutoStartOperationResult.Failed })
            {
                int cleaned = 0;
                var result = AutoStartService.StageTaskXml("xml", () => "synthetic",
                    (p, xml) => Assert.AreEqual("xml", xml),
                    p => exit, p => cleaned++);
                Assert.AreEqual(exit, result);
                Assert.AreEqual(1, cleaned);
            }
        }
    }
}
