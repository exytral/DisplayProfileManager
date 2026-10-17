using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using DisplayProfileManager.Application;
using DisplayProfileManager.Displays;
using DisplayProfileManager.Displays.Models;
using DisplayProfileManager.Displays.Policy;

namespace DisplayProfileManager.UI.Controls
{
    public class DisplaySettingControl : UserControl
    {
        private readonly ProfileEditorService _profileEditorService;
        private readonly DisplaySetting _setting;

        public List<DisplaySetting> CloneGroupMembers; // Public so editor can preserve device order across clone rebuilds
        private readonly bool _isCloneGroup;
        private ComboBox _resolutionComboBox;
        private ComboBox _refreshRateComboBox;
        private CheckBox _primaryCheckBox;
        private CheckBox _enabledCheckBox;
        private CheckBox _hdrCheckBox;
        private CheckBox _wcgCheckBox;
        private bool _preHdrWcgEnabled;
        private bool _hdrSelectionActive;
        private string _preHdrColorProfile;
        private bool _hdrColorProfileSelectionChanged;
        private bool _suppressAdvancedColorEvents;
        private readonly List<DisplayConfigInfo> _displayConfigs;
        private readonly bool? _editorHdrSupported;
        private readonly bool? _editorWcgSupported;
        private bool _suppressColorProfileEvents;
        private ComboBox _rotationComboBox;
        private ComboBox _dpiComboBox;
        private ComboBox _colorProfileComboBox;
        private TextBlock _colorProfileLabel;

        internal static bool IsWcgControlAvailable(bool usesDedicatedWcgApi, bool isWcgSupported) => usesDedicatedWcgApi && isWcgSupported;

        private bool UsesDedicatedWcgApi => AdvancedColorService.IsWindows24H2OrGreater();

        internal static (bool HasLiveObservation, bool? HdrSupported, bool? WcgSupported) ResolveAdvancedColorCapabilitiesForEditor(
            DisplaySetting setting,
            List<DisplayConfigInfo> displayConfigs)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));

            var live = displayConfigs == null ? null : DisplayAddressResolver.ResolveLiveDisplay(setting, displayConfigs);
            if (live?.IsAdvancedColorInfoAvailable == true)
            {
                return (true, live.IsHdrSupported, live.IsWcgSupported);
            }

            return (false, setting.LastKnownHdrSupported, setting.LastKnownWcgSupported);
        }

        internal static bool ResolveAdvancedColorInfoAvailability(DisplaySetting setting, List<DisplayConfigInfo> displayConfigs)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));

            return displayConfigs != null &&
                DisplayAddressResolver.ResolveLiveDisplay(setting, displayConfigs)?.IsAdvancedColorInfoAvailable == true;
        }

        private bool HasHdrCapabilityEvidence => _editorHdrSupported.HasValue;
        private bool HasSecondaryAdvancedColorEvidence => _editorWcgSupported.HasValue;
        private bool IsHdrSupportedForEditor => _editorHdrSupported == true;

        private bool IsSecondaryAdvancedColorSupported() =>
            UsesDedicatedWcgApi
                ? IsWcgControlAvailable(true, _editorWcgSupported == true)
                : AdvancedColorService.IsLegacyAcmSupported(_editorWcgSupported == true);

        private string GetHdrControlLabel() =>
            !HasHdrCapabilityEvidence
                ? "HDR (Pending)"
                : (IsHdrSupportedForEditor ? "HDR" : "HDR (Not Supported)");

        private string GetHdrControlToolTip() =>
            !HasHdrCapabilityEvidence
                ? "HDR capability is pending; stored intent will be preserved"
                : (IsHdrSupportedForEditor ? "Enable HDR for this monitor" : "This monitor does not support HDR");

        private string GetSecondaryAdvancedColorLabel()
        {
            string name = UsesDedicatedWcgApi ? "WCG" : "ACM";
            return HasSecondaryAdvancedColorEvidence ? name : $"{name} (Pending)";
        }

        private string GetSecondaryAdvancedColorToolTip() =>
            !HasSecondaryAdvancedColorEvidence
                ? "Advanced Color capability is pending; stored intent will be preserved"
                : (UsesDedicatedWcgApi ? "Wide Color Gamut" : "Auto Color Management (legacy Advanced Color)");

        internal static T SetThemeResourceReference<T>(T element, DependencyProperty property, object resourceKey)
            where T : FrameworkElement
        {
            element.SetResourceReference(property, resourceKey);
            return element;
        }

        internal DisplaySettingControl(ProfileEditorService profileEditorService, DisplaySetting setting, bool isCloneGroup = false, List<DisplaySetting> cloneGroupMembers = null, List<DisplayConfigInfo> displayConfigs = null)
        {
            _profileEditorService = profileEditorService ?? throw new ArgumentNullException(nameof(profileEditorService));

            // Skip lookup when identity and native resolution are already populated
            if (string.IsNullOrEmpty(setting.DeviceName) || setting.NativeWidth == 0 || setting.NativeHeight == 0)
                DisplayAddressResolver.ResolveDeviceName(setting, displayConfigs);

            _setting = setting;
            _displayConfigs = displayConfigs;
            var advancedColorCapabilities = ResolveAdvancedColorCapabilitiesForEditor(setting, _displayConfigs);
            _editorHdrSupported = advancedColorCapabilities.HdrSupported;
            _editorWcgSupported = advancedColorCapabilities.WcgSupported;
            _preHdrWcgEnabled = setting.IsWcgEnabled;
            _hdrSelectionActive = setting.IsHdrEnabled;
            _preHdrColorProfile = setting.IsHdrEnabled ? null : setting.ColorProfile;
            _isCloneGroup = isCloneGroup;
            CloneGroupMembers = cloneGroupMembers ?? [setting];

            InitializeControl();
        }

        private void InitializeControl()
        {
            var mainPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };

            FrameworkElement nameRow;

            if (_isCloneGroup && CloneGroupMembers.Count > 1)
            {
                var nameGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                nameGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = SetThemeResourceReference(new TextBlock
                {
                    Text = "\uE71B",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                }, TextBlock.ForegroundProperty, "ButtonBackgroundBrush");
                Grid.SetColumn(icon, 0);
                nameGrid.Children.Add(icon);

                var leftContentPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var namesPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                foreach (var member in CloneGroupMembers)
                {
                    var nameText = member.IsCloneSource ? $"{member.ReadableDeviceName}  (Source)" : $"{member.ReadableDeviceName}  (Clone)";
                    namesPanel.Children.Add(SetThemeResourceReference(new TextBlock
                    {
                        Text = nameText,
                        FontWeight = FontWeights.Medium,
                        FontSize = 14,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }, TextBlock.ForegroundProperty, "PrimaryTextBrush"));
                }
                leftContentPanel.Children.Add(namesPanel);

                _enabledCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = "Enable",
                    IsChecked = _setting.IsEnabled,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _enabledCheckBox.Checked += EnabledCheckBox_CheckedChanged;
                _enabledCheckBox.Unchecked += EnabledCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_enabledCheckBox);

                _primaryCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = "Primary",
                    IsChecked = _setting.IsPrimary,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
                _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;
                leftContentPanel.Children.Add(_primaryCheckBox);

                _hdrCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = GetHdrControlLabel(),
                    IsChecked = _setting.IsHdrEnabled,
                    IsEnabled = HasHdrCapabilityEvidence && IsHdrSupportedForEditor,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = GetHdrControlToolTip()
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _hdrCheckBox.Checked += HdrCheckBox_CheckedChanged;
                _hdrCheckBox.Unchecked += HdrCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_hdrCheckBox);

                bool wcgSupported = IsSecondaryAdvancedColorSupported();
                _wcgCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = GetSecondaryAdvancedColorLabel(),
                    IsChecked = _setting.IsHdrEnabled || _setting.IsWcgEnabled,
                    IsEnabled = HasSecondaryAdvancedColorEvidence && wcgSupported && !(_setting.IsHdrEnabled && IsHdrSupportedForEditor),
                    Visibility = (!HasSecondaryAdvancedColorEvidence || wcgSupported) ? Visibility.Visible : Visibility.Collapsed,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = GetSecondaryAdvancedColorToolTip()
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _wcgCheckBox.Checked += WcgCheckBox_CheckedChanged;
                _wcgCheckBox.Unchecked += WcgCheckBox_CheckedChanged;
                leftContentPanel.Children.Add(_wcgCheckBox);

                Grid.SetColumn(leftContentPanel, 1);
                nameGrid.Children.Add(leftContentPanel);

                var breakBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                breakBtnContent.Children.Add(new TextBlock
                {
                    Text = "\uE8E6",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 6, 0)
                });
                breakBtnContent.Children.Add(new TextBlock { Text = "Break Clone", VerticalAlignment = VerticalAlignment.Center });

                var breakBtn = new Button
                {
                    Content = breakBtnContent,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Style = BuildPrimaryButtonStyle()
                };
                breakBtn.Click += (s, e) => BreakCloneGroup();
                Grid.SetColumn(breakBtn, 2);
                nameGrid.Children.Add(breakBtn);

                nameRow = nameGrid;
            }
            else
            {
                var singleGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                singleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                singleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var nameBlock = SetThemeResourceReference(new TextBlock
                {
                    Text = $"{_setting.ReadableDeviceName}",
                    FontWeight = FontWeights.Medium,
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 12, 0)
                }, TextBlock.ForegroundProperty, "PrimaryTextBrush");
                leftPanel.Children.Add(nameBlock);

                _enabledCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = "Enable",
                    IsChecked = _setting.IsEnabled,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _enabledCheckBox.Checked += EnabledCheckBox_CheckedChanged;
                _enabledCheckBox.Unchecked += EnabledCheckBox_CheckedChanged;
                leftPanel.Children.Add(_enabledCheckBox);

                _primaryCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = "Primary",
                    IsChecked = _setting.IsPrimary,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
                _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;
                leftPanel.Children.Add(_primaryCheckBox);

                _hdrCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = GetHdrControlLabel(),
                    IsChecked = _setting.IsHdrEnabled,
                    IsEnabled = HasHdrCapabilityEvidence && IsHdrSupportedForEditor,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = GetHdrControlToolTip()
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _hdrCheckBox.Checked += HdrCheckBox_CheckedChanged;
                _hdrCheckBox.Unchecked += HdrCheckBox_CheckedChanged;
                leftPanel.Children.Add(_hdrCheckBox);

                bool wcgSupported = IsSecondaryAdvancedColorSupported();
                _wcgCheckBox = SetThemeResourceReference(new CheckBox
                {
                    Content = GetSecondaryAdvancedColorLabel(),
                    IsChecked = _setting.IsHdrEnabled || _setting.IsWcgEnabled,
                    IsEnabled = HasSecondaryAdvancedColorEvidence && wcgSupported && !(_setting.IsHdrEnabled && IsHdrSupportedForEditor),
                    Visibility = (!HasSecondaryAdvancedColorEvidence || wcgSupported) ? Visibility.Visible : Visibility.Collapsed,
                    FontSize = 14,
                    Padding = new Thickness(6, 0, 0, 0),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = GetSecondaryAdvancedColorToolTip()
                }, Control.ForegroundProperty, "PrimaryTextBrush");
                _wcgCheckBox.Checked += WcgCheckBox_CheckedChanged;
                _wcgCheckBox.Unchecked += WcgCheckBox_CheckedChanged;
                leftPanel.Children.Add(_wcgCheckBox);

                Grid.SetColumn(leftPanel, 0);
                singleGrid.Children.Add(leftPanel);

                var cloneBtnContent = new StackPanel { Orientation = Orientation.Horizontal };
                cloneBtnContent.Children.Add(new TextBlock { Text = "Clone", VerticalAlignment = VerticalAlignment.Center });
                cloneBtnContent.Children.Add(new TextBlock
                {
                    Text = "\u25BC",
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 2, 0, 0)
                });

                var cloneBtn = new Button
                {
                    Content = cloneBtnContent,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Style = BuildDropdownButtonStyle()
                };
                cloneBtn.Click += CloneButton_Click;
                Grid.SetColumn(cloneBtn, 1);
                singleGrid.Children.Add(cloneBtn);

                nameRow = singleGrid;
            }
            mainPanel.Children.Add(nameRow);

            // Single-row settings grid — Resolution | Refresh Rate | Rotation | DPI | SDR/HDR Color
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Resolution
            var resolutionPanel = new StackPanel();
            resolutionPanel.Children.Add(SetThemeResourceReference(new TextBlock { Text = "Resolution", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4) }, TextBlock.ForegroundProperty, "PrimaryTextBrush"));
            _resolutionComboBox = SetThemeResourceReference(new ComboBox
            {
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryComboBoxStyle"]
            }, ComboBox.BorderBrushProperty, "ComboBoxBorderBrush");
            _resolutionComboBox.SelectionChanged += ResolutionComboBox_SelectionChanged;
            PopulateResolutionComboBox();
            resolutionPanel.Children.Add(_resolutionComboBox);
            Grid.SetColumn(resolutionPanel, 0);
            contentGrid.Children.Add(resolutionPanel);

            // Refresh Rate
            var refreshRatePanel = new StackPanel();
            refreshRatePanel.Children.Add(SetThemeResourceReference(new TextBlock { Text = "Refresh Rate", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4) }, TextBlock.ForegroundProperty, "PrimaryTextBrush"));
            _refreshRateComboBox = SetThemeResourceReference(new ComboBox
            {
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryComboBoxStyle"]
            }, ComboBox.BorderBrushProperty, "ComboBoxBorderBrush");
            PopulateRefreshRateComboBox();
            refreshRatePanel.Children.Add(_refreshRateComboBox);
            Grid.SetColumn(refreshRatePanel, 2);
            contentGrid.Children.Add(refreshRatePanel);

            // Rotation
            var rotationPanel = new StackPanel();
            rotationPanel.Children.Add(SetThemeResourceReference(new TextBlock { Text = "Rotation", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4) }, TextBlock.ForegroundProperty, "PrimaryTextBrush"));
            _rotationComboBox = SetThemeResourceReference(new ComboBox
            {
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryComboBoxStyle"]
            }, ComboBox.BorderBrushProperty, "ComboBoxBorderBrush");
            PopulateRotationComboBox();
            _rotationComboBox.SelectionChanged += RotationComboBox_SelectionChanged;
            rotationPanel.Children.Add(_rotationComboBox);
            Grid.SetColumn(rotationPanel, 4);
            contentGrid.Children.Add(rotationPanel);

            // DPI
            var dpiPanel = new StackPanel();
            dpiPanel.Children.Add(SetThemeResourceReference(new TextBlock { Text = "DPI", FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 4) }, TextBlock.ForegroundProperty, "PrimaryTextBrush"));
            _dpiComboBox = SetThemeResourceReference(new ComboBox
            {
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryComboBoxStyle"]
            }, ComboBox.BorderBrushProperty, "ComboBoxBorderBrush");
            PopulateDpiComboBox();
            dpiPanel.Children.Add(_dpiComboBox);
            Grid.SetColumn(dpiPanel, 6);
            contentGrid.Children.Add(dpiPanel);

            // SDR/HDR Color Profile
            var colorProfilePanel = new StackPanel();
            _colorProfileLabel = SetThemeResourceReference(new TextBlock
            {
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 4)
            }, TextBlock.ForegroundProperty, "PrimaryTextBrush");
            colorProfilePanel.Children.Add(_colorProfileLabel);
            _colorProfileComboBox = SetThemeResourceReference(new ComboBox
            {
                Padding = new Thickness(8),
                BorderThickness = new Thickness(1),
                Style = (Style)System.Windows.Application.Current.Resources["PrimaryComboBoxStyle"]
            }, ComboBox.BorderBrushProperty, "ComboBoxBorderBrush");
            try { PopulateColorProfileComboBox(); } catch (Exception) { }
            _colorProfileComboBox.SelectionChanged += ColorProfileComboBox_SelectionChanged;
            colorProfilePanel.Children.Add(_colorProfileComboBox);
            Grid.SetColumn(colorProfilePanel, 8);
            Grid.SetRow(colorProfilePanel, 0);
            contentGrid.Children.Add(colorProfilePanel);
            UpdateColorProfileLabel();

            mainPanel.Children.Add(contentGrid);
            Content = mainPanel;

            UpdateControlStates();
        }
        private static Style BuildPrimaryButtonStyle()
        {
            // Visual tree construction
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            // State triggers
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ButtonHoverBackgroundBrush")));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ButtonPressedBackgroundBrush")));
            template.Triggers.Add(pressedTrigger);

            // Style composition
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ButtonBackgroundBrush")));
            style.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("ButtonForegroundBrush")));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(Button.FontWeightProperty, FontWeights.Medium));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            return style;
        }

        private static Style BuildDropdownButtonStyle()
        {
            // Visual tree construction
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(content);

            // State triggers
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFactory };
            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ComboBoxHoverBackgroundBrush")));
            template.Triggers.Add(hoverTrigger);

            // Style composition
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ComboBoxBackgroundBrush")));
            style.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("PrimaryTextBrush")));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("ComboBoxBorderBrush")));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            return style;
        }

        private void UpdateControlStates()
        {
            // Read enabled state from checkbox — _setting.IsEnabled is not written by handlers
            bool isEnabled = _enabledCheckBox.IsChecked == true;

            double opacity = isEnabled ? 1.0 : UiOpacity.Inactive;

            _resolutionComboBox.IsEnabled = true;
            _refreshRateComboBox.IsEnabled = true;
            _dpiComboBox.IsEnabled = true;
            _primaryCheckBox.IsEnabled = isEnabled;
            _rotationComboBox.IsEnabled = true;

            _resolutionComboBox.Opacity = opacity;
            _refreshRateComboBox.Opacity = opacity;
            _dpiComboBox.Opacity = opacity;
            _primaryCheckBox.Opacity = opacity;
            _rotationComboBox.Opacity = opacity;

            _hdrCheckBox.Visibility = (!HasHdrCapabilityEvidence || IsHdrSupportedForEditor) ? Visibility.Visible : Visibility.Collapsed;
            _hdrCheckBox.IsEnabled = HasHdrCapabilityEvidence && IsHdrSupportedForEditor;
            _hdrCheckBox.Opacity = HasHdrCapabilityEvidence ? opacity : UiOpacity.Blocked;

            if (_wcgCheckBox != null)
            {
                bool hdrForced = _hdrCheckBox?.IsChecked == true && IsHdrSupportedForEditor;
                bool wcgSupported = IsSecondaryAdvancedColorSupported();

                _wcgCheckBox.Visibility = (!HasSecondaryAdvancedColorEvidence || wcgSupported) ? Visibility.Visible : Visibility.Collapsed;
                var wcgPresentation = ResolveWcgControlPresentation(
                    HasSecondaryAdvancedColorEvidence,
                    wcgSupported,
                    hdrForced,
                    isEnabled);

                _wcgCheckBox.IsEnabled = wcgPresentation.IsEnabled;
                _wcgCheckBox.Opacity = wcgPresentation.Opacity;
            }

            _rotationComboBox.Opacity = isEnabled ? (_rotationComboBox.SelectedIndex == 0 ? UiOpacity.Inactive : 1.0) : UiOpacity.Inactive;

            if (_colorProfileComboBox != null)
            {
                _colorProfileComboBox.IsEnabled = true;
                _colorProfileComboBox.Opacity = isEnabled ? ((_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null ? UiOpacity.Inactive : 1.0) : UiOpacity.Inactive;
            }

            // Enforce minimum one enabled display
            var parent = Parent as Panel;
            if (parent != null && !isEnabled)
            {
                int enabledCount = 0;
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true)
                        enabledCount++;

                if (enabledCount == 0)
                {
                    // Recheck last enabled display so at least one remains active
                    _enabledCheckBox.IsChecked = true;
                    MessageBox.Show("At least one display must remain enabled.", "Display Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            // Transfer primary flag to another enabled display when current primary is disabled
            if (!isEnabled && _primaryCheckBox.IsChecked == true && parent != null)
            {
                _primaryCheckBox.IsChecked = false;

                foreach (var child in parent.Children)
                {
                    if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true)
                    {
                        control.SetPrimary(true);
                        break;
                    }
                }
            }
        }

        private void EnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateControlStates();
        }

        private void PrimaryCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _setting.IsPrimary = true;
            if (Parent is Panel parent)
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control != this)
                    {
                        control._primaryCheckBox.IsChecked = false;
                        control._setting.IsPrimary = false;
                    }
        }

        private void PrimaryCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Parent is Panel parent)
            {
                int primaryCount = 0;
                foreach (var child in parent.Children)
                    if (child is DisplaySettingControl control && control != this)
                        if (control._primaryCheckBox.IsChecked == true && control._enabledCheckBox.IsChecked == true)
                            primaryCount++;

                if (primaryCount == 0 && _enabledCheckBox.IsChecked == true)
                {
                    _primaryCheckBox.IsChecked = true;
                    MessageBox.Show("At least one enabled display must be set as primary.", "Display Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            _setting.IsPrimary = false;
        }

        public void SetPrimary(bool isPrimary)
        {
            // Update primary checkbox while suppressing event loops
            _primaryCheckBox.Checked -= PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked -= PrimaryCheckBox_Unchecked;

            _primaryCheckBox.IsChecked = isPrimary;
            _setting.IsPrimary = isPrimary;

            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;

            // Enforce single-primary constraint across siblings
            if (isPrimary)
            {
                if (Parent is Panel parent)
                    foreach (var child in parent.Children)
                        if (child is DisplaySettingControl control && control != this)
                            control.SetPrimary(false);
            }
        }

        internal static (bool IsEnabled, double Opacity) ResolveWcgControlPresentation(
            bool advancedColorAvailable,
            bool wcgSupported,
            bool hdrForced,
            bool displayEnabled)
        {
            return (
                IsEnabled: advancedColorAvailable && wcgSupported && !hdrForced,
                Opacity: !advancedColorAvailable
                    ? UiOpacity.Blocked
                    : (displayEnabled ? 1.0 : UiOpacity.Inactive));
        }

        internal static (bool WcgChecked, bool RememberedWcg) ResolveHdrWcgEditorState(
            bool hdrOn,
            bool wasHdrOn,
            bool currentWcgChecked,
            bool rememberedWcg)
        {
            if (hdrOn)
            {
                return (
                    WcgChecked: true,
                    RememberedWcg: wasHdrOn ? rememberedWcg : currentWcgChecked);
            }

            bool restored = wasHdrOn ? rememberedWcg : currentWcgChecked;
            return (WcgChecked: restored, RememberedWcg: restored);
        }

        internal static (string SelectedProfile, string RememberedSdrProfile, bool HdrSelectionChanged) ResolveHdrColorProfileEditorState(
            bool hdrOn,
            bool wasHdrOn,
            string currentProfile,
            string rememberedSdrProfile,
            bool hdrSelectionChanged,
            IEnumerable<string> sdrCompatibleProfiles)
        {
            if (hdrOn)
            {
                return (
                    SelectedProfile: null,
                    RememberedSdrProfile: wasHdrOn ? rememberedSdrProfile : currentProfile,
                    HdrSelectionChanged: wasHdrOn && hdrSelectionChanged);
            }

            if (!wasHdrOn)
            {
                return (currentProfile, currentProfile, false);
            }

            bool currentCompatible = string.IsNullOrEmpty(currentProfile) ||
                (sdrCompatibleProfiles?.Any(profile => string.Equals(profile, currentProfile, StringComparison.OrdinalIgnoreCase)) ?? false);
            string selected = hdrSelectionChanged && currentCompatible
                ? currentProfile
                : rememberedSdrProfile;
            return (selected, selected, false);
        }

        private void WcgCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressAdvancedColorEvents || _hdrSelectionActive) return;

            _preHdrWcgEnabled = _wcgCheckBox?.IsChecked == true;
        }

        private void HdrCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool hdrOn = _hdrCheckBox.IsChecked == true && IsHdrSupportedForEditor;
            bool wasHdrOn = _hdrSelectionActive;
            string currentColorProfile = (_colorProfileComboBox?.SelectedItem as ComboBoxItem)?.Tag as string;
            IEnumerable<string> sdrCompatibleProfiles = [];
            if (!hdrOn && wasHdrOn && _hdrColorProfileSelectionChanged)
            {
                try
                {
                    sdrCompatibleProfiles = _profileEditorService.GetColorProfiles(hdrOnly: false);
                }
                catch (Exception)
                {
                    sdrCompatibleProfiles = [];
                }
            }

            var (selectedProfile, rememberedSdrProfile, hdrSelectionChanged) = ResolveHdrColorProfileEditorState(
                hdrOn,
                wasHdrOn,
                currentColorProfile,
                _preHdrColorProfile,
                _hdrColorProfileSelectionChanged,
                sdrCompatibleProfiles);

            if (_wcgCheckBox != null)
            {
                var (wcgChecked, rememberedWcg) = ResolveHdrWcgEditorState(
                    hdrOn,
                    wasHdrOn,
                    _wcgCheckBox.IsChecked == true,
                    _preHdrWcgEnabled);

                _suppressAdvancedColorEvents = true;
                try
                {
                    _preHdrWcgEnabled = rememberedWcg;
                    _wcgCheckBox.IsChecked = wcgChecked;
                    _wcgCheckBox.IsEnabled = !hdrOn &&
                        HasSecondaryAdvancedColorEvidence &&
                        IsSecondaryAdvancedColorSupported();
                }
                finally
                {
                    _suppressAdvancedColorEvents = false;
                }
            }

            _hdrSelectionActive = hdrOn;
            _preHdrColorProfile = rememberedSdrProfile;
            _hdrColorProfileSelectionChanged = hdrSelectionChanged;

            UpdateColorProfileLabel();
            _suppressColorProfileEvents = true;
            try
            {
                PopulateColorProfileComboBox(clearSelection: true);
                SelectColorProfile(selectedProfile);
            }
            catch (Exception) { }
            finally
            {
                _suppressColorProfileEvents = false;
            }

            UpdateControlStates();
        }

        private void CloneButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            if (Parent is not Panel panel) return;

            var available = panel.Children.OfType<DisplaySettingControl>().Where(c => c != this && !c._isCloneGroup).ToList();

            if (!available.Any())
            {
                MessageBox.Show("No other displays available to clone with.", "Clone Display", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var stack = new StackPanel { MinWidth = 220 };
            foreach (var target in available)
            {
                var num = Regex.Match(target._setting.DeviceName ?? "", @"\d+$").Value;
                var label = string.IsNullOrEmpty(num) ? target._setting.ReadableDeviceName : $"Display {num} · {target._setting.ReadableDeviceName}";

                var row = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(12, 8, 12, 8),
                    Cursor = Cursors.Hand,
                    Child = SetThemeResourceReference(new TextBlock { Text = label, FontSize = 13 }, TextBlock.ForegroundProperty, "PrimaryTextBrush")
                };

                row.MouseEnter += (s, ev) => SetThemeResourceReference(row, Border.BackgroundProperty, "ControlHoverBackgroundBrush");
                row.MouseLeave += (s, ev) => row.Background = Brushes.Transparent;

                var captured = target;
                row.MouseLeftButtonUp += (s, ev) =>
                {
                    ((Popup)((Border)((StackPanel)row.Parent).Parent).Parent).IsOpen = false;
                    CreateCloneGroup(captured);
                };
                stack.Children.Add(row);
            }

            var popupBorder = SetThemeResourceReference(new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0, 4, 0, 4),
                Child = stack
            }, Border.BackgroundProperty, "ContentBackgroundBrush");
            SetThemeResourceReference(popupBorder, Border.BorderBrushProperty, "BorderBrush");

            var popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = popupBorder,
                IsOpen = true
            };
        }

        private void CreateCloneGroup(DisplaySettingControl other)
        {
            var newCloneGroupId = "clone-" + Guid.NewGuid().ToString("N")[..8];
            int sharedX = _setting.DisplayPositionX;
            int sharedY = _setting.DisplayPositionY;

            // Save pre-clone state before any primary transfer changes it
            foreach (var member in other.CloneGroupMembers)
            {
                member.OriginalSettings = false;
                member.OriginalPositionX = member.DisplayPositionX;
                member.OriginalPositionY = member.DisplayPositionY;
                member.OriginalIsPrimary = member.IsPrimary;
                member.OriginalWidth = member.Width;
                member.OriginalHeight = member.Height;
                member.OriginalFrequency = member.Frequency;
                member.OriginalRotation = member.Rotation;
                member.OriginalDpiScaling = member.DpiScaling;
                member.OriginalIsHdrEnabled = member.IsHdrEnabled;
                member.OriginalIsWcgEnabled = member.IsWcgEnabled;
                member.OriginalColorProfile = member.ColorProfile;
            }

            // Only transfer primary to source if no independent display already holds it
            bool otherHadPrimary = other.CloneGroupMembers.Any(m => m.IsPrimary);
            if (otherHadPrimary)
            {
                var panel = Parent as Panel;

                // No transfer needed if this control or any other independent control already holds primary
                bool primaryExistsElsewhere = CloneGroupMembers.Any(m => m.IsPrimary) ||
                    (panel != null && panel.Children
                        .OfType<DisplaySettingControl>()
                        .Where(c => c != this && c != other)
                        .Any(c => c.CloneGroupMembers.Any(m => m.IsPrimary)));

                foreach (var m in other.CloneGroupMembers)
                    m.IsPrimary = false;

                if (!primaryExistsElsewhere)
                    CloneGroupMembers[0].IsPrimary = true;
            }

            foreach (var member in CloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = true;
            }

            foreach (var member in other.CloneGroupMembers)
            {
                member.CloneGroupId = newCloneGroupId;
                member.IsCloneSource = false;
                member.DisplayPositionX = sharedX;
                member.DisplayPositionY = sharedY;
            }

            OnCloneGroupChanged?.Invoke();
        }

        private void BreakCloneGroup()
        {
            var panel = Parent as Panel;

            // Partition by clone role rather than list position
            var sourceMembers = CloneGroupMembers.Where(m => m.IsCloneSource).ToList();
            var attachedMembers = CloneGroupMembers.Where(m => !m.IsCloneSource).ToList();

            // Clear group id but retain source marker until settings are rebuilt
            foreach (var member in CloneGroupMembers)
                member.CloneGroupId = string.Empty;

            // Restore attached members first so primary ownership can be resolved correctly
            bool attachedHadPrimary = attachedMembers.Any(m => m.OriginalIsPrimary == true);
            bool primaryExistsElsewhere = (Parent as Panel)?.Children
                .OfType<DisplaySettingControl>()
                .Where(c => c != this)
                .Any(c => c.CloneGroupMembers.Any(m => m.IsPrimary)) ?? false;

            foreach (var member in sourceMembers)
            {
                member.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;
                member.OriginalSettings = false;
            }

            foreach (var member in attachedMembers)
                RestoreAttachedMemberState(member, _setting);

            // Sync representative checkbox before rebuilding controls
            _primaryCheckBox.Checked -= PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked -= PrimaryCheckBox_Unchecked;
            _primaryCheckBox.IsChecked = !attachedHadPrimary && !primaryExistsElsewhere;
            _setting.IsPrimary = !attachedHadPrimary && !primaryExistsElsewhere;
            _primaryCheckBox.Checked += PrimaryCheckBox_Checked;
            _primaryCheckBox.Unchecked += PrimaryCheckBox_Unchecked;

            OnCloneGroupChanged?.Invoke();
        }

        public static void RestoreAttachedMemberState(DisplaySetting member, DisplaySetting cloneSource)
        {
            member.IsPrimary = member.OriginalIsPrimary ?? false;

            if (member.OriginalPositionX.HasValue)
            {
                member.DisplayPositionX = member.OriginalPositionX.Value;
                member.DisplayPositionY = member.OriginalPositionY ?? 0;
                member.Width = member.OriginalWidth ?? (member.NativeWidth > 0 ? member.NativeWidth : member.Width);
                member.Height = member.OriginalHeight ?? (member.NativeHeight > 0 ? member.NativeHeight : member.Height);
                member.Frequency = member.OriginalFrequency ?? member.Frequency;
                member.Rotation = member.OriginalRotation ?? member.Rotation;
                member.DpiScaling = member.OriginalDpiScaling ?? member.DpiScaling;
                member.IsHdrEnabled = member.OriginalIsHdrEnabled ?? member.IsHdrEnabled;
                member.IsWcgEnabled = member.OriginalIsWcgEnabled ?? member.IsWcgEnabled;
                member.ColorProfile = member.OriginalColorProfile;
            }
            else
            {
                // Restore sensible independent layout when no pre-clone state was saved
                member.DisplayPositionX = cloneSource.DisplayPositionX + cloneSource.Width;
                member.DisplayPositionY = cloneSource.DisplayPositionY;
                if (member.NativeWidth > 0)
                {
                    member.Width = member.NativeWidth;
                    member.Height = member.NativeHeight;
                }
                var resKey = $"{member.Width}x{member.Height}";
                if (member.AvailableRefreshRates != null && member.AvailableRefreshRates.TryGetValue(resKey, out var rates) && rates.Count > 0)
                    member.Frequency = rates[0];
                if (member.AvailableDpiScaling != null && member.AvailableDpiScaling.Count > 0)
                    member.DpiScaling = member.AvailableDpiScaling[0];
            }

            // Clear all saved originals
            member.OriginalSettings = true;
            member.OriginalPositionX = null;
            member.OriginalPositionY = null;
            member.OriginalIsPrimary = null;
            member.OriginalWidth = null;
            member.OriginalHeight = null;
            member.OriginalFrequency = null;
            member.OriginalRotation = null;
            member.OriginalDpiScaling = null;
            member.OriginalIsHdrEnabled = null;
            member.OriginalIsWcgEnabled = null;
            member.OriginalColorProfile = null;
        }

        public Action OnCloneGroupChanged;

        private void PopulateResolutionComboBox()
        {
            List<string> supportedResolutions;

            // Prefer stored resolutions; fall back to live system query
            if (_setting.AvailableResolutions != null && _setting.AvailableResolutions.Count > 0)
                supportedResolutions = _setting.AvailableResolutions;
            else
                supportedResolutions = _profileEditorService.GetSupportedResolutionsOnly(_setting.DeviceName);

            string nativeRes = _setting.NativeWidth > 0 ? $"{_setting.NativeWidth}x{_setting.NativeHeight}" : null;
            foreach (var resolution in supportedResolutions)
            {
                bool isNative = nativeRes != null && string.Equals(resolution, nativeRes, StringComparison.OrdinalIgnoreCase);
                _resolutionComboBox.Items.Add(isNative ? $"{resolution} ★" : resolution);
            }

            var currentResolution = $"{_setting.Width}x{_setting.Height}";
            var matchedItem = _resolutionComboBox.Items.Cast<object>().FirstOrDefault(i => i.ToString().StartsWith(currentResolution, StringComparison.OrdinalIgnoreCase));

            if (matchedItem != null)
                _resolutionComboBox.SelectedItem = matchedItem;
            else
            {
                _resolutionComboBox.Items.Insert(0, currentResolution);
                _resolutionComboBox.SelectedIndex = 0;
            }
        }

        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_resolutionComboBox.SelectedItem == null || _refreshRateComboBox == null) return;

            var resolutionText = (_resolutionComboBox.SelectedItem?.ToString() ?? "").Replace(" ★", "").Trim();
            var resolutionParts = resolutionText.Split('x');

            if (resolutionParts.Length >= 2 && int.TryParse(resolutionParts[0], out int width) && int.TryParse(resolutionParts[1], out int height))
            {
                // Temporarily update dimensions so refresh rates are populated for selected resolution
                int prevWidth = _setting.Width, prevHeight = _setting.Height;
                _setting.Width = width;
                _setting.Height = height;
                PopulateRefreshRateComboBox();
                _setting.Width = prevWidth;
                _setting.Height = prevHeight;
            }
        }

        private void PopulateRefreshRateComboBox()
        {
            _refreshRateComboBox.Items.Clear();

            List<int> refreshRates;
            var currentResolution = $"{_setting.Width}x{_setting.Height}";

            // Prefer stored rates for current resolution; fall back to live query
            if (_setting.AvailableRefreshRates != null && _setting.AvailableRefreshRates.ContainsKey(currentResolution) && _setting.AvailableRefreshRates[currentResolution].Count > 0)
                refreshRates = _setting.AvailableRefreshRates[currentResolution];
            else
                refreshRates = _profileEditorService.GetAvailableRefreshRates(_setting.DeviceName, _setting.Width, _setting.Height);

            int maxRate = refreshRates.Count > 0 ? refreshRates.Max() : -1;
            foreach (var rate in refreshRates)
                _refreshRateComboBox.Items.Add(rate == maxRate ? $"{rate}Hz ★" : $"{rate}Hz");

            var currentRefreshRate = $"{_setting.Frequency}Hz";
            var matchedItem = _refreshRateComboBox.Items.Cast<object>().FirstOrDefault(i => i.ToString().StartsWith(currentRefreshRate, StringComparison.OrdinalIgnoreCase));

            if (matchedItem != null)
                _refreshRateComboBox.SelectedItem = matchedItem;
            else if (_refreshRateComboBox.Items.Count > 0)
            {
                _refreshRateComboBox.Items.Insert(0, currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
            else
            {
                _refreshRateComboBox.Items.Add(currentRefreshRate);
                _refreshRateComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateRotationComboBox()
        {
            _rotationComboBox.Items.Clear();
            _rotationComboBox.Items.Add("Not Applied");
            _rotationComboBox.Items.Add("0°");
            _rotationComboBox.Items.Add("90°");
            _rotationComboBox.Items.Add("180°");
            _rotationComboBox.Items.Add("270°");
            _rotationComboBox.SelectedIndex = _setting.Rotation;

            RotationComboBox_SelectionChanged(null, null);
        }

        private void RotationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rotationComboBox != null && _enabledCheckBox.IsChecked == true)
                _rotationComboBox.Opacity = _rotationComboBox.SelectedIndex == 0 ? UiOpacity.Inactive : 1.0;
        }

        private void PopulateDpiComboBox()
        {
            List<uint> dpiValues;

            // Prefer stored values; fall back to live system query
            if (_setting.AvailableDpiScaling != null && _setting.AvailableDpiScaling.Count > 0)
                dpiValues = _setting.AvailableDpiScaling;
            else
                dpiValues = _profileEditorService.GetSupportedDpiScaling(_setting.DeviceName);

            foreach (uint dpi in dpiValues)
                _dpiComboBox.Items.Add($"{dpi}%");

            var currentDpi = $"{_setting.DpiScaling}%";
            if (_dpiComboBox.Items.Contains(currentDpi))
                _dpiComboBox.SelectedItem = currentDpi;
            else
            {
                _dpiComboBox.Items.Insert(0, currentDpi);
                _dpiComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateColorProfileComboBox(bool clearSelection = false)
        {
            // Preserve current selection across repopulation unless HDR mode just changed
            string previousTag = clearSelection ? null : ((_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? _setting.ColorProfile);

            _colorProfileComboBox.Items.Clear();

            bool hdrMode = _hdrCheckBox?.IsChecked == true && IsHdrSupportedForEditor;

            _colorProfileComboBox.Items.Add(SetThemeResourceReference(new ComboBoxItem
            {
                Content = "Not Applied",
                Tag = null
            }, Control.ForegroundProperty, "PrimaryTextBrush"));

            var installedProfiles = hdrMode
                ? _profileEditorService.GetColorProfiles(hdrOnly: true)
                : _profileEditorService.GetColorProfiles(hdrOnly: false);
            foreach (var filename in installedProfiles)
            {
                _colorProfileComboBox.Items.Add(SetThemeResourceReference(new ComboBoxItem
                {
                    Content = filename,
                    Tag = filename,
                }, Control.ForegroundProperty, "PrimaryTextBrush"));
            }

            SelectColorProfile(previousTag);
        }

        private void SelectColorProfile(string profileValue)
        {
            if (string.IsNullOrEmpty(profileValue))
            {
                _colorProfileComboBox.SelectedIndex = 0;
                UpdateColorProfileOpacity();
                return;
            }

            foreach (ComboBoxItem item in _colorProfileComboBox.Items)
            {
                if (string.Equals(item.Tag as string, profileValue, StringComparison.OrdinalIgnoreCase))
                {
                    _colorProfileComboBox.SelectedItem = item;
                    UpdateColorProfileOpacity();
                    return;
                }
            }

            // Preserve stored profile as placeholder when it is no longer installed
            var missing = SetThemeResourceReference(new ComboBoxItem
            {
                Content = $"{profileValue}  (Not Found)",
                Tag = profileValue,
                ToolTip = "This color profile is no longer installed on this system"
            }, Control.ForegroundProperty, "TertiaryTextBrush");
            _colorProfileComboBox.Items.Add(missing);
            _colorProfileComboBox.SelectedItem = missing;
            UpdateColorProfileOpacity();
        }

        private void ColorProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateColorProfileOpacity();
            if (_suppressColorProfileEvents) return;

            string selectedProfile = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            if (_hdrSelectionActive)
                _hdrColorProfileSelectionChanged = true;
            else
                _preHdrColorProfile = selectedProfile;
        }

        private void UpdateColorProfileLabel()
        {
            if (_colorProfileLabel == null) return;
            bool hdrActive = _hdrCheckBox?.IsChecked == true && IsHdrSupportedForEditor;
            _colorProfileLabel.Text = hdrActive ? "HDR Color" : "SDR Color";
        }

        private void UpdateColorProfileOpacity()
        {
            if (_colorProfileComboBox == null) return;

            bool displayEnabled = _enabledCheckBox?.IsChecked == true;
            bool notApplied = (_colorProfileComboBox.SelectedItem as ComboBoxItem)?.Tag == null;
            _colorProfileComboBox.Opacity = !displayEnabled || notApplied ? UiOpacity.Inactive : 1.0;
        }

        internal static (bool IsHdrEnabled, bool IsWcgEnabled) ResolveAdvancedColorIntentForSave(
            DisplaySetting originalSetting,
            bool hasLiveObservation,
            bool? hdrSupported,
            bool? wcgSupported,
            bool requestedHdrEnabled,
            bool requestedWcgEnabled)
        {
            if (originalSetting == null) throw new ArgumentNullException(nameof(originalSetting));

            bool secondarySupported = wcgSupported == true;
            if (hasLiveObservation)
            {
                bool hdrEnabled = requestedHdrEnabled && hdrSupported == true;
                bool wcgEnabled = !hdrEnabled && requestedWcgEnabled && secondarySupported;
                return (hdrEnabled, wcgEnabled);
            }

            bool offlineHdrEnabled = hdrSupported == true ? requestedHdrEnabled : originalSetting.IsHdrEnabled;
            bool offlineWcgEnabled = secondarySupported ? requestedWcgEnabled : originalSetting.IsWcgEnabled;
            if (offlineHdrEnabled)
                offlineWcgEnabled = false;

            return (offlineHdrEnabled, offlineWcgEnabled);
        }

        internal static (bool IsHdrEnabled, bool IsWcgEnabled) ResolveAdvancedColorIntentForSave(
            DisplaySetting originalSetting,
            bool advancedColorAvailable,
            bool requestedHdrEnabled,
            bool requestedWcgEnabled)
        {
            if (originalSetting == null) throw new ArgumentNullException(nameof(originalSetting));

            if (!advancedColorAvailable)
            {
                return (originalSetting.IsHdrEnabled, originalSetting.IsWcgEnabled);
            }

            bool hdrEnabled = requestedHdrEnabled && originalSetting.IsHdrSupported;
            bool wcgEnabled = !hdrEnabled && requestedWcgEnabled;
            return (hdrEnabled, wcgEnabled);
        }

        public List<DisplaySetting> GetDisplaySettings()
        {
            var settings = new List<DisplaySetting>();

            if (_resolutionComboBox.SelectedItem == null || _dpiComboBox.SelectedItem == null || _refreshRateComboBox.SelectedItem == null)
            {
                return settings;
            }

            var resolutionText = _resolutionComboBox.SelectedItem.ToString().Replace(" ★", "").Replace("★", "").Trim();
            var dpiText = _dpiComboBox.SelectedItem.ToString();
            var refreshRateText = _refreshRateComboBox.SelectedItem.ToString();

            var resolutionParts = resolutionText.Split('x');
            if (resolutionParts.Length < 2)
            {
                return settings;
            }
            if (!int.TryParse(resolutionParts[0], out int width))
            {
                return settings;
            }

            string heightPart = resolutionParts[1].Replace(" ★", "").Replace("★", "").Trim();
            if (heightPart.Contains("@")) heightPart = heightPart.Split('@')[0].Trim();
            if (!int.TryParse(heightPart, out int height))
            {
                return settings;
            }

            if (!uint.TryParse(dpiText.Replace("%", ""), out uint dpiScaling))
            {
                return settings;
            }

            if (!int.TryParse(refreshRateText.Replace("Hz", "").Replace(" ★", "").Trim(), out int frequency))
                frequency = 60;

            var isEnabled = _enabledCheckBox.IsChecked == true;
            var isHdrEnabled = _hdrCheckBox.IsChecked == true;
            var isWcgEnabled = _wcgCheckBox?.IsChecked == true && !isHdrEnabled;
            var rotation = _rotationComboBox.SelectedIndex == 0 ? 0 : _rotationComboBox.SelectedIndex;
            var colorProfile = (_colorProfileComboBox?.SelectedItem is ComboBoxItem cp) ? cp.Tag as string : null;

            // Source always reads combo; attached reads own restored params only after BreakCloneGroup (CloneGroupId cleared)
            foreach (var originalSetting in CloneGroupMembers)
            {
                bool useOriginalSettings = originalSetting.OriginalSettings;
                var (hasLiveObservation, hdrSupported, wcgSupported) = ResolveAdvancedColorCapabilitiesForEditor(originalSetting, _displayConfigs);
                var (resolvedHdrEnabled, resolvedWcgEnabled) = ResolveAdvancedColorIntentForSave(
                    originalSetting,
                    hasLiveObservation,
                    hdrSupported,
                    wcgSupported,
                    useOriginalSettings ? originalSetting.IsHdrEnabled : isHdrEnabled,
                    useOriginalSettings ? originalSetting.IsWcgEnabled : isWcgEnabled);
                var displaySetting = new DisplaySetting
                {
                    // Identity
                    DeviceName = originalSetting.DeviceName,
                    DeviceString = originalSetting.DeviceString,
                    ReadableDeviceName = originalSetting.ReadableDeviceName,
                    ManufacturerName = originalSetting.ManufacturerName,
                    ProductCodeID = originalSetting.ProductCodeID,
                    TargetId = originalSetting.TargetId,
                    CloneGroupId = originalSetting.CloneGroupId,
                    IsCloneSource = originalSetting.IsCloneSource && !string.IsNullOrEmpty(originalSetting.CloneGroupId),
                    // State
                    IsEnabled = isEnabled,
                    IsPrimary = originalSetting.IsPrimary,
                    // Layout
                    DisplayPositionX = originalSetting.DisplayPositionX,
                    DisplayPositionY = originalSetting.DisplayPositionY,
                    // Configuration
                    Width = useOriginalSettings ? originalSetting.Width : width,
                    Height = useOriginalSettings ? originalSetting.Height : height,
                    Frequency = useOriginalSettings ? originalSetting.Frequency : frequency,
                    Rotation = useOriginalSettings ? originalSetting.Rotation : rotation,
                    DpiScaling = useOriginalSettings ? originalSetting.DpiScaling : dpiScaling,
                    IsHdrSupported = originalSetting.IsHdrSupported,
                    IsWcgSupported = originalSetting.IsWcgSupported,
                    LastKnownHdrSupported = originalSetting.LastKnownHdrSupported,
                    LastKnownWcgSupported = originalSetting.LastKnownWcgSupported,
                    IsHdrEnabled = resolvedHdrEnabled,
                    IsWcgEnabled = resolvedWcgEnabled,
                    ColorProfile = useOriginalSettings ? originalSetting.ColorProfile : colorProfile,
                    // Clone
                    // Restoration is consumed by this rebuild projection; later editor Save reads new selections.
                    OriginalSettings = originalSetting.OriginalSettings && !string.IsNullOrEmpty(originalSetting.CloneGroupId),
                    OriginalPositionX = originalSetting.OriginalPositionX,
                    OriginalPositionY = originalSetting.OriginalPositionY,
                    OriginalIsPrimary = originalSetting.OriginalIsPrimary,
                    OriginalWidth = originalSetting.OriginalWidth,
                    OriginalHeight = originalSetting.OriginalHeight,
                    OriginalFrequency = originalSetting.OriginalFrequency,
                    OriginalRotation = originalSetting.OriginalRotation,
                    OriginalDpiScaling = originalSetting.OriginalDpiScaling,
                    OriginalIsHdrEnabled = originalSetting.OriginalIsHdrEnabled,
                    OriginalIsWcgEnabled = originalSetting.OriginalIsWcgEnabled,
                    OriginalColorProfile = originalSetting.OriginalColorProfile,
                    // Native
                    NativeWidth = originalSetting.NativeWidth,
                    NativeHeight = originalSetting.NativeHeight,
                    // Capabilities
                    AvailableResolutions = originalSetting.AvailableResolutions,
                    AvailableRefreshRates = originalSetting.AvailableRefreshRates,
                    AvailableDpiScaling = originalSetting.AvailableDpiScaling
                };
                settings.Add(displaySetting);
            }

            return settings;
        }

        public bool ValidateInput()
        {
            if (_resolutionComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a resolution for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _resolutionComboBox.Focus();
                return false;
            }

            if (_refreshRateComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a refresh rate for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _refreshRateComboBox.Focus();
                return false;
            }

            if (_dpiComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a DPI scaling for all displays.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                _dpiComboBox.Focus();
                return false;
            }

            if (_enabledCheckBox.IsChecked == true)
            {
                if (Parent is Panel parent)
                {
                    bool hasPrimary = false;
                    foreach (var child in parent.Children)
                    {
                        if (child is DisplaySettingControl control && control._enabledCheckBox.IsChecked == true && control._primaryCheckBox.IsChecked == true)
                        {
                            hasPrimary = true;
                            break;
                        }
                    }

                    if (!hasPrimary)
                    {
                        MessageBox.Show("At least one enabled display must be set as primary.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _primaryCheckBox.Focus();
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
