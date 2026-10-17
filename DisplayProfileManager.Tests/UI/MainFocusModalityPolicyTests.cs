using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class MainFocusModalityPolicyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void MainDuplicateButton_VerticalMetricMatchesPrimaryWithoutReservedWidth()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "Name=\"DuplicateProfileButton\"");
            StringAssert.Contains(xaml, "Style=\"{StaticResource SecondaryButtonStyle}\"");
            StringAssert.Contains(xaml, "Padding=\"8,5\"");
            StringAssert.Contains(xaml, "Visibility=\"Collapsed\"");
            StringAssert.Contains(xaml, "<Setter Property=\"Padding\" Value=\"8,6\"/>");
            StringAssert.Contains(xaml, "<Setter Property=\"BorderThickness\" Value=\"1\"/>");

            var primaryPadding = new Thickness(8, 6, 8, 6);
            var secondaryPadding = new Thickness(8, 5, 8, 5);
            var primaryBorder = new Thickness(0);
            var secondaryBorder = new Thickness(1);

            Assert.AreEqual(primaryPadding.Top + primaryPadding.Bottom + primaryBorder.Top + primaryBorder.Bottom, secondaryPadding.Top + secondaryPadding.Bottom + secondaryBorder.Top + secondaryBorder.Bottom);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MainProfileHeader_SelectionVisibilityDoesNotChangeListY()
        {
            RunSta(() =>
            {
                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { MinHeight = 56, Padding = new Thickness(16, 16, 16, 12) };
                var headerGrid = new Grid();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var duplicate = CreateMetricButton("Duplicate", new Thickness(8, 5, 8, 5), new Thickness(1));
                var create = CreateMetricButton("Create", new Thickness(8, 6, 8, 6), new Thickness(0));
                duplicate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                create.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.AreEqual(create.DesiredSize.Height, duplicate.DesiredSize.Height, 0.001);

                duplicate.Visibility = Visibility.Collapsed;
                duplicate.Margin = new Thickness(0, 0, 8, 0);
                actions.Children.Add(duplicate);
                actions.Children.Add(create);
                headerGrid.Children.Add(actions);
                header.Child = headerGrid;
                Grid.SetRow(header, 0);

                var list = new Border();
                Grid.SetRow(list, 1);
                root.Children.Add(header);
                root.Children.Add(list);

                root.Measure(new Size(700, 500));
                root.Arrange(new Rect(0, 0, 700, 500));
                double collapsedY = list.TranslatePoint(new Point(0, 0), root).Y;

                duplicate.Visibility = Visibility.Visible;
                root.Measure(new Size(700, 500));
                root.Arrange(new Rect(0, 0, 700, 500));
                double visibleY = list.TranslatePoint(new Point(0, 0), root).Y;

                Assert.AreEqual(collapsedY, visibleY, 0.001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MainListSelectionBorder_RemainsGeometryInvariant()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "<Setter Property=\"BorderThickness\" Value=\"1\"/>");
            Assert.IsFalse(xaml.Contains(
                "<Setter TargetName=\"ItemBorder\" Property=\"BorderThickness\"",
                StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_ConcreteStyleBypassesObsoleteSystemKeyModel()
        {
            RunSta(() =>
            {
                var concreteFocusStyle = new Style();
                var buttonStyle = new Style(typeof(Button));
                buttonStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, concreteFocusStyle));

                var button = new Button { Style = buttonStyle };

                Assert.AreSame(concreteFocusStyle, button.FocusVisualStyle);
                Assert.AreEqual(DependencyProperty.UnsetValue, button.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty));
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_RealKeyboardFocusMovesAndNullsTargetWithoutHandlingPreview()
        {
            RunSta(() =>
            {
                InstallGenericFocusVisualPolicy();

                var concreteFocusStyle = new Style();
                var secondStyle = new Style(typeof(Button));
                secondStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, concreteFocusStyle));

                var first = new Button { Content = "First", Width = 80, Height = 28 };
                var second = new Button { Content = "Second", Width = 80, Height = 28, Style = secondStyle };
                var panel = new StackPanel();
                panel.Children.Add(first);
                panel.Children.Add(second);

                bool previewObserved = false;
                bool previewHandled = false;
                bool gotObserved = false;
                second.PreviewGotKeyboardFocus += (_, e) =>
                {
                    previewObserved = true;
                    previewHandled = e.Handled;
                };
                second.GotKeyboardFocus += (_, _) => gotObserved = true;

                var window = new Window
                {
                    Content = panel,
                    Width = 240,
                    Height = 120,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                };

                try
                {
                    window.Show();
                    window.Activate();

                    Assert.AreSame(first, Keyboard.Focus(first));
                    Assert.IsTrue(first.IsKeyboardFocused);

                    bool moved = first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                    Assert.IsTrue(moved);
                    Assert.IsTrue(second.IsKeyboardFocused);
                    Assert.IsTrue(previewObserved);
                    Assert.IsFalse(previewHandled);
                    Assert.IsTrue(gotObserved);
                    Assert.IsNull(second.FocusVisualStyle);
                    Assert.IsNull(second.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_ShowFocusVisualRoute_AddsAdornerForConcreteStyleAndNoneForLocalNull()
        {
            RunSta(() =>
            {
                InstallGenericFocusVisualPolicy();

                var button = new Button { Content = "Focus target", Width = 100, Height = 30 };
                var decorator = new AdornerDecorator { Child = button };
                var window = new Window
                {
                    Content = decorator,
                    Width = 240,
                    Height = 120,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                };

                Type keyboardNavigation = typeof(KeyboardNavigation);
                MethodInfo showFocusVisual = keyboardNavigation.GetMethod(
                    "ShowFocusVisual",
                    BindingFlags.Static | BindingFlags.NonPublic);
                PropertyInfo alwaysShowFocusVisual = keyboardNavigation.GetProperty(
                    "AlwaysShowFocusVisual",
                    BindingFlags.Static | BindingFlags.NonPublic);

                Assert.IsNotNull(showFocusVisual);
                Assert.IsNotNull(alwaysShowFocusVisual);

                bool originalAlwaysShow = (bool)alwaysShowFocusVisual.GetValue(null);
                try
                {
                    window.Show();
                    window.Activate();
                    Assert.AreSame(button, Keyboard.Focus(button));
                    Assert.IsTrue(button.IsKeyboardFocused);
                    Assert.IsNull(button.FocusVisualStyle);

                    alwaysShowFocusVisual.SetValue(null, true);

                    button.FocusVisualStyle = CreateConcreteFocusVisualStyle();
                    showFocusVisual.Invoke(null, null);

                    AdornerLayer layer = AdornerLayer.GetAdornerLayer(button);
                    Assert.IsNotNull(layer);
                    Adorner[] visibleAdorners = layer.GetAdorners(button);
                    Assert.IsNotNull(visibleAdorners);
                    Assert.IsTrue(visibleAdorners.Length > 0);

                    button.FocusVisualStyle = null;
                    showFocusVisual.Invoke(null, null);

                    Adorner[] suppressedAdorners = layer.GetAdorners(button);
                    Assert.IsTrue(suppressedAdorners == null || suppressedAdorners.Length == 0);
                }
                finally
                {
                    alwaysShowFocusVisual.SetValue(null, originalAlwaysShow);
                    window.Close();
                }
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_RoutedTargetsCoverRepresentativeFrameworkElements()
        {
            RunSta(() =>
            {
                InstallGenericFocusVisualPolicy();

                FrameworkElement[] targets =
                [
                    new Button(),
                    new CheckBox(),
                    new RadioButton(),
                    new ListBoxItem(),
                    new ComboBox()
                ];

                foreach (FrameworkElement target in targets)
                {
                    var concreteFocusStyle = new Style();
                    target.FocusVisualStyle = concreteFocusStyle;
                    target.ClearValue(FrameworkElement.FocusVisualStyleProperty);

                    var style = new Style(target.GetType());
                    style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, concreteFocusStyle));
                    target.Style = style;
                    Assert.AreSame(concreteFocusStyle, target.FocusVisualStyle);

                    var args = new KeyboardFocusChangedEventArgs(
                        Keyboard.PrimaryDevice,
                        Environment.TickCount,
                        null,
                        target)
                    {
                        RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent,
                        Source = target
                    };

                    target.RaiseEvent(args);

                    Assert.IsFalse(args.Handled, target.GetType().Name);
                    Assert.IsNull(target.FocusVisualStyle, target.GetType().Name);
                    Assert.IsNull(target.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty), target.GetType().Name);
                }
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_LocalNullSurvivesLaterConcreteStyleAssignment()
        {
            RunSta(() =>
            {
                InstallGenericFocusVisualPolicy();

                var firstConcrete = new Style();
                var firstStyle = new Style(typeof(Button));
                firstStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, firstConcrete));

                var button = new Button { Style = firstStyle };
                RaisePreviewGotKeyboardFocus(button);

                var secondConcrete = new Style();
                var secondStyle = new Style(typeof(Button));
                secondStyle.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, secondConcrete));
                button.Style = secondStyle;

                Assert.IsNull(button.FocusVisualStyle);
                Assert.IsNull(button.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty));
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GenericFocusPolicy_CoversFrameworkContentElementWithoutHandlingFocusEvent()
        {
            RunSta(() =>
            {
                InstallGenericFocusVisualPolicy();

                var concreteFocusStyle = new Style();
                var hyperlink = new Hyperlink { FocusVisualStyle = concreteFocusStyle };
                hyperlink.ClearValue(FrameworkContentElement.FocusVisualStyleProperty);

                var style = new Style(typeof(Hyperlink));
                style.Setters.Add(new Setter(FrameworkContentElement.FocusVisualStyleProperty, concreteFocusStyle));
                hyperlink.Style = style;
                Assert.AreSame(concreteFocusStyle, hyperlink.FocusVisualStyle);

                var args = new KeyboardFocusChangedEventArgs(
                    Keyboard.PrimaryDevice,
                    Environment.TickCount,
                    null,
                    hyperlink)
                {
                    RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent,
                    Source = hyperlink
                };

                hyperlink.RaiseEvent(args);

                Assert.IsFalse(args.Handled);
                Assert.IsNull(hyperlink.FocusVisualStyle);
                Assert.IsNull(hyperlink.ReadLocalValue(FrameworkContentElement.FocusVisualStyleProperty));
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GlobalFocusVisualAuthority_RetiresModifierDrivenLocalCueOwnership()
        {
            string appXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "App.xaml")));
            string appCode = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "App.xaml.cs")));
            string baseXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Themes", "Base.xaml")));
            string policy = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Presentation", "GenericFocusVisualPolicy.cs")));
            string behaviorPath = FindRepositoryPath(
                Path.Combine("DisplayProfileManager",  "UI", "Presentation", "KeyboardFocusCueBehavior.cs"));

            Assert.IsFalse(appXaml.Contains("SystemParameters.FocusVisualStyleKey", StringComparison.Ordinal));
            StringAssert.Contains(appCode, "GenericFocusVisualPolicy.Install();");
            StringAssert.Contains(policy, "Keyboard.PreviewGotKeyboardFocusEvent");
            StringAssert.Contains(policy, "typeof(FrameworkElement)");
            StringAssert.Contains(policy, "typeof(FrameworkContentElement)");
            StringAssert.Contains(policy, "handledEventsToo: true");
            Assert.IsFalse(policy.Contains("e.Handled =", StringComparison.Ordinal));
            Assert.IsFalse(baseXaml.Contains("KeyboardFocusCueBehavior", StringComparison.Ordinal));
            Assert.IsFalse(baseXaml.Contains("KeyboardFocusCue", StringComparison.Ordinal));
            Assert.IsFalse(baseXaml.Contains("FocusVisualStyle\" Value=\"{x:Null}", StringComparison.Ordinal));
            Assert.IsFalse(File.Exists(behaviorPath));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GlobalFocusVisualAuthority_PreservesRepresentativeKeyboardSemantics()
        {
            RunSta(() =>
            {
                Control[] controls =
                [
                    new Button(),
                    new CheckBox(),
                    new RadioButton(),
                    new ListBox(),
                    new ListBoxItem()
                ];

                foreach (Control control in controls)
                {
                    Assert.IsTrue(control.Focusable, control.GetType().Name);
                }
            });

            string baseXaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Themes", "Base.xaml")));
            int checkStart = baseXaml.IndexOf("<Style TargetType=\"CheckBox\">", StringComparison.Ordinal);
            int radioStart = baseXaml.IndexOf("<Style TargetType=\"RadioButton\">", StringComparison.Ordinal);
            int comboItemStart = baseXaml.IndexOf("<!-- ComboBox Item Style -->", radioStart, StringComparison.Ordinal);
            string checkAndRadio = baseXaml[checkStart..comboItemStart];

            Assert.AreEqual(2, Count(checkAndRadio, "RecognizesAccessKey=\"True\""));
            Assert.IsFalse(checkAndRadio.Contains("Focusable=\"False\"", StringComparison.Ordinal));
            Assert.IsFalse(checkAndRadio.Contains("IsTabStop=\"False\"", StringComparison.Ordinal));
            Assert.IsFalse(checkAndRadio.Contains("KeyboardNavigation.TabNavigation", StringComparison.Ordinal));
            Assert.IsFalse(checkAndRadio.Contains("KeyboardNavigation.DirectionalNavigation=\"None\"", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GlobalFocusVisualAuthority_DoesNotChangeCheckBoxOrRadioDesiredSize()
        {
            RunSta(() =>
            {
                ResourceDictionary dictionary = LoadBaseTheme();

                AssertDesiredSizeInvariant(new CheckBox { Content = "_Check" }, (Style)dictionary[typeof(CheckBox)]);
                AssertDesiredSizeInvariant(new RadioButton { Content = "_Radio" }, (Style)dictionary[typeof(RadioButton)]);
            });
        }

        private static Button CreateMetricButton(string content, Thickness padding, Thickness borderThickness)
        {
            var button = new Button
            {
                Content = content,
                FontSize = 14,
                Padding = padding,
                BorderThickness = borderThickness,
                Template = (ControlTemplate)XamlReader.Parse(
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\">" +
                    "<Border BorderThickness=\"{TemplateBinding BorderThickness}\" Padding=\"{TemplateBinding Padding}\">" +
                    "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                    "</Border></ControlTemplate>")
            };
            return button;
        }

        private static ResourceDictionary LoadBaseTheme()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Themes", "Base.xaml")));
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        private static void AssertDesiredSizeInvariant(ContentControl control, Style style)
        {
            control.Style = style;
            control.FocusVisualStyle = new Style();
            control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size withConcreteFocusStyle = control.DesiredSize;

            control.FocusVisualStyle = null;
            control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size withSuppressedGenericFocusStyle = control.DesiredSize;

            Assert.AreEqual(withConcreteFocusStyle.Width, withSuppressedGenericFocusStyle.Width, 0.001);
            Assert.AreEqual(withConcreteFocusStyle.Height, withSuppressedGenericFocusStyle.Height, 0.001);
        }

        private static Style CreateConcreteFocusVisualStyle()
        {
            const string xaml =
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type Control}\">" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"{x:Type Control}\">" +
                "<Border BorderBrush=\"Black\" BorderThickness=\"1\"/>" +
                "</ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private static void InstallGenericFocusVisualPolicy()
        {
            Type policy = typeof(App).Assembly.GetType(
                "DisplayProfileManager.UI.Presentation.GenericFocusVisualPolicy",
                throwOnError: true);
            MethodInfo install = policy.GetMethod(
                "Install",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(install);
            install.Invoke(null, null);
        }

        private static void RaisePreviewGotKeyboardFocus(FrameworkElement target)
        {
            var args = new KeyboardFocusChangedEventArgs(
                Keyboard.PrimaryDevice,
                Environment.TickCount,
                null,
                target)
            {
                RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent,
                Source = target
            };

            target.RaiseEvent(args);
            Assert.IsFalse(args.Handled);
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void RunSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null) throw new AssertFailedException(failure.ToString());
        }

        private static string FindRepositoryPath(string relativePath)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, relativePath);
                    if (Directory.Exists(Path.GetDirectoryName(candidate)))
                    {
                        return candidate;
                    }
                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException($"Repository path not found: {relativePath}");
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
