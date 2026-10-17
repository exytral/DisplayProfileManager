using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Themes;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class ThemeServiceTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SavedLayoutPreview_OrdinaryThemeResourceSeam_IsAuthoritativeAndGuaranteed()
        {
            var requiredKeysField = typeof(ThemeStore).GetField(
                "RequiredThemeKeys",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(requiredKeysField);

            var requiredKeys = ((string[])requiredKeysField.GetValue(null)).ToHashSet(StringComparer.Ordinal);
            foreach (var key in SavedLayoutPreviewControl.OrdinaryThemeResourceKeys.Where(
                         key => !string.Equals(key, ThemeService.DetailsBorderBrushKey, StringComparison.Ordinal)))
            {
                Assert.IsTrue(requiredKeys.Contains(key), $"Preview resource '{key}' is not guaranteed by the custom-theme contract.");
            }
            CollectionAssert.Contains(SavedLayoutPreviewControl.OrdinaryThemeResourceKeys.ToList(), ThemeService.DetailsBorderBrushKey);
            CollectionAssert.DoesNotContain(requiredKeys.ToList(), "TertiaryTextBrush");

            CollectionAssert.DoesNotContain(SavedLayoutPreviewControl.OrdinaryThemeResourceKeys.ToList(), "ControlBackgroundBrush");
            CollectionAssert.DoesNotContain(SavedLayoutPreviewControl.OrdinaryThemeResourceKeys.ToList(), "SecondaryTextBrush");

            string source = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml.cs")));
            Assert.AreEqual(
                1,
                Regex.Matches(source, @"(?<![A-Za-z0-9_])SetResourceReference(?![A-Za-z0-9_])").Count,
                "SavedLayoutPreviewControl must have exactly one direct SetResourceReference call.");
            Assert.AreEqual(
                1,
                Regex.Matches(
                    source,
                    @"private\s+static\s+void\s+SetOrdinaryThemeResource\s*\([^)]*\)\s*\{[^{}]*\.\s*SetResourceReference\s*\(",
                    RegexOptions.Singleline).Count,
                "The sole direct SetResourceReference call must be inside SetOrdinaryThemeResource.");

            StringAssert.Contains(source, "SetOrdinaryThemeResource(DiagramBorder, Border.BorderBrushProperty, OrdinaryThemeResource.Border);");
            StringAssert.Contains(source, "SetOrdinaryThemeResource(tile, Border.BorderBrushProperty, OrdinaryThemeResource.Border);");
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Controls", "SavedLayoutPreviewControl.xaml")));
            StringAssert.Contains(xaml, "BorderThickness=\"1\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DetailsBorderBrush_ValidMinimalCustomThemeWithoutTertiary_FallsBackAndSwitchesLive()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var resources = new ResourceDictionary();
                    var packagedBorder = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                    var packagedTertiary = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                    var customBorder = new SolidColorBrush(Color.FromRgb(20, 70, 120));
                    var customTertiary = new SolidColorBrush(Color.FromRgb(140, 80, 30));

                    static ResourceDictionary CreateMinimalTheme(Brush border, Brush tertiary = null)
                    {
                        var theme = new ResourceDictionary
                        {
                            ["WindowBackgroundBrush"] = new SolidColorBrush(Colors.Black),
                            ["PrimaryTextBrush"] = new SolidColorBrush(Colors.White),
                            ["ContentBackgroundBrush"] = new SolidColorBrush(Colors.Black),
                            ["BorderBrush"] = border,
                            ["ButtonBackgroundBrush"] = new SolidColorBrush(Colors.White),
                            ["ButtonForegroundBrush"] = new SolidColorBrush(Colors.Black)
                        };
                        if (tertiary != null)
                            theme["TertiaryTextBrush"] = tertiary;
                        return theme;
                    }

                    var packaged = CreateMinimalTheme(packagedBorder, packagedTertiary);
                    var customWithoutTertiary = CreateMinimalTheme(customBorder);
                    var customWithTertiary = CreateMinimalTheme(customBorder, customTertiary);

                    var requiredKeysField = typeof(ThemeStore).GetField(
                        "RequiredThemeKeys", BindingFlags.NonPublic | BindingFlags.Static);
                    Assert.IsNotNull(requiredKeysField);
                    var requiredKeys = (string[])requiredKeysField.GetValue(null);
                    Assert.AreEqual(6, requiredKeys.Length);
                    Assert.IsTrue(requiredKeys.All(customWithoutTertiary.Contains));
                    Assert.IsFalse(customWithoutTertiary.Contains("TertiaryTextBrush"));

                    ThemeService.ApplyDetailsBorderBrushOwnership(resources, packaged);
                    Assert.AreSame(packagedTertiary, resources[ThemeService.DetailsBorderBrushKey]);

                    ThemeService.ApplyDetailsBorderBrushOwnership(resources, customWithoutTertiary);
                    Assert.AreSame(customBorder, resources[ThemeService.DetailsBorderBrushKey]);

                    ThemeService.ApplyDetailsBorderBrushOwnership(resources, customWithTertiary);
                    Assert.AreSame(customTertiary, resources[ThemeService.DetailsBorderBrushKey]);

                    ThemeService.ApplyDetailsBorderBrushOwnership(resources, packaged);
                    Assert.AreSame(packagedTertiary, resources[ThemeService.DetailsBorderBrushKey]);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw failure;
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

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyButtonForegroundOwnership_PackagedCustomPackaged_TransfersPrimaryOwnership()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var resources = new ResourceDictionary();
                    var customForeground = new SolidColorBrush(Color.FromRgb(31, 79, 127));
                    var custom = new ResourceDictionary
                    {
                        ["ButtonBackgroundBrush"] = new SolidColorBrush(Colors.White),
                        ["ButtonForegroundBrush"] = customForeground
                    };
                    resources.MergedDictionaries.Add(custom);
                    var element = new Border { Resources = resources };

                    ThemeService.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: true, Colors.White);
                    Assert.IsTrue(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreEqual(Colors.Black, ((SolidColorBrush)element.TryFindResource("ButtonForegroundBrush")).Color);

                    ThemeService.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: false, Colors.White);
                    Assert.IsFalse(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreSame(customForeground, element.TryFindResource("ButtonForegroundBrush"));

                    ThemeService.ApplyButtonForegroundOwnership(resources, useAutomaticForeground: true, Colors.Black);
                    Assert.IsTrue(resources.Keys.Cast<object>().Contains("ButtonForegroundBrush"));
                    Assert.AreEqual(Colors.White, ((SolidColorBrush)element.TryFindResource("ButtonForegroundBrush")).Color);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw failure;
        }
    }
}
