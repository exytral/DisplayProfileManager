using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;

using DisplayProfileManager.UI.Controls;
using DisplayProfileManager.UI.Windows;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class ThemeRefreshPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void SetThemeResourceReference_ResourceChanges_ExistingElementResolvesReplacement()
        {
            Brush initial = null;
            Brush replacement = null;
            Brush initialResolved = null;
            Brush replacementResolved = null;
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    initial = new SolidColorBrush(Colors.Black);
                    replacement = new SolidColorBrush(Colors.White);
                    var text = new TextBlock();
                    text.Resources["TestThemeBrush"] = initial;
                    DisplaySettingControl.SetThemeResourceReference(text, TextBlock.ForegroundProperty, "TestThemeBrush");
                    initialResolved = text.Foreground;
                    text.Resources["TestThemeBrush"] = replacement;
                    replacementResolved = text.Foreground;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.IsNull(failure);
            Assert.AreSame(initial, initialResolved);
            Assert.AreSame(replacement, replacementResolved);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UpdateNoticeForeground_UsesPrimaryFallbackAndReevaluatesAvailability()
        {
            var resources = new Dictionary<object, object>
            {
                ["PrimaryTextBrush"] = new SolidColorBrush(Colors.White),
            };

            object Lookup(object key) => resources.TryGetValue(key, out var value) ? value : null;

            Assert.AreEqual(
                "PrimaryTextBrush",
                MainWindow.ResolveUpdateNoticeForegroundResourceKey(Lookup));

            resources["SuccessButtonBackgroundBrush"] = new SolidColorBrush(Colors.Green);
            Assert.AreEqual(
                "SuccessButtonBackgroundBrush",
                MainWindow.ResolveUpdateNoticeForegroundResourceKey(Lookup));

            resources.Remove("SuccessButtonBackgroundBrush");
            Assert.AreEqual(
                "PrimaryTextBrush",
                MainWindow.ResolveUpdateNoticeForegroundResourceKey(Lookup));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DisplaySettingGeneratedPresentation_UsesLiveThemeResources()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager", "UI", "Controls", "DisplaySettingControl.cs")));

            StringAssert.Contains(code, "Control.ForegroundProperty, \"PrimaryTextBrush\"");
            StringAssert.Contains(code, "ComboBox.BorderBrushProperty, \"ComboBoxBorderBrush\"");
            StringAssert.Contains(code, "new DynamicResourceExtension(\"ButtonBackgroundBrush\")");
            StringAssert.Contains(code, "new DynamicResourceExtension(\"ButtonHoverBackgroundBrush\")");
            StringAssert.Contains(code, "new DynamicResourceExtension(\"ComboBoxBackgroundBrush\")");
            StringAssert.Contains(code, "new DynamicResourceExtension(\"ComboBoxHoverBackgroundBrush\")");
            StringAssert.Contains(code, "Border.BackgroundProperty, \"ContentBackgroundBrush\"");
            StringAssert.Contains(code, "Border.BorderBrushProperty, \"BorderBrush\"");
            StringAssert.Contains(code, "Border.BackgroundProperty, \"ControlHoverBackgroundBrush\"");
            Assert.IsFalse(code.Contains("var primaryFg = (Brush)System.Windows.Application.Current.Resources[\"PrimaryTextBrush\"]", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("BorderBrush = (Brush)System.Windows.Application.Current.Resources[\"ComboBoxBorderBrush\"]", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("var bg = (Brush)System.Windows.Application.Current.Resources[\"ButtonBackgroundBrush\"]", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("var bg = (Brush)System.Windows.Application.Current.Resources[\"ComboBoxBackgroundBrush\"]", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("var bg = (Brush)System.Windows.Application.Current.Resources[\"ContentBackgroundBrush\"]", StringComparison.Ordinal));
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (File.Exists(candidate)) { return candidate; }
                    directory = directory.Parent;
                }
            }
            throw new FileNotFoundException($"Repository file not found: {relativePath}");
        }
    }
}
