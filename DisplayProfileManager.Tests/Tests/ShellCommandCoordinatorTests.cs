using DisplayProfileManager.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class ShellCommandCoordinatorTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public async Task Register_AllPreviousStateAndPersistenceCombinations_CompensateOnlyNewRegistration()
        {
            foreach (bool wasRegistered in new[] { false, true })
            foreach (bool registerResult in new[] { false, true })
            foreach (bool loadResult in new[] { false, true })
            foreach (bool saveResult in new[] { false, true })
            {
                bool externalRegistered = wasRegistered;
                int unregisterCalls = 0;
                var warnings = new List<string>();

                int exitCode = await ShellCommandCoordinator.ExecuteAsync(
                    ShellAction.Register,
                    () => wasRegistered,
                    () =>
                    {
                        if (registerResult) externalRegistered = true;
                        return registerResult;
                    },
                    () =>
                    {
                        unregisterCalls++;
                        externalRegistered = false;
                        return true;
                    },
                    () => Task.FromResult(loadResult),
                    _ => Task.FromResult(saveResult),
                    () => true,
                    warnings.Add);

                if (!registerResult)
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, externalRegistered);
                    Assert.AreEqual(0, unregisterCalls);
                    continue;
                }

                bool persisted = loadResult && saveResult;
                if (persisted)
                {
                    Assert.AreEqual(wasRegistered ? 2 : 0, exitCode);
                    Assert.IsTrue(externalRegistered);
                    Assert.AreEqual(0, unregisterCalls);
                }
                else
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, externalRegistered);
                    Assert.AreEqual(wasRegistered ? 0 : 1, unregisterCalls);
                    Assert.IsTrue(warnings.Count > 0);
                }
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Unregister_AllPreviousStateAndPersistenceCombinations_NeverReregistersAfterSuccessfulTeardown()
        {
            foreach (bool wasRegistered in new[] { false, true })
            foreach (bool unregisterResult in new[] { false, true })
            foreach (bool loadResult in new[] { false, true })
            foreach (bool saveResult in new[] { false, true })
            {
                bool externalRegistered = wasRegistered;
                int registerCalls = 0;
                var warnings = new List<string>();

                int exitCode = await ShellCommandCoordinator.ExecuteAsync(
                    ShellAction.Unregister,
                    () => wasRegistered,
                    () =>
                    {
                        registerCalls++;
                        externalRegistered = true;
                        return true;
                    },
                    () =>
                    {
                        if (unregisterResult) externalRegistered = false;
                        return unregisterResult;
                    },
                    () => Task.FromResult(loadResult),
                    _ => Task.FromResult(saveResult),
                    () => true,
                    warnings.Add);

                Assert.AreEqual(0, registerCalls);
                if (!unregisterResult)
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, externalRegistered);
                    continue;
                }

                Assert.IsFalse(externalRegistered);
                Assert.AreEqual(wasRegistered ? 0 : 2, exitCode);
                if (!(loadResult && saveResult))
                    Assert.IsTrue(warnings.Count > 0);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Unregister_ExplorerRestartFails_ExternalRemovalStillWinsAndExitIsFailure()
        {
            bool externalRegistered = true;

            int exitCode = await ShellCommandCoordinator.ExecuteAsync(
                ShellAction.Unregister,
                () => true,
                () =>
                {
                    externalRegistered = true;
                    return true;
                },
                () =>
                {
                    externalRegistered = false;
                    return true;
                },
                () => Task.FromResult(true),
                _ => Task.FromResult(true),
                () => false,
                _ => { });

            Assert.AreEqual(1, exitCode);
            Assert.IsFalse(externalRegistered);
        }
    }
}
