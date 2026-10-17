using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Settings.Persistence;

namespace DisplayProfileManager.Tests.Settings
{
    [TestClass]
    public class SettingsLegacyMigrationTests
    {
        private const string Canonical = @"C:\Fixture\Settings.json";
        private const string Legacy = @"C:\Fixture\settings.json";
        private const string Temp = @"C:\Fixture\Settings.json.tmp";
        private const string CanonicalJson = "{ \"theme\": \"Dark\" }";
        private const string LegacyJson = "{ \"theme\": \"Light\" }";

        private sealed class FakeFiles
        {
            public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
            public bool FailWrite { get; set; }
            public int Writes { get; private set; }
            public bool Exists(string path) => Files.ContainsKey(path);
            public string Read(string path) => Files[path];
            public void AtomicWrite(string path, string json)
            {
                Writes++;
                if (FailWrite) throw new IOException("Injected failed stage/promotion.");
                Files[path] = json;
            }
            public void Migrate() => SettingsStore.MigrateLegacySettings(Canonical, Legacy, Temp, Exists, Read, AtomicWrite);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_CanonicalExists_LeavesBothCopiesUntouched()
        {
            var io = new FakeFiles();
            io.Files[Canonical] = CanonicalJson;
            io.Files[Legacy] = LegacyJson;

            io.Migrate();

            Assert.AreEqual(CanonicalJson, io.Files[Canonical]);
            Assert.AreEqual(LegacyJson, io.Files[Legacy]);
            Assert.AreEqual(0, io.Writes);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_OnlyLegacy_AtomicallyPromotesWithoutDeletingSource()
        {
            var io = new FakeFiles();
            io.Files[Legacy] = LegacyJson;

            io.Migrate();
            io.Migrate();

            Assert.AreEqual(LegacyJson, io.Files[Canonical]);
            Assert.AreEqual(LegacyJson, io.Files[Legacy]);
            Assert.AreEqual(1, io.Writes);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_FailedAtomicPromotion_RetainsLegacyAndRecoversOnNextStartup()
        {
            var io = new FakeFiles { FailWrite = true };
            io.Files[Legacy] = LegacyJson;

            Assert.ThrowsExactly<IOException>(() => io.Migrate());
            Assert.IsFalse(io.Exists(Canonical));
            Assert.AreEqual(LegacyJson, io.Files[Legacy]);

            io.FailWrite = false;
            io.Migrate();

            Assert.AreEqual(LegacyJson, io.Files[Canonical]);
            Assert.AreEqual(LegacyJson, io.Files[Legacy]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_SurvivingHistoricalTemp_RecoversWithoutDeletion()
        {
            var io = new FakeFiles();
            io.Files[Temp] = LegacyJson;

            io.Migrate();
            io.Migrate();

            Assert.AreEqual(LegacyJson, io.Files[Canonical]);
            Assert.AreEqual(LegacyJson, io.Files[Temp]);
            Assert.AreEqual(1, io.Writes);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_LegacyAndTemp_PrefersLegacy()
        {
            var io = new FakeFiles();
            io.Files[Legacy] = LegacyJson;
            io.Files[Temp] = CanonicalJson;

            io.Migrate();

            Assert.AreEqual(LegacyJson, io.Files[Canonical]);
            Assert.AreEqual(CanonicalJson, io.Files[Temp]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_InvalidLegacy_RejectsWithoutDefaultOverwrite()
        {
            var io = new FakeFiles();
            io.Files[Legacy] = "{ invalid";

            Assert.ThrowsExactly<Newtonsoft.Json.JsonReaderException>(() => io.Migrate());

            Assert.IsFalse(io.Exists(Canonical));
            Assert.AreEqual("{ invalid", io.Files[Legacy]);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migrate_NormalWindowsCaseAlias_CanonicalPrecedence()
        {
            var io = new FakeFiles();
            io.Files[Canonical] = CanonicalJson;

            SettingsStore.MigrateLegacySettings(Canonical, Canonical, Temp, io.Exists, io.Read, io.AtomicWrite);

            Assert.AreEqual(CanonicalJson, io.Files[Canonical]);
            Assert.AreEqual(0, io.Writes);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async System.Threading.Tasks.Task LoadAsync_HistoricalStrandedTemp_RestoresCanonicalFromOwnedTemp()
        {
            string root = Path.Combine(Path.GetTempPath(), "DPM-SettingsLegacyMigration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "Settings.json.tmp"), LegacyJson);

                var store = new SettingsStore(root);
                var loaded = await store.LoadAsync();

                Assert.AreEqual("Light", loaded.Theme);
                Assert.AreEqual(LegacyJson, File.ReadAllText(Path.Combine(root, "Settings.json")));
                Assert.AreEqual(LegacyJson, File.ReadAllText(Path.Combine(root, "Settings.json.tmp")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async System.Threading.Tasks.Task LoadAsync_InvalidStrandedTemp_DoesNotPersistDefaults()
        {
            string root = Path.Combine(Path.GetTempPath(), "DPM-SettingsLegacyMigration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string stranded = Path.Combine(root, "Settings.json.tmp");
                File.WriteAllText(stranded, "{ malformed");

                var store = new SettingsStore(root);
                await Assert.ThrowsExactlyAsync<IOException>(() => store.LoadAsync());

                Assert.IsFalse(File.Exists(Path.Combine(root, "Settings.json")));
                Assert.AreEqual("{ malformed", File.ReadAllText(stranded));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }


        [TestMethod]
        [TestCategory("Unit")]
        public async System.Threading.Tasks.Task HistoricalTemp_TolerableMemberError_PromotesIdenticalBytesAndEnablesSettingsService()
        {
            string root = Path.Combine(Path.GetTempPath(), "DPM-SettingsLegacyMigration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                const string json = "{\"theme\":\"Ocean\",\"startWithWindows\":\"not-a-bool\"}";
                string historical = Path.Combine(root, "Settings.json.tmp");
                File.WriteAllText(historical, json);

                var store = new SettingsStore(root);
                Assert.AreEqual(json, File.ReadAllText(Path.Combine(root, "Settings.json")));
                Assert.AreEqual(json, File.ReadAllText(historical));

                var service = new DisplayProfileManager.Settings.SettingsService(store);
                Assert.IsTrue(await service.LoadSettingsAsync());
                Assert.IsTrue(service.SettingsLoaded);
                Assert.AreEqual("Ocean", service.Settings.Theme);
                Assert.IsFalse(service.Settings.StartWithWindows);
                Assert.IsTrue(await service.SaveSettingsAsync());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migration_RejectsNonObjectJsonRootsWithoutWritingDefaults()
        {
            foreach (string json in new[] { "null", "[]", "42", "\"value\"", "{ invalid" })
            {
                var io = new FakeFiles();
                io.Files[Temp] = json;

                Assert.Throws<Newtonsoft.Json.JsonException>(() => io.Migrate());
                Assert.IsFalse(io.Exists(Canonical));
                Assert.AreEqual(json, io.Files[Temp]);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Migration_InvalidPreferredLegacyAndValidHistoricalTemp_FailsClosed()
        {
            var io = new FakeFiles();
            io.Files[Legacy] = "{ invalid";
            io.Files[Temp] = LegacyJson;

            Assert.ThrowsExactly<Newtonsoft.Json.JsonReaderException>(() => io.Migrate());
            Assert.IsFalse(io.Exists(Canonical));
            Assert.AreEqual("{ invalid", io.Files[Legacy]);
            Assert.AreEqual(LegacyJson, io.Files[Temp]);
        }

    }
}
