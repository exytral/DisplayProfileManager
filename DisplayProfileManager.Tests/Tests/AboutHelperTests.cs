using DisplayProfileManager.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace DisplayProfileManager.Tests.Tests
{
    [TestClass]
    public class AboutHelperTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SystemManagementVersion_MatchesShippedAssemblyMetadata()
        {
            string actual = AboutHelper.Libraries.SystemManagementVersion;
            var assembly = Assembly.Load(new AssemblyName(AboutHelper.Libraries.SystemManagementName));
            string expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            expected = !string.IsNullOrEmpty(expected)
                ? expected.Split('+')[0]
                : assembly.GetName().Version?.ToString(3) ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(actual));
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FormatLibraryDetails_MissingVersion_DoesNotRenderBareVersionPrefix()
        {
            string text = AboutHelper.Libraries.FormatLibraryDetails(string.Empty, "MIT", "Windows system management");

            Assert.AreEqual(" (MIT) - Windows system management", text);
            Assert.IsFalse(text.Contains(" v "));
        }
    }
}
