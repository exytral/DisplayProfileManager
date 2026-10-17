using System.Diagnostics;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Application;

namespace DisplayProfileManager.Tests.Application
{
    [TestClass]
    public class AboutHelperTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ApplicationVersionMetadata_ProvidersReflectAssemblyMetadata()
        {
            Assembly assembly = typeof(AboutInfoProvider).Assembly;
            string fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion ?? string.Empty;
            string informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(fileVersion));
            Assert.IsFalse(string.IsNullOrWhiteSpace(informationalVersion));
            Assert.AreEqual(fileVersion, AboutInfoProvider.GetVersion());
            Assert.AreEqual(informationalVersion, AboutInfoProvider.GetInformationalVersion());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SystemManagementVersion_MatchesShippedAssemblyMetadata()
        {
            string actual = AboutInfoProvider.Libraries.SystemManagementVersion;
            var assembly = Assembly.Load(new AssemblyName(AboutInfoProvider.Libraries.SystemManagementName));
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
            string text = AboutInfoProvider.Libraries.FormatLibraryDetails(string.Empty, "MIT", "Windows system management");

            Assert.AreEqual(" (MIT) - Windows system management", text);
            Assert.IsFalse(text.Contains(" v "));
        }
    }
}
