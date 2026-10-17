using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DisplayProfileManager.UI.Themes;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class DeclarativeThemeTests
    {
        private const string Prefix = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">";
        private const string Six = "<Color x:Key=\"BackgroundColor\">#112233</Color>" +
            "<SolidColorBrush x:Key=\"WindowBackgroundBrush\" Color=\"{StaticResource BackgroundColor}\"/>" +
            "<SolidColorBrush x:Key=\"PrimaryTextBrush\" Color=\"White\"/>" +
            "<SolidColorBrush x:Key=\"ContentBackgroundBrush\" Color=\"Black\"/>" +
            "<SolidColorBrush x:Key=\"BorderBrush\" Color=\"#001122\"/>" +
            "<SolidColorBrush x:Key=\"ButtonBackgroundBrush\" Color=\"#112233\"/>" +
            "<SolidColorBrush x:Key=\"ButtonForegroundBrush\" Color=\"White\"/>";
        private static void Run(string xml, bool valid)
        {
            string file = Path.Combine(Path.GetTempPath(), "dpm-theme-synthetic-" + Guid.NewGuid().ToString("N") + ".xaml");
            try
            {
                File.WriteAllText(file, xml, Encoding.UTF8);
                if (valid)
                {
                    var dict = ThemeStore.ReadDeclarativeTheme(file);
                    Assert.AreEqual(8, dict.Count);
                }
                else
                    Assert.Throws<Exception>(() => ThemeStore.ReadDeclarativeTheme(file));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SafeColorsReferencesAndShadow_AreParsedAsData()
        {
            string xml = Prefix + Six + "<DropShadowEffect x:Key=\"CardShadow\" Color=\"Black\" ShadowDepth=\"2\" Opacity=\"0.15\" BlurRadius=\"8\" Direction=\"270\"/></ResourceDictionary>";
            Run(xml, true);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public async System.Threading.Tasks.Task SyntheticImportDiscoveryAndReload_UseDeclarativeParser()
        {
            string root = Path.Combine(Path.GetTempPath(), "dpm-theme-root-" + Guid.NewGuid().ToString("N"));
            string external = Path.Combine(Path.GetTempPath(), "dpm-theme-source-" + Guid.NewGuid().ToString("N") + ".xaml");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(external, Prefix + Six + "</ResourceDictionary>");
                var store = new ThemeStore(root);
                string imported = await store.ImportThemeAsync(external);
                Assert.IsNotNull(imported);
                Assert.IsTrue(store.IsUserTheme(imported));
                Assert.IsNotNull(store.LoadUserTheme(imported));
                var themes = new System.Collections.Generic.Dictionary<string, System.Windows.ResourceDictionary>();
                store.LoadThemesFromFolder(themes);
                Assert.IsTrue(themes.ContainsKey(imported));
            }
            finally
            {
                if (File.Exists(external))
                    File.Delete(external);
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FiveAccentTokens_UseDistinctInjectedPaletteValues()
        {
            string[] tokens = ["AccentColor", "AccentColorDark1", "AccentColorDark2", "AccentColorLight1", "AccentColorLight2"];
            var colors = new System.Collections.Generic.Dictionary<string, System.Windows.Media.Color>();
            for (int i = 0; i < tokens.Length; i++)
                colors[tokens[i]] = System.Windows.Media.Color.FromRgb((byte)(10 + i), (byte)(20 + i), (byte)(30 + i));

            string xml = Prefix + Six;
            foreach (string name in tokens)
                xml += "<SolidColorBrush x:Key=\"" + name + "Brush\" Color=\"{DynamicResource {x:Static SystemColors." + name + "Key}}\"/>";
            xml += "</ResourceDictionary>";

            var dict = ThemeStore.ReadDeclarativeThemeBytes(Encoding.UTF8.GetBytes(xml), token => colors[token]);
            foreach (string name in tokens)
                Assert.AreEqual(colors[name], ((System.Windows.Media.SolidColorBrush)dict[name + "Brush"]).Color);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PackagedColorThemes_ParseAsExternalDeclarativeData()
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "DisplayProfileManager", "UI", "Themes", "Light.xaml")))
                root = Directory.GetParent(root)?.FullName;

            Assert.IsNotNull(root, "Source theme fixture root not found");
            foreach (string name in new[] { "Light", "Dark", "Black" })
            {
                var dict = ThemeStore.ReadDeclarativeTheme(
                    Path.Combine(root, "DisplayProfileManager", "UI", "Themes", name + ".xaml"));
                Assert.AreEqual(55, dict.Count, name);
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnsupportedObjectsMarkupAndEntities_AreRejectedWithoutInstantiation()
        {
            Run(Prefix + Six + "<Button x:Key=\"Unexpected\"/></ResourceDictionary>", false);
            Run(Prefix + Six + "<SolidColorBrush x:Key=\"Unexpected\" Color=\"{Binding Path=Foo}\"/></ResourceDictionary>", false);
            Run("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///x\">]>" + Prefix + Six + "</ResourceDictionary>", false);
            Run(Prefix + Six + "<ResourceDictionary.MergedDictionaries/></ResourceDictionary>", false);
        }
    }
}
