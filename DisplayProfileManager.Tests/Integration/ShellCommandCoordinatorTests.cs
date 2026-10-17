using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;
using DisplayProfileManager.Integration;
using DisplayProfileManager.Settings;
using DisplayProfileManager.Settings.Models;
using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Tests.Integration
{
    [TestClass]
    public class ShellCommandCoordinatorTests
    {
        private sealed class FakeSettingsStore : ISettingsStore
        {
            public string SettingsFilePath => @"C:\Config\Settings.json";
            public string AppDataFolder => @"C:\Config";
            public bool LoadResult { get; set; } = true;
            public bool SaveResult { get; set; } = true;
            public AppSettings Settings { get; } = new AppSettings();

            public Task<AppSettings> LoadAsync()
            {
                if (!LoadResult) throw new InvalidOperationException("load failure");
                return Task.FromResult(Settings);
            }

            public Task<bool> SaveAsync(AppSettings settings) => Task.FromResult(SaveResult);
        }

        private sealed class FakeRegistrar : IShellExtensionRegistrar
        {
            public bool Registered { get; set; }
            public bool RegisterResult { get; set; } = true;
            public bool UnregisterResult { get; set; } = true;
            public bool RestartResult { get; set; } = true;
            public int RegisterCalls { get; private set; }
            public int UnregisterCalls { get; private set; }
            public ShellRegistrationMode Mode { get; set; } = ShellRegistrationMode.Normal;

            public bool IsRegistered() => Registered;
            public ShellRegistrationMode GetRegistrationMode() => Mode;

            public bool Register() => Register(ShellRegistrationMode.Normal);

            public bool Register(ShellRegistrationMode mode)
            {
                RegisterCalls++;
                if (RegisterResult)
                {
                    Registered = true;
                    Mode = mode;
                }
                return RegisterResult;
            }

            public bool Unregister()
            {
                UnregisterCalls++;
                if (UnregisterResult)
                {
                    Registered = false;
                    Mode = ShellRegistrationMode.Normal;
                }
                return UnregisterResult;
            }

            public bool RestartExplorer() => RestartResult;
        }

        private static ShellCommandCoordinator Create(
            bool wasRegistered,
            bool registerResult,
            bool unregisterResult,
            bool loadResult,
            bool saveResult,
            bool restartResult,
            out FakeRegistrar registrar)
        {
            var store = new FakeSettingsStore { LoadResult = loadResult, SaveResult = saveResult };
            var settings = new SettingsService(store);
            registrar = new FakeRegistrar
            {
                Registered = wasRegistered,
                RegisterResult = registerResult,
                UnregisterResult = unregisterResult,
                RestartResult = restartResult
            };
            return new ShellCommandCoordinator(settings, registrar);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Register_AllPreviousStateAndPersistenceCombinations_CompensateOnlyNewRegistration()
        {
            foreach (bool wasRegistered in new[] { false, true })
            foreach (bool registerResult in new[] { false, true })
            foreach (bool loadResult in new[] { false, true })
            foreach (bool saveResult in new[] { false, true })
            {
                var warnings = new List<string>();
                var coordinator = Create(wasRegistered, registerResult, true, loadResult, saveResult, true, out var registrar);

                int exitCode = await coordinator.ExecuteAsync(ShellAction.Register, warnings.Add);

                if (!registerResult)
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, registrar.Registered);
                    Assert.AreEqual(0, registrar.UnregisterCalls);
                    continue;
                }

                bool persisted = loadResult && saveResult;
                if (persisted)
                {
                    Assert.AreEqual(wasRegistered ? 2 : 0, exitCode);
                    Assert.IsTrue(registrar.Registered);
                    Assert.AreEqual(0, registrar.UnregisterCalls);
                }
                else
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, registrar.Registered);
                    Assert.AreEqual(wasRegistered ? 0 : 1, registrar.UnregisterCalls);
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
                var warnings = new List<string>();
                var coordinator = Create(wasRegistered, true, unregisterResult, loadResult, saveResult, true, out var registrar);

                int exitCode = await coordinator.ExecuteAsync(ShellAction.Unregister, warnings.Add);

                Assert.AreEqual(0, registrar.RegisterCalls);
                if (!unregisterResult)
                {
                    Assert.AreEqual(1, exitCode);
                    Assert.AreEqual(wasRegistered, registrar.Registered);
                    continue;
                }

                Assert.IsFalse(registrar.Registered);
                Assert.AreEqual(wasRegistered ? 0 : 2, exitCode);
                if (!(loadResult && saveResult))
                {
                    Assert.IsTrue(warnings.Count > 0);
                }
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task RegisterDeveloper_UsesDeveloperRoutingMode()
        {
            var coordinator = Create(false, true, true, true, true, true, out var registrar);

            int exitCode = await coordinator.ExecuteAsync(ShellAction.RegisterDeveloper, _ => { });

            Assert.AreEqual(0, exitCode);
            Assert.IsTrue(registrar.Registered);
            Assert.AreEqual(ShellRegistrationMode.Developer, registrar.Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task RegisterNormal_AfterDeveloperRegistration_ClearsDeveloperRoutingMode()
        {
            var coordinator = Create(true, true, true, true, true, true, out var registrar);
            registrar.Mode = ShellRegistrationMode.Developer;

            int exitCode = await coordinator.ExecuteAsync(ShellAction.Register, _ => { });

            Assert.AreEqual(2, exitCode);
            Assert.AreEqual(ShellRegistrationMode.Normal, registrar.Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Unregister_FromDeveloperRegistration_RemovesRoutingMode()
        {
            var coordinator = Create(true, true, true, true, true, true, out var registrar);
            registrar.Mode = ShellRegistrationMode.Developer;

            int exitCode = await coordinator.ExecuteAsync(ShellAction.Unregister, _ => { });

            Assert.AreEqual(0, exitCode);
            Assert.IsFalse(registrar.Registered);
            Assert.AreEqual(ShellRegistrationMode.Normal, registrar.Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async Task Unregister_ExplorerRestartFails_ExternalRemovalStillWinsAndExitIsFailure()
        {
            var coordinator = Create(true, true, true, true, true, false, out var registrar);

            int exitCode = await coordinator.ExecuteAsync(ShellAction.Unregister, _ => { });

            Assert.AreEqual(1, exitCode);
            Assert.IsFalse(registrar.Registered);
        }
    }
}
