using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchFocusVisualPolicyTests
    {
        [TestMethod]
        [TestCategory("Unit")]
        public void ItemContainerStyle_UsesGlobalFocusAuthorityWithoutChangingNavigationSemantics()
        {
            RunOnSta(() =>
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "/DisplayProfileManager;component/UI/Windows/QuickSwitchOverlayResources.xaml",
                        UriKind.Relative)
                };

                var style = (Style)dictionary["QuickSwitchItemContainerStyle"];
                var setters = style.Setters.OfType<Setter>().ToList();

                Assert.AreEqual(typeof(ListBoxItem), style.TargetType);
                Assert.IsFalse(setters.Any(setter => setter.Property.Name == "FocusVisualStyle"));
                Assert.IsFalse(setters.Any(setter => setter.Property.Name == "Focusable"));
                Assert.IsFalse(setters.Any(setter => setter.Property.Name == "IsTabStop"));
                Assert.IsFalse(setters.Any(setter => setter.Property.Name == "IsEnabled"));
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileList_GlobalFocusAuthorityPreservesKeyboardFocusAndSelection()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int listStart = xaml.IndexOf("<ListBox x:Name=\"ProfileList\"", StringComparison.Ordinal);
            int listTemplate = xaml.IndexOf("<ListBox.Template>", listStart, StringComparison.Ordinal);
            string declaration = xaml[listStart..listTemplate];

            Assert.IsFalse(declaration.Contains("FocusVisualStyle", StringComparison.Ordinal));
            Assert.IsFalse(declaration.Contains("Focusable=\"False\"", StringComparison.Ordinal));
            Assert.IsFalse(declaration.Contains("IsTabStop=\"False\"", StringComparison.Ordinal));
            StringAssert.Contains(code, "ProfileList.Focus();");
            StringAssert.Contains(code, "ProfileList.SelectedIndex");
            StringAssert.Contains(code, "QuickSwitchInputAction.MoveUp");
            StringAssert.Contains(code, "QuickSwitchInputAction.MoveDown");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ItemContainerStyle_HoverAndSelection_UseWinPGeometryWithoutCurrentAccent()
        {
            RunOnSta(() =>
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "/DisplayProfileManager;component/UI/Windows/QuickSwitchOverlayResources.xaml",
                        UriKind.Relative)
                };

                var style = (Style)dictionary["QuickSwitchItemContainerStyle"];
                var templateSetter = style.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == Control.TemplateProperty);
                var template = (ControlTemplate)templateSetter.Value;
                var triggers = template.Triggers;

                var hover = triggers
                    .OfType<Trigger>()
                    .Single(trigger => trigger.Property == UIElement.IsMouseOverProperty && Equals(trigger.Value, true));
                AssertDynamicResource(hover.Setters, Control.BackgroundProperty, "ControlHoverBackgroundBrush");

                var selected = triggers
                    .OfType<Trigger>()
                    .Single(trigger => trigger.Property == ListBoxItem.IsSelectedProperty && Equals(trigger.Value, true));
                AssertDynamicResource(selected.Setters, Control.BackgroundProperty, "SecondaryButtonBackgroundBrush");
                AssertDynamicResource(selected.Setters, Control.BorderBrushProperty, "PrimaryTextBrush");
                Assert.AreEqual(0, triggers.OfType<DataTrigger>().Count());

                var item = new ListBoxItem { Style = style, Content = "Profile" };
                item.Measure(new Size(300, 80));
                item.Arrange(new Rect(0, 0, 300, 80));
                item.ApplyTemplate();

                var itemBorder = (Border)template.FindName("ItemBorder", item);
                Assert.IsNotNull(itemBorder);
                Assert.AreEqual(new CornerRadius(4), itemBorder.CornerRadius);
                Assert.AreEqual(new Thickness(1), itemBorder.BorderThickness);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OverlayScrollViewerStyle_VerticalScrollbarOverlaysContentAtRightEdge()
        {
            RunOnSta(() =>
            {
                var dictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        "/DisplayProfileManager;component/UI/Themes/Base.xaml",
                        UriKind.Relative)
                };

                var style = (Style)dictionary["OverlayScrollViewerStyle"];
                var viewer = new ScrollViewer
                {
                    Style = style,
                    Width = 300,
                    Height = 120,
                    Content = new Border { Width = 280, Height = 500 }
                };

                viewer.Measure(new Size(300, 120));
                viewer.Arrange(new Rect(0, 0, 300, 120));
                viewer.ApplyTemplate();

                var template = viewer.Template;
                var scrollBar = (System.Windows.Controls.Primitives.ScrollBar)template.FindName("PART_VerticalScrollBar", viewer);
                Assert.IsNotNull(scrollBar);
                Assert.AreEqual(HorizontalAlignment.Right, scrollBar.HorizontalAlignment);
                Assert.AreEqual(1, System.Windows.Controls.Panel.GetZIndex(scrollBar));
            });
        }

        private static void AssertDynamicResource(
            SetterBaseCollection setters,
            DependencyProperty property,
            object expectedResourceKey)
        {
            var setter = setters
                .OfType<Setter>()
                .Single(candidate => candidate.Property == property);

            object value = setter.Value;
            object resourceKey = value is DynamicResourceExtension extension
                ? extension.ResourceKey
                : value?.GetType()
                    .GetProperty("ResourceKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(value);

            Assert.AreEqual(expectedResourceKey, resourceKey);
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

        private static void RunOnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null)
                throw error;
        }
    }
}
