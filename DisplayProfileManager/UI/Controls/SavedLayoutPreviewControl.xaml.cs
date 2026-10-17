using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.UI.Displays;
using DisplayProfileManager.UI.Themes;

namespace DisplayProfileManager.UI.Controls
{
    public partial class SavedLayoutPreviewControl : UserControl
    {
        private const double ProjectorInset = 12;

        internal enum OrdinaryThemeResource
        {
            ContentBackground,
            Border,
            PrimaryText,
            ButtonBackground,
            ButtonForeground
        }

        internal static IReadOnlyList<string> OrdinaryThemeResourceKeys { get; } =
            Array.AsReadOnly(Enum.GetValues<OrdinaryThemeResource>()
                .Select(GetOrdinaryThemeResourceKey)
                .ToArray());
        private SavedLayoutGeometry _geometry = SavedLayoutProjector.Build([]);
        private bool _systemParametersSubscribed;

        public static readonly DependencyProperty DisplaySettingsProperty =
            DependencyProperty.Register(
                nameof(DisplaySettings),
                typeof(IReadOnlyList<DisplaySetting>),
                typeof(SavedLayoutPreviewControl),
                new PropertyMetadata(null, OnDisplaySettingsChanged));

        public SavedLayoutPreviewControl()
        {
            InitializeComponent();
            Loaded += SavedLayoutPreviewControl_Loaded;
            Unloaded += SavedLayoutPreviewControl_Unloaded;
        }

        public IReadOnlyList<DisplaySetting> DisplaySettings
        {
            get => (IReadOnlyList<DisplaySetting>)GetValue(DisplaySettingsProperty);
            set => SetValue(DisplaySettingsProperty, value);
        }

        private static void OnDisplaySettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (SavedLayoutPreviewControl)d;
            control._geometry = SavedLayoutProjector.Build(control.DisplaySettings ?? []);
            control.RefreshProjection();
        }

        private void SavedLayoutPreviewControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_systemParametersSubscribed)
            {
                SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
                _systemParametersSubscribed = true;
            }

            RefreshProjection();
        }

        private void SavedLayoutPreviewControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_systemParametersSubscribed) return;
            SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
            _systemParametersSubscribed = false;
        }

        private void SystemParameters_StaticPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(SystemParameters.HighContrast))
                return;

            if (Dispatcher.CheckAccess())
                RefreshProjection();
            else
                Dispatcher.BeginInvoke(RefreshProjection);
        }

        private void DiagramBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshProjection();
        }

        private void RefreshProjection()
        {
            if (LayoutCanvas == null || DiagramBorder == null || SummaryTextBlock == null)
                return;

            ApplySurfaceBrushes();
            LayoutCanvas.Children.Clear();
            string summary = SavedLayoutPresentationPolicy.BuildExceptionalSummary(_geometry);
            SummaryTextBlock.Text = summary;
            SummaryTextBlock.Visibility = string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetHelpText(this, BuildHelpText(_geometry));

            if (_geometry.EnabledLayoutCount == 0)
            {
                AddStateMessage("No enabled displays in this saved layout");
                return;
            }

            if (!_geometry.HasLogicalBounds)
            {
                AddStateMessage("Saved layout preview unavailable");
                return;
            }

            var projection = SavedLayoutProjector.Fit(
                _geometry,
                LayoutCanvas.ActualWidth,
                LayoutCanvas.ActualHeight,
                ProjectorInset);

            foreach (var projected in projection.Footprints)
                AddFootprint(projected);
        }

        private void ApplySurfaceBrushes()
        {
            if (SystemParameters.HighContrast)
            {
                DiagramBorder.Background = SystemColors.WindowBrush;
                DiagramBorder.BorderBrush = SystemColors.WindowTextBrush;
                SummaryTextBlock.Foreground = SystemColors.WindowTextBrush;
                return;
            }

            SetOrdinaryThemeResource(DiagramBorder, Border.BackgroundProperty, OrdinaryThemeResource.ContentBackground);
            SetOrdinaryThemeResource(DiagramBorder, Border.BorderBrushProperty, OrdinaryThemeResource.Border);
            SetOrdinaryThemeResource(SummaryTextBlock, TextBlock.ForegroundProperty, OrdinaryThemeResource.PrimaryText);
        }

        private void AddFootprint(ProjectedLayoutFootprint projected)
        {
            var footprint = projected.Source;
            var tile = new Border
            {
                Width = projected.Width,
                Height = projected.Height,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                ClipToBounds = true
            };

            if (SystemParameters.HighContrast)
            {
                tile.Background = SystemColors.WindowBrush;
                tile.BorderBrush = SystemColors.WindowTextBrush;
            }
            else
            {
                SetOrdinaryThemeResource(tile, Border.BackgroundProperty, OrdinaryThemeResource.ContentBackground);
                SetOrdinaryThemeResource(tile, Border.BorderBrushProperty, OrdinaryThemeResource.Border);
            }

            var content = new Grid();
            var visibleNames = SavedLayoutPresentationPolicy.GetVisibleNames(footprint);
            double minimumHeight = visibleNames.Count > 1 ? 40 : 24;
            if (projected.Width >= 48 && projected.Height >= minimumHeight)
            {
                var names = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4)
                };

                for (int index = 0; index < visibleNames.Count; index++)
                {
                    var name = new TextBlock
                    {
                        Text = visibleNames[index],
                        FontSize = index == 0 ? 13 : 12,
                        FontWeight = index == 0 ? FontWeights.SemiBold : FontWeights.Medium,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = visibleNames[index],
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    SetTextBrush(name);
                    names.Children.Add(name);
                }

                content.Children.Add(names);
            }

            tile.Child = content;
            LayoutCanvas.Children.Add(tile);
            Canvas.SetLeft(tile, projected.X);
            Canvas.SetTop(tile, projected.Y);
        }

        private void AddStateMessage(string message)
        {
            var text = new TextBlock
            {
                Text = message,
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = Math.Max(0, LayoutCanvas.ActualWidth - 24)
            };
            SetTextBrush(text);
            LayoutCanvas.Children.Add(text);
            Canvas.SetLeft(text, 12);
            Canvas.SetTop(text, Math.Max(12, (LayoutCanvas.ActualHeight - 20) / 2));
        }

        private static string BuildHelpSummary(SavedLayoutGeometry geometry)
        {
            var parts = new List<string>
            {
                $"{geometry.EnabledLayoutCount} enabled {Plural(geometry.EnabledLayoutCount, "layout")}",
                $"{geometry.CloneGroupCount} clone {Plural(geometry.CloneGroupCount, "group")}",
                $"{geometry.DisabledDisplayCount} disabled not shown"
            };

            var primaryNames = geometry.Footprints
                .Where(footprint => footprint.IsPrimary)
                .Select(footprint => footprint.Name)
                .ToList();
            if (primaryNames.Count > 0)
                parts.Add($"Primary: {string.Join(", ", primaryNames)}");

            if (geometry.LayoutUnavailableCount > 0)
                parts.Add($"{geometry.LayoutUnavailableCount} {Plural(geometry.LayoutUnavailableCount, "layout")} unavailable");
            if (geometry.OverlapCount > 0)
                parts.Add("saved overlap");
            if (geometry.InconsistentCloneCount > 0)
                parts.Add("clone geometry differs");
            if (geometry.HasPrimaryStateWarning)
                parts.Add($"primary state warning ({geometry.PrimaryDisplayCount} saved)");

            return string.Join(" • ", parts);
        }

        private static string BuildHelpText(SavedLayoutGeometry geometry)
        {
            var items = geometry.Footprints.Select(footprint =>
            {
                string state = footprint.IsPrimary ? ", Primary" : string.Empty;
                if (footprint.IsCloneGroup) state += $", Clone ×{footprint.CloneMemberCount}";
                string members = footprint.IsCloneGroup
                    ? $", members: {string.Join(", ", footprint.Members.Select(member => $"{member.Name} ({MemberState(member)})"))}"
                    : string.Empty;
                string rotation = footprint.Rotation switch
                {
                    2 => ", rotation 90°",
                    3 => ", rotation 180°",
                    4 => ", rotation 270°",
                    _ => string.Empty
                };
                return $"{footprint.Name}{state}, position {footprint.X},{footprint.Y}, {footprint.Width} by {footprint.Height}{rotation}{members}";
            }).ToList();

            string summary = BuildHelpSummary(geometry);
            return items.Count == 0 ? summary : $"{summary}. {string.Join("; ", items)}";
        }

        private static string MemberState(SavedLayoutMember member)
        {
            string role = member.IsCloneSource ? "Source" : "Clone";
            string enabled = member.IsEnabled ? "enabled" : "disabled";
            string primary = member.IsPrimary ? ", Primary" : string.Empty;
            return $"{role}, {enabled}{primary}";
        }

        private void SetTextBrush(TextBlock text)
        {
            if (SystemParameters.HighContrast)
                text.Foreground = SystemColors.WindowTextBrush;
            else
                SetOrdinaryThemeResource(text, TextBlock.ForegroundProperty, OrdinaryThemeResource.PrimaryText);
        }

        private static void SetOrdinaryThemeResource(
            FrameworkElement target,
            DependencyProperty property,
            OrdinaryThemeResource role)
        {
            target.SetResourceReference(property, GetOrdinaryThemeResourceKey(role));
        }

        private static string GetOrdinaryThemeResourceKey(OrdinaryThemeResource role)
        {
            return role switch
            {
                OrdinaryThemeResource.ContentBackground => "ContentBackgroundBrush",
                OrdinaryThemeResource.Border => ThemeService.DetailsBorderBrushKey,
                OrdinaryThemeResource.PrimaryText => "PrimaryTextBrush",
                OrdinaryThemeResource.ButtonBackground => "ButtonBackgroundBrush",
                OrdinaryThemeResource.ButtonForeground => "ButtonForegroundBrush",
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
        }

        private static string Plural(int count, string singular)
        {
            return count == 1 ? singular : singular + "s";
        }
    }
}
