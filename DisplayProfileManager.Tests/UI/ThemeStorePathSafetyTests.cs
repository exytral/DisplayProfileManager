using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Themes;

namespace DisplayProfileManager.Tests.Tests.UI
{
    [TestClass]
    public class ThemeStorePathSafetyTests
    {
        private const string SyntheticRoot = @"C:\PX57-707-NEVER-CREATED\Themes";

        [TestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow(".")]
        [DataRow("..")]
        [DataRow("../other")]
        [DataRow(@"..\other")]
        [DataRow(@"sub\child")]
        [DataRow("sub/child")]
        [DataRow(@"C:\outside\item")]
        [DataRow(@"\\server\share\item")]
        [DataRow(@"C:relative")]
        [DataRow("foo:stream")]
        [DataRow("System")]
        [DataRow("sYsTeM")]
        [DataRow("CON")]
        [DataRow("con.txt")]
        [DataRow("NUL")]
        [DataRow("AUX")]
        [DataRow("PRN")]
        [DataRow("CONIN$")]
        [DataRow("conout$.anything")]
        [DataRow("COM1")]
        [DataRow("COM¹")]
        [DataRow("LPT\u00B2.ext")]
        [DataRow("filename.")]
        [DataRow("filename ")]
        [DataRow("bad\u0001name")]
        public void ResolveUserThemePath_InvalidName_ReturnsFalse(string name)
        {
            bool result = ThemeStore.TryResolveUserThemePath(SyntheticRoot, name, out string path);

            Assert.IsFalse(result);
            Assert.IsNull(path);
        }

        [TestMethod]
        [DataRow("Trailing .xaml")]
        [DataRow("Trailing..xaml")]
        [DataRow("System.xaml")]
        [DataRow("sYsTeM.xaml")]
        [DataRow("CONIN$.xaml")]
        [DataRow("COM¹.xaml")]
        [DataRow("file:ads.xaml")]
        [DataRow("nested\\other.xaml")]
        [DataRow("name.txt")]
        public void ResolveUserThemeFileName_InvalidImportOrEnumerationName_IsRejected(string fileName)
        {
            bool result = ThemeStore.TryResolveUserThemeFileName(SyntheticRoot, fileName, out string name, out string path);

            Assert.IsFalse(result);
            Assert.IsNull(name);
            Assert.IsNull(path);
        }

        [TestMethod]
        [DataRow("Light.xaml", "Light")]
        [DataRow("Dark.xaml", "Dark")]
        [DataRow("name (1).xaml", "name (1)")]
        [DataRow("My.Theme.xaml", "My.Theme")]
        [DataRow("日本語.xaml", "日本語")]
        public void ResolveUserThemeFileName_ValidImportOrEnumerationName_MatchesLookup(string fileName, string expected)
        {
            Assert.IsTrue(ThemeStore.TryResolveUserThemeFileName(SyntheticRoot, fileName, out string name, out string path));
            Assert.AreEqual(expected, name);
            Assert.IsTrue(ThemeStore.TryResolveUserThemePath(SyntheticRoot, name, out string lookupPath));
            Assert.AreEqual(path, lookupPath);
        }

        [TestMethod]
        public void ResolveUserThemePath_UnpairedSurrogate_ReturnsFalse()
        {
            foreach (string name in new[] { "bad" + (char)0xD800 + "name", "bad" + (char)0xDC00 + "name" })
            {
                Assert.IsFalse(ThemeStore.TryResolveUserThemePath(SyntheticRoot, name, out string path));
                Assert.IsNull(path);
            }
        }

        [TestMethod]
        [DataRow("Desk Setup")]
        [DataRow("name (1)")]
        [DataRow("Light")]
        [DataRow("Dark")]
        [DataRow("DARK")]
        [DataRow("日本語 🌸")]
        [DataRow("Theme--Name")]
        [DataRow("My.Theme")]
        public void ResolveUserThemePath_ValidChild_ReturnsExactChild(string name)
        {
            bool result = ThemeStore.TryResolveUserThemePath(SyntheticRoot, name, out string path);

            Assert.IsTrue(result);
            Assert.AreEqual(Path.GetFullPath(SyntheticRoot), Path.GetDirectoryName(path), true);
            Assert.AreEqual(name + ".xaml", Path.GetFileName(path));
        }
    }
}
