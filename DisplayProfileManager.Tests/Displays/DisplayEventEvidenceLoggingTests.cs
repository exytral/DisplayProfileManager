using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Displays.Events;

namespace DisplayProfileManager.Tests.Displays
{
    [TestClass]
    public class DisplayEventEvidenceLoggingTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void NLogConfig_EvidenceRuleIsDedicatedFinalAndRetainedSevenDays()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "NLog.config");
            var document = XDocument.Load(path);
            XNamespace ns = document.Root.Name.Namespace;

            var variables = document.Root.Elements(ns + "variable").ToList();
            var evidenceDirectory = variables.Single(element => (string)element.Attribute("name") == "evidenceDirectory");
            Assert.AreEqual("${logDirectory}/Evidence", (string)evidenceDirectory.Attribute("value"));

            var targets = document.Root.Element(ns + "targets").Elements(ns + "target").ToList();
            var evidenceTarget = targets.Single(element => (string)element.Attribute("name") == "evidenceFile");
            Assert.AreEqual("${evidenceDirectory}/DisplayEvents-${shortdate}.log", (string)evidenceTarget.Attribute("fileName"));
            Assert.AreEqual("7", (string)evidenceTarget.Attribute("maxArchiveDays"));
            Assert.AreEqual("Day", (string)evidenceTarget.Attribute("archiveEvery"));

            var rules = document.Root.Element(ns + "rules").Elements(ns + "logger").ToList();
            var evidenceRule = rules.Single(element =>
                (string)element.Attribute("name") == DisplayEventProcessor.EvidenceLoggerName);
            Assert.AreEqual("evidenceFile", (string)evidenceRule.Attribute("writeTo"));
            Assert.AreEqual("true", (string)evidenceRule.Attribute("final"));
            Assert.IsTrue(rules.IndexOf(evidenceRule) < rules.FindIndex(element => (string)element.Attribute("name") == "*"));
        }
    }
}
