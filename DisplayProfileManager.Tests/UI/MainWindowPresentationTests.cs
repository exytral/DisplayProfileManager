using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class MainWindowPresentationTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void MainMinimumGeometry_BalancesPanesAndKeepsProfilesFlexible()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "MinWidth=\"700\"");
            StringAssert.Contains(xaml, "<Grid Grid.Row=\"1\" Margin=\"16,16,16,0\">");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"*\"/>");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"16\"/>");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"326\"/>");
            StringAssert.Contains(xaml, "Margin=\"0,2,-5,2\"");
            StringAssert.Contains(xaml, "<ScrollViewer Grid.Row=\"1\" Padding=\"16,8,17,16\" Margin=\"0,0,-1,0\" VerticalScrollBarVisibility=\"Auto\" Style=\"{StaticResource OverlayScrollViewerStyle}\" presentation:SmoothScrollBehavior.IsEnabled=\"True\">");
            Assert.IsFalse(xaml.Contains("<ColumnDefinition Width=\"320\"/>", StringComparison.Ordinal));

            var document = XDocument.Parse(xaml);
            var ns = document.Root.Name.Namespace;
            var contentArea = document.Descendants(ns + "Grid")
                .Single(grid =>
                    (string)grid.Attribute("Grid.Row") == "1" &&
                    grid.Elements(ns + "Grid").Any(child => child.Element(ns + "Grid.ColumnDefinitions") != null));
            var contentGrid = contentArea.Elements(ns + "Grid")
                .Single(grid => grid.Element(ns + "Grid.ColumnDefinitions") != null);
            var columns = contentGrid.Element(ns + "Grid.ColumnDefinitions")
                .Elements(ns + "ColumnDefinition")
                .ToArray();

            double minimumWidth = double.Parse((string)document.Root.Attribute("MinWidth"), CultureInfo.InvariantCulture);
            double[] margin = ((string)contentArea.Attribute("Margin"))
                .Split(',')
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            double gap = double.Parse((string)columns[1].Attribute("Width"), CultureInfo.InvariantCulture);
            double detailsWidth = double.Parse((string)columns[2].Attribute("Width"), CultureInfo.InvariantCulture);
            double starWidthAtMinimum = minimumWidth - margin[0] - margin[2] - gap - detailsWidth;

            Assert.AreEqual(detailsWidth, starWidthAtMinimum);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DetailsCards_UseStrongOneDipThemeBorderWithHighContrastAuthority()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            StringAssert.Contains(code, "ApplyDetailsCardBorder(disabledBorder);");
            StringAssert.Contains(code, "ApplyDetailsCardBorder(enabledBorder);");
            StringAssert.Contains(code, "border.BorderThickness = new Thickness(1);");
            StringAssert.Contains(code, "SystemParameters.HighContrast ? SystemColors.WindowTextBrushKey : ThemeService.DetailsBorderBrushKey");
            Assert.IsFalse(code.Contains(
                "SystemParameters.HighContrast ? SystemColors.WindowTextBrushKey : \"BorderBrush\"",
                StringComparison.Ordinal));
            Assert.AreEqual(3, code.Split("ApplyDetailsCardBorder(", StringSplitOptions.None).Length - 1);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void GeneratedMainLayout_RefreshesOnThemeChangeRegardlessOfCustomIcon()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            StringAssert.Contains(code, "_themeService.ThemeChanged += OnThemeChanged;");
            StringAssert.Contains(code, "Dispatcher.Invoke(() => OnThemeChanged(sender, e));");
            StringAssert.Contains(code, "RefreshGeneratedProfileIcons();");
            StringAssert.Contains(code, "FindVisualChildByName<Image>(item, \"ProfileLayoutImage\")");
            Assert.IsFalse(code.Contains("if (!string.IsNullOrWhiteSpace(viewModel.Profile.Icon))", StringComparison.Ordinal));

            StringAssert.Contains(xaml, "x:Name=\"ProfileLayoutImage\"");
            StringAssert.Contains(xaml, "Width=\"32\"");
            StringAssert.Contains(xaml, "Binding Profile, Converter={StaticResource SavedLayoutThumbnailConverter}, ConverterParameter=32");
            StringAssert.Contains(xaml, "Binding Profile.Icon, Converter={StaticResource ProfileIconConverter}, ConverterParameter=16");
            StringAssert.Contains(code, "ProfileIconProvider.LoadImageSource(profile.Icon, 18)");
            Assert.IsFalse(code.Contains("LoadProfileImageSource", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GeneratedProfileDetails_UseLiveThemeResourceReferences()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager", "UI", "Windows", "MainWindow.xaml.cs")));

            StringAssert.Contains(code, "ResolveUpdateNoticeForegroundResourceKey(link.TryFindResource)");
            StringAssert.Contains(code, "tryFindResource(\"SuccessButtonBackgroundBrush\") != null");
            StringAssert.Contains(code, "? \"SuccessButtonBackgroundBrush\"");
            StringAssert.Contains(code, ": \"PrimaryTextBrush\"");
            StringAssert.Contains(code, "if (StatusTextBlock.Inlines.FirstInline is System.Windows.Documents.Hyperlink updateLink)");
            StringAssert.Contains(code, "ApplyUpdateNoticeForeground(updateLink);");
            Assert.IsFalse(code.Contains("link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, \"SuccessButtonBackgroundBrush\")", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("Foreground = (Brush)(TryFindResource(\"SuccessButtonBackgroundBrush\")", StringComparison.Ordinal));
            StringAssert.Contains(code, "TextBlock.ForegroundProperty, \"SecondaryTextBrush\"");
            StringAssert.Contains(code, "TextBlock.ForegroundProperty, \"TertiaryTextBrush\"");
            StringAssert.Contains(code, "TextBlock.ForegroundProperty, \"ButtonBackgroundBrush\"");
            StringAssert.Contains(code, "TextBlock.ForegroundProperty, profile.HotkeyConfig.IsEnabled");
            Assert.IsFalse(code.Contains("Foreground = (SolidColorBrush)FindResource(\"SecondaryTextBrush\")", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("Foreground = (SolidColorBrush)FindResource(\"TertiaryTextBrush\")", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("Foreground = (SolidColorBrush)FindResource(\"ButtonBackgroundBrush\")", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("var statusColor =", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileListMetadata_UsesSavedEnabledTotalBinding()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "Text=\"{Binding DisplayCountText}\"");
            Assert.IsFalse(xaml.Contains("DisplaySettings.Count", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyAndRefreshButtons_DoNotDeclareToolTips()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            Assert.IsFalse(xaml.Contains("ToolTip=\"Apply\"", StringComparison.Ordinal));
            Assert.IsFalse(xaml.Contains("ToolTip=\"Refresh\"", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ApplyBusyState_PreservesProfileListEnabledPresentation()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            StringAssert.Contains(code, "ProfilesListBox.IsHitTestVisible = !isBusy;");
            Assert.IsFalse(code.Contains("ProfilesListBox.IsEnabled = !isBusy;", StringComparison.Ordinal));
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileList_SeparatesLayoutAndCustomIconWhilePreservingCurrentRowContent()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"44\"/>");
            StringAssert.Contains(xaml, "x:Name=\"ProfileLayoutImage\"");
            StringAssert.Contains(xaml, "Width=\"32\"");
            int layoutStart = xaml.IndexOf("x:Name=\"ProfileLayoutImage\"", StringComparison.Ordinal);
            int contentStart = xaml.IndexOf("<Grid Grid.Column=\"1\">", layoutStart, StringComparison.Ordinal);
            string layoutBlock = xaml[layoutStart..contentStart];
            StringAssert.Contains(layoutBlock, "HorizontalAlignment=\"Left\"");
            Assert.IsFalse(layoutBlock.Contains("TranslateTransform", StringComparison.Ordinal));
            StringAssert.Contains(xaml, "x:Name=\"ProfileIconImage\"");
            StringAssert.Contains(xaml, "Text=\"{Binding Description}\"");
            StringAssert.Contains(xaml, "Trigger Property=\"Source\" Value=\"{x:Null}\"");
            StringAssert.Contains(xaml, "Setter Property=\"Visibility\" Value=\"Collapsed\"");

            int name = xaml.IndexOf("Text=\"{Binding Name}\"", StringComparison.Ordinal);
            int defaultLabel = xaml.IndexOf("Text=\"Default\"", name, StringComparison.Ordinal);
            int activeLabel = xaml.IndexOf("Text=\"Active\"", name, StringComparison.Ordinal);
            int description = xaml.IndexOf("Text=\"{Binding Description}\"", name, StringComparison.Ordinal);

            Assert.IsTrue(name >= 0);
            Assert.IsTrue(defaultLabel > name && defaultLabel < description);
            Assert.IsTrue(activeLabel > name && activeLabel < description);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SettingsAndProfileEditor_Pre503UiContracts_ArePinned()
        {
            string settingsXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml")));
            string settingsCode = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml.cs")));
            string profileXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml")));
            string profileCode = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml.cs")));

            const string profileHotkeyCopy = "Profile hotkeys are configured in the Profile Editor. Configured shortcuts reserve their key combinations even while disabled.";

            Assert.IsFalse(settingsXaml.Contains("QuickSwitchAcrylic", StringComparison.Ordinal));
            Assert.IsFalse(settingsXaml.Contains("Acrylic background", StringComparison.Ordinal));
            Assert.IsTrue(settingsXaml.IndexOf(profileHotkeyCopy, StringComparison.Ordinal) >= 0);

            string AttributeValue(XElement element, string localName) =>
                element.Attributes()
                    .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
                    ?.Value ?? string.Empty;

            void AssertQuickSwitchSettingsContract(string xaml)
            {
                var document = XDocument.Parse(xaml);

                XElement[] globalHotkeysHeaders = document.Descendants()
                    .Where(element =>
                        element.Name.LocalName == "TextBlock" &&
                        AttributeValue(element, "Text") == "Global Hotkeys")
                    .ToArray();
                Assert.AreEqual(1, globalHotkeysHeaders.Length);

                XElement globalHotkeysHeader = globalHotkeysHeaders[0];
                XElement globalHotkeysPanel = globalHotkeysHeader.Parent;
                Assert.IsNotNull(globalHotkeysPanel);
                Assert.AreEqual("StackPanel", globalHotkeysPanel.Name.LocalName);

                XElement[] children = globalHotkeysPanel.Elements().ToArray();
                int globalHotkeysIndex = Array.IndexOf(children, globalHotkeysHeader);

                XElement[] quickSwitchHeaders = children
                    .Skip(globalHotkeysIndex + 1)
                    .Where(element =>
                        element.Name.LocalName == "TextBlock" &&
                        AttributeValue(element, "Text") == "Quick Switch")
                    .ToArray();
                Assert.AreEqual(1, quickSwitchHeaders.Length);

                XElement quickSwitchHeader = quickSwitchHeaders[0];
                int quickSwitchHeaderIndex = Array.IndexOf(children, quickSwitchHeader);
                Assert.IsTrue(quickSwitchHeaderIndex > globalHotkeysIndex);
                Assert.AreEqual("{StaticResource LabelTextBlockStyle}", AttributeValue(quickSwitchHeader, "Style"));
                Assert.AreEqual("0,16,0,0", AttributeValue(quickSwitchHeader, "Margin"));

                string headingVisibility = AttributeValue(quickSwitchHeader, "Visibility");
                Assert.IsFalse(
                    string.Equals(headingVisibility, "Hidden", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(headingVisibility, "Collapsed", StringComparison.OrdinalIgnoreCase));

                XElement[] enabledCheckBoxes = children
                    .Where(element =>
                        element.Name.LocalName == "CheckBox" &&
                        AttributeValue(element, "Name") == "QuickSwitchEnabledCheckBox")
                    .ToArray();
                Assert.AreEqual(1, enabledCheckBoxes.Length);

                XElement enabledCheckBox = enabledCheckBoxes[0];
                int enabledCheckBoxIndex = Array.IndexOf(children, enabledCheckBox);
                Assert.AreEqual(quickSwitchHeaderIndex + 1, enabledCheckBoxIndex);
                Assert.AreEqual("0,8,0,8", AttributeValue(enabledCheckBox, "Margin"));
                Assert.AreEqual(
                    "QuickSwitchEnabledCheckBox_Changed",
                    AttributeValue(enabledCheckBox, "Checked"));
                Assert.AreEqual(
                    "QuickSwitchEnabledCheckBox_Changed",
                    AttributeValue(enabledCheckBox, "Unchecked"));

                XElement[] configuredHotkeysHeaders = children
                    .Where(element =>
                        element.Name.LocalName == "TextBlock" &&
                        AttributeValue(element, "Text") == "Configured Hotkeys")
                    .ToArray();
                Assert.AreEqual(1, configuredHotkeysHeaders.Length);

                XElement configuredHotkeysHeader = configuredHotkeysHeaders[0];
                int configuredHotkeysIndex = Array.IndexOf(children, configuredHotkeysHeader);
                Assert.IsTrue(configuredHotkeysIndex > enabledCheckBoxIndex);
                Assert.IsFalse(children.Any(element =>
                    element.Name.LocalName == "TextBlock" &&
                    AttributeValue(element, "Text") == "Configured Hotkeys:"));

                const string quickSwitchCopy =
                    "Opens a profile switcher overlay with the configured global hotkey.";
                XElement[] explanationBlocks = children
                    .Skip(enabledCheckBoxIndex + 1)
                    .Take(configuredHotkeysIndex - enabledCheckBoxIndex - 1)
                    .Where(element =>
                        element.Name.LocalName == "TextBlock" &&
                        AttributeValue(element, "Text") == quickSwitchCopy)
                    .ToArray();
                Assert.AreEqual(1, explanationBlocks.Length);
            }

            AssertQuickSwitchSettingsContract(settingsXaml);


            StringAssert.Contains(settingsXaml, "presentation:SmoothScrollBehavior.WheelRouting=\"SelfThenAncestor\"");
            Assert.IsFalse(settingsXaml.Contains("presentation:SmoothScrollBehavior.WheelRouting=\"AncestorOnly\"", StringComparison.Ordinal));
            Assert.IsFalse(settingsXaml.Contains("Name=\"MinimizeButton\"", StringComparison.Ordinal));
            Assert.IsFalse(settingsXaml.Contains("Click=\"MinimizeButton_Click\"", StringComparison.Ordinal));
            StringAssert.Contains(settingsXaml, "Name=\"CloseButton\"");
            Assert.IsFalse(settingsCode.Contains("MinimizeButton_Click", StringComparison.Ordinal));

            int descriptionStart = profileXaml.IndexOf("<!-- Description -->", StringComparison.Ordinal);
            int iconStart = profileXaml.IndexOf("<!-- Icon", descriptionStart, StringComparison.Ordinal);
            string descriptionSection = profileXaml[descriptionStart..iconStart];
            StringAssert.Contains(descriptionSection, "<RowDefinition Height=\"4\"/>");
            Assert.IsFalse(descriptionSection.Contains("<RowDefinition Height=\"5\"/>", StringComparison.Ordinal));

            int outputStart = profileXaml.IndexOf("Name=\"ApplyOutputDeviceCheckBox\"", StringComparison.Ordinal);
            int outputEnd = profileXaml.IndexOf("Name=\"OutputDeviceComboBox\"", outputStart, StringComparison.Ordinal);
            string outputCheckBox = profileXaml[outputStart..outputEnd];
            StringAssert.Contains(outputCheckBox, "Margin=\"0,0,8,0\"");
            Assert.IsFalse(outputCheckBox.Contains("Margin=\"0,0,10,0\"", StringComparison.Ordinal));

            StringAssert.Contains(profileXaml, "presentation:SmoothScrollBehavior.WheelRouting=\"SelfThenAncestor\"");
            Assert.IsFalse(profileXaml.Contains("Name=\"MinimizeButton\"", StringComparison.Ordinal));
            Assert.IsFalse(profileXaml.Contains("Click=\"MinimizeButton_Click\"", StringComparison.Ordinal));
            StringAssert.Contains(profileXaml, "Name=\"CloseButton\"");
            Assert.IsFalse(profileCode.Contains("MinimizeButton_Click", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MainCommands_ImportRemovedAndDuplicatePreserved()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            Assert.IsFalse(xaml.Contains("Name=\"ImportButton\"", StringComparison.Ordinal));
            Assert.IsFalse(xaml.Contains("Click=\"ImportButton_Click\"", StringComparison.Ordinal));
            StringAssert.Contains(xaml, "Name=\"DuplicateProfileButton\"");
            StringAssert.Contains(xaml, "Content=\"Duplicate\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MainCodeBehind_ImportHandlerRemovedAndRefreshCopyIsNeutral()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            Assert.IsFalse(code.Contains("ImportButton_Click", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("Profiles and themes refreshed", StringComparison.Ordinal));
            StringAssert.Contains(code, "StatusTextBlock.Text = \"Refreshed\";");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DedicatedResourcesAndScrollbarIsolation_RemainAvailable()
        {
            string settingsXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml")));
            string editorXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "ProfileEditWindow.xaml")));
            string baseXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Themes", "Base.xaml")));

            StringAssert.Contains(settingsXaml, "Name=\"ImportThemeButton\"");
            StringAssert.Contains(settingsXaml, "Click=\"ImportThemeButton_Click\"");
            StringAssert.Contains(settingsXaml, "Padding=\"0,0,15,0\" Margin=\"16,16,1,16\" Style=\"{StaticResource OverlayScrollViewerStyle}\"");
            StringAssert.Contains(settingsXaml, "<ScrollViewer Style=\"{StaticResource OverlayScrollViewerStyle}\" VerticalScrollBarVisibility=\"Auto\" Padding=\"8\"");
            StringAssert.Contains(editorXaml, "Name=\"ImportIconButton\"");
            StringAssert.Contains(editorXaml, "Click=\"ImportIconButton_Click\"");
            StringAssert.Contains(editorXaml, "Padding=\"0,0,15,0\" Margin=\"16,16,1,0\" Style=\"{StaticResource OverlayScrollViewerStyle}\"");
            StringAssert.Contains(editorXaml, "MaxHeight=\"148\"");
            Assert.AreEqual(1, editorXaml.Split("Padding=\"2\"", StringSplitOptions.None).Length - 1);
            Assert.AreEqual(2, baseXaml.Split("Margin=\"0,2,-4,2\"", StringSplitOptions.None).Length - 1);
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
