using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;
using DisplayProfileManager.Profiles;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class EngineeringOverridesTests
    {
        private static EngineeringOverrides Resolve(
            IReadOnlyDictionary<string, string> values,
            List<string> warnings = null) =>
            EngineeringOverrides.Resolve(
                name => values.TryGetValue(name, out string value) ? value : null,
                warning => warnings?.Add(warning));

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_AbsentValues_IsInactive()
        {
            var overrides = Resolve(new Dictionary<string, string>());

            Assert.IsFalse(overrides.AnyActive);
            Assert.IsFalse(overrides.ForceApplyFailureStage.HasValue);
            Assert.IsFalse(overrides.ForceTopologyRecovery);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceApplyFailureInactiveValues_AreInactive()
        {
            foreach (string value in new[] { "", "0", " 0 " })
            {
                var overrides = Resolve(new Dictionary<string, string>
                {
                    [EngineeringOverrides.ForceApplyFailureVariable] = value
                });

                Assert.IsFalse(overrides.ForceApplyFailureStage.HasValue, value);
                Assert.IsFalse(overrides.AnyActive, value);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceApplyFailureOne_SelectsTopology()
        {
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceApplyFailureVariable] = " 1 "
            });

            Assert.AreEqual(ProfileApplyFailureStage.Topology, overrides.ForceApplyFailureStage);
            Assert.IsTrue(overrides.AnyActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceApplyFailureTwo_SelectsDisplayConfiguration()
        {
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceApplyFailureVariable] = "2"
            });

            Assert.AreEqual(ProfileApplyFailureStage.DisplayConfiguration, overrides.ForceApplyFailureStage);
            Assert.IsTrue(overrides.AnyActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceApplyFailureMalformed_IsInactiveAndWarnsOnce()
        {
            var warnings = new List<string>();
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceApplyFailureVariable] = "3"
            }, warnings);

            Assert.IsFalse(overrides.ForceApplyFailureStage.HasValue);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "event=EngineeringOverrideInvalid");
            StringAssert.Contains(warnings[0], EngineeringOverrides.ForceApplyFailureVariable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceTopologyRecoveryInactiveValues_AreInactive()
        {
            foreach (string value in new[] { "", "0", " 0 " })
            {
                var overrides = Resolve(new Dictionary<string, string>
                {
                    [EngineeringOverrides.ForceTopologyRecoveryVariable] = value
                });

                Assert.IsFalse(overrides.ForceTopologyRecovery, value);
                Assert.IsFalse(overrides.AnyActive, value);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceTopologyRecoveryOne_IsActive()
        {
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceTopologyRecoveryVariable] = " 1 "
            });

            Assert.IsTrue(overrides.ForceTopologyRecovery);
            Assert.IsTrue(overrides.AnyActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ForceTopologyRecoveryMalformed_IsInactiveAndWarnsOnce()
        {
            var warnings = new List<string>();
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceTopologyRecoveryVariable] = "true"
            }, warnings);

            Assert.IsFalse(overrides.ForceTopologyRecovery);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(warnings[0], "event=EngineeringOverrideInvalid");
            StringAssert.Contains(warnings[0], EngineeringOverrides.ForceTopologyRecoveryVariable);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_MultipleActiveOverrides_ReportsAnyActive()
        {
            var overrides = Resolve(new Dictionary<string, string>
            {
                [EngineeringOverrides.ForceApplyFailureVariable] = "1",
                [EngineeringOverrides.ForceTopologyRecoveryVariable] = "1"
            });

            Assert.AreEqual(ProfileApplyFailureStage.Topology, overrides.ForceApplyFailureStage);
            Assert.IsTrue(overrides.ForceTopologyRecovery);
            Assert.IsTrue(overrides.AnyActive);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Resolve_ReadsEachKnownVariableExactlyOnce()
        {
            var reads = new Dictionary<string, int>();
            EngineeringOverrides.Resolve(name =>
            {
                reads[name] = reads.TryGetValue(name, out int count) ? count + 1 : 1;
                return null;
            });

            Assert.AreEqual(1, reads[EngineeringOverrides.ForceApplyFailureVariable]);
            Assert.AreEqual(1, reads[EngineeringOverrides.ForceTopologyRecoveryVariable]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RetiredDebugSurface_HasNoProductionOrTestConsumer()
        {
            string root = FindRepositoryRoot();
            string productionRoot = Path.Combine(root, "DisplayProfileManager");
            string testsRoot = Path.Combine(root, "DisplayProfileManager.Tests");

            string[] retired =
            [
                "Debug" + "Flags",
                "SettingsService." + "Debug",
                "SkipSpotlight" + "Repaint",
                "CenterIcon" + "Grid"
            ];

            var sourceFiles = Directory.EnumerateFiles(productionRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Concat(Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories));

            foreach (string path in sourceFiles)
            {
                string text = File.ReadAllText(path);
                foreach (string identifier in retired)
                {
                    Assert.IsFalse(text.Contains(identifier, StringComparison.Ordinal), $"{identifier} remains in {Path.GetRelativePath(root, path)}");
                }
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EngineeringEnvironmentVariables_AreOwnedOnlyByCentralParserInProduction()
        {
            string root = FindRepositoryRoot();
            string productionRoot = Path.Combine(root, "DisplayProfileManager");
            string authority = Path.Combine(
                productionRoot,
                "Application",
                "EngineeringOverrides.cs");

            foreach (string variable in new[]
            {
                EngineeringOverrides.ForceApplyFailureVariable,
                EngineeringOverrides.ForceTopologyRecoveryVariable
            })
            {
                string[] owners = [.. Directory.EnumerateFiles(productionRoot, "*.cs", SearchOption.AllDirectories)
                    .Where(path => File.ReadAllText(path).Contains(variable, StringComparison.Ordinal))
                    .Select(path => Path.GetFullPath(path))];

                CollectionAssert.AreEquivalent(new[] { Path.GetFullPath(authority) }, owners, variable);
            }
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "DisplayProfileManager.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            Assert.Fail("Repository root not found.");
            return string.Empty;
        }
    }
}
