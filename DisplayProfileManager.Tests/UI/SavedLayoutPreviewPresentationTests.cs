using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class SavedLayoutPreviewPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void PreviewOutlines_UseStrongOneDipDetailsBorderTreatment()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml.cs")));

            StringAssert.Contains(xaml, "BorderThickness=\"1\"");
            StringAssert.Contains(code, "BorderThickness = new Thickness(1)");
            StringAssert.Contains(code, "SetOrdinaryThemeResource(DiagramBorder, Border.BorderBrushProperty, OrdinaryThemeResource.Border);");
            StringAssert.Contains(code, "SetOrdinaryThemeResource(tile, Border.BorderBrushProperty, OrdinaryThemeResource.Border);");
            StringAssert.Contains(code, "OrdinaryThemeResource.Border => ThemeService.DetailsBorderBrushKey");
            Assert.IsFalse(code.Contains("OrdinaryThemeResource.Border => \"BorderBrush\"", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("GetHairlineThickness", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviewHighContrast_UsesSystemBorderAuthority()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml.cs")));

            StringAssert.Contains(code, "DiagramBorder.BorderBrush = SystemColors.WindowTextBrush;");
            StringAssert.Contains(code, "tile.BorderBrush = SystemColors.WindowTextBrush;");
            Assert.IsFalse(code.Contains("tile.BorderBrush = footprint.IsPrimary", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviewNames_ExposeFullSavedNameThroughTooltip()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml.cs")));

            StringAssert.Contains(code, "TextTrimming = TextTrimming.CharacterEllipsis");
            StringAssert.Contains(code, "ToolTip = visibleNames[index]");
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            throw new FileNotFoundException($"Repository file not found: {relativePath}");
        }
    }
}
