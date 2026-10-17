using System;
using System.IO;
using System.Windows.Media;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.Tests.Helpers;
using DisplayProfileManager.UI.Icons;
using DisplayProfileManager.UI.Windows;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class QuickSwitchPresentationPolicyTests
    {

        [TestMethod]
        [TestCategory("Unit")]
        public void ProfileImageProviders_SeparateCustomAndGeneratedSemantics()
        {
            string provider = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Icons", "ProfileIconProvider.cs")));
            string converter = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Converters", "ProfileIconConverter.cs")));

            Assert.IsFalse(provider.Contains("LoadProfileImageSource", StringComparison.Ordinal));
            Assert.IsFalse(provider.Contains("ResolveProfileImageSource", StringComparison.Ordinal));
            StringAssert.Contains(converter, "ProfileIconProvider.LoadImageSource(value as string, size)");
            StringAssert.Contains(converter, "class SavedLayoutThumbnailConverter");
            StringAssert.Contains(converter, "SavedLayoutThumbnailRenderer.Create(profile.DisplaySettings, size)");
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchProfileRows_UseIndependentLayoutAndCustomIconSources()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(code, "public ImageSource LayoutIcon { get; private set; }");
            StringAssert.Contains(code, "public ImageSource CustomIcon { get; }");
            StringAssert.Contains(code, "SavedLayoutThumbnailRenderer.Create(Profile.DisplaySettings, 22)");
            StringAssert.Contains(code, "ProfileIconProvider.LoadImageSource(profile.Icon, 16)");
            StringAssert.Contains(xaml, "Source=\"{Binding LayoutIcon}\"");
            StringAssert.Contains(xaml, "x:Name=\"ProfileNameRegion\"");
            StringAssert.Contains(xaml, "MaxWidth=\"{Binding ActualWidth, ElementName=ProfileNameRegion}\"");
            StringAssert.Contains(xaml, "HorizontalAlignment=\"Left\"");
            StringAssert.Contains(xaml, "TextTrimming=\"CharacterEllipsis\"");
            StringAssert.Contains(xaml, "Source=\"{Binding CustomIcon}\"");
            StringAssert.Contains(xaml, "Trigger Property=\"Source\" Value=\"{x:Null}\"");

            int name = xaml.IndexOf("Text=\"{Binding Name}\"", StringComparison.Ordinal);
            int customIcon = xaml.IndexOf("Source=\"{Binding CustomIcon}\"", name, StringComparison.Ordinal);
            Assert.IsTrue(name >= 0);
            Assert.IsTrue(customIcon > name);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SavedLayoutThumbnail_CloneGroup_RendersOneFilledFootprint()
        {
            var source = new DisplaySettingBuilder()
                .WithDeviceName("DISPLAY1")
                .WithName("Source")
                .WithPosition(0, 0)
                .WithResolution(1920, 1080)
                .Enabled()
                .WithCloneGroup("g")
                .AsCloneSource()
                .Build();
            var clone = new DisplaySettingBuilder()
                .WithDeviceName("DISPLAY2")
                .WithName("Clone")
                .WithPosition(0, 0)
                .WithResolution(1920, 1080)
                .Enabled()
                .WithCloneGroup("g")
                .Build();

            var image = (DrawingImage)SavedLayoutThumbnailRenderer.Create([source, clone], 22);
            var drawing = (DrawingGroup)image.Drawing;

            Assert.AreEqual(1, drawing.Children.Count);
            Assert.IsTrue(drawing.Children[0] is GeometryDrawing);
            var footprint = (GeometryDrawing)drawing.Children[0];
            Assert.IsNotNull(footprint.Brush);
            Assert.IsNull(footprint.Pen);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void DetailsSmallIcon_IsCustomOnlyAndMainUsesDedicatedGeneratedLayout()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml.cs")));

            StringAssert.Contains(xaml, "ConverterParameter=32");
            StringAssert.Contains(xaml, "SavedLayoutThumbnailConverter");
            StringAssert.Contains(xaml, "Binding Profile.Icon, Converter={StaticResource ProfileIconConverter}, ConverterParameter=16");
            StringAssert.Contains(code, "ProfileIconProvider.LoadImageSource(profile.Icon, 18)");
            Assert.IsFalse(code.Contains("LoadProfileImageSource", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchThemeChange_RefreshesGeneratedImagesWithoutProfileRebuild()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void OnThemeChanged", StringComparison.Ordinal);
            int end = code.IndexOf("private void OnSettingsChanged", start, StringComparison.Ordinal);
            string themePath = code[start..end];

            StringAssert.Contains(themePath, "RefreshGeneratedLayoutImages();");
            StringAssert.Contains(themePath, "item.RefreshLayoutIcon();");
            Assert.IsFalse(themePath.Contains("RefreshProfiles()", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchRefresh_NaturallySortsBeforeSelectionPolicy()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int sort = code.IndexOf(".OrderBy(profile => profile.Name, NaturalStringComparer.Instance)", StringComparison.Ordinal);
            int selection = code.IndexOf("QuickSwitchSelectionPolicy.GetInitialIndex(", StringComparison.Ordinal);

            Assert.IsTrue(sort >= 0);
            Assert.IsTrue(selection > sort);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_InitialContentRendered_DoesNotRebuildProfilesAfterReveal()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int renderedStart = code.IndexOf("protected override void OnContentRendered(EventArgs e)", StringComparison.Ordinal);
            int renderedEnd = code.IndexOf("protected override void OnDpiChanged", renderedStart, StringComparison.Ordinal);
            string rendered = code[renderedStart..renderedEnd];

            StringAssert.Contains(rendered, "base.OnContentRendered(e);");
            Assert.IsFalse(rendered.Contains("RefreshProfiles()", StringComparison.Ordinal));
            Assert.IsFalse(rendered.Contains("RevealPositionedSurface", StringComparison.Ordinal));
            Assert.IsFalse(rendered.Contains("TryFlushComposition", StringComparison.Ordinal));

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];

            StringAssert.Contains(refresh, "RefreshProfiles()");
            StringAssert.Contains(code, "_profiles.ProfilesLoaded += _profileRefresh.OnProfilesLoaded;");
            StringAssert.Contains(code, "_profiles.ProfileAdded += _profileRefresh.OnProfileAdded;");
            StringAssert.Contains(code, "_profiles.ProfileUpdated += _profileRefresh.OnProfileUpdated;");
            StringAssert.Contains(code, "_profiles.ProfileDeleted += _profileRefresh.OnProfileDeleted;");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_PlacementDiagnostic_RecordsRawWorkAreaTargetDpiAppBarEdgesAndFinalBounds()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(code, "Quick Switch placement |");
            StringAssert.Contains(code, "rcMonitor=");
            StringAssert.Contains(code, "rcWork=");
            StringAssert.Contains(code, "targetDpi=");
            StringAssert.Contains(code, "foregroundDpi=");
            StringAssert.Contains(code, "autoHideLeft=");
            StringAssert.Contains(code, "autoHideTop=");
            StringAssert.Contains(code, "autoHideRight=");
            StringAssert.Contains(code, "autoHideBottom=");
            StringAssert.Contains(code, "final=");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MainWindow_UsesCompactMinimumAndBalancedDetailsGeometry()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "MainWindow.xaml")));

            StringAssert.Contains(xaml, "MinWidth=\"700\"");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"*\"/>");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"16\"/>");
            StringAssert.Contains(xaml, "<ColumnDefinition Width=\"326\"/>");
        }

        private static int CountOccurrences(string text, string value)
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


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticFinalReveal_IsOneShotExactAndFailClosed()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void TryCompleteInitialStaticFinalReveal", StringComparison.Ordinal);
            int end = code.IndexOf("private void TryActivateAfterReveal", start, StringComparison.Ordinal);
            string reveal = code[start..end];

            int guard = reveal.IndexOf("if (_initialRevealAttempted)", StringComparison.Ordinal);
            int mark = reveal.IndexOf("_initialRevealAttempted = true;", StringComparison.Ordinal);
            int calculate = reveal.IndexOf("QuickSwitchWindowPlacement.CalculateBounds(", StringComparison.Ordinal);
            int place = reveal.IndexOf("QuickSwitchWindowPlacement.TryPlace(this, finalBounds)", StringComparison.Ordinal);
            int readback = reveal.IndexOf("QuickSwitchWindowPlacement.TryGetWindowBounds(this, out var actualBounds)", StringComparison.Ordinal);
            int exact = reveal.IndexOf("_finalStageRectExact = actualBounds == finalBounds", StringComparison.Ordinal);
            int opacity = reveal.IndexOf("Opacity = 1.0;", StringComparison.Ordinal);
            int release = reveal.IndexOf("_visibilityReleased = true;", StringComparison.Ordinal);

            Assert.IsTrue(guard >= 0 && mark > guard);
            Assert.IsTrue(calculate > mark);
            Assert.IsTrue(place > calculate);
            Assert.IsTrue(readback > place);
            Assert.IsTrue(exact > readback);
            Assert.IsTrue(opacity > exact);
            Assert.IsTrue(release > opacity);
            StringAssert.Contains(reveal, "FailClosedStaging(\"final-placement\")");
            StringAssert.Contains(reveal, "FailClosedStaging(\"final-rect-read\")");
            StringAssert.Contains(reveal, "FailClosedStaging(\"final-rect-mismatch\")");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticFinalReveal_HasNoActiveRouteAOrRepresentationPath()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            Assert.IsFalse(code.Contains("TrySetApplicationCloak(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("TryGetCloakFlags(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("TryFlushComposition(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("CompositionTarget.Rendering", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("CalculateEntrance(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("TryPlacePosition(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("TryPlaceTerminal(", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("DwmRegisterThumbnail", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("GraphicsCapture", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("RenderTargetBitmap", StringComparison.Ordinal));
            StringAssert.Contains(code, "entranceRoute=StaticFinal");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticFinalReveal_UsesOpacityAndPreShowParkingOnlyBeforeLoaded()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(xaml, "Opacity=\"0\"");

            int start = code.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
            int end = code.IndexOf("protected override void OnContentRendered", start, StringComparison.Ordinal);
            string source = code[start..end];

            StringAssert.Contains(source, "QuickSwitchWindowPlacement.TryParkOffscreen");
            StringAssert.Contains(source, "Opacity = 0.0;");
            StringAssert.Contains(source, "Loaded += OnInitialLoaded;");
            Assert.IsFalse(source.Contains("TrySetApplicationCloak(", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("TryGetCloakFlags(", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticFinalFailure_AlwaysKeepsOverlayHidden()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void FailClosedStaging", StringComparison.Ordinal);
            int end = code.IndexOf("private void LogPlacementState", start, StringComparison.Ordinal);
            string failure = code[start..end];

            StringAssert.Contains(failure, "Opacity = 0.0;");
            StringAssert.Contains(failure, "_visibilityReleased = false;");
            StringAssert.Contains(failure, "new Action(Close)");
            StringAssert.Contains(failure, "Quick Switch static-final reveal failed");
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_FirstShow_IsNonActivatingButExplicitPostShowActivationRemains()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(xaml, "ShowActivated=\"False\"");

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];

            int dismissedGuard = refresh.IndexOf("if (_interaction.IsDismissed)", StringComparison.Ordinal);
            int show = refresh.IndexOf("Show();", StringComparison.Ordinal);
            int activationGate = refresh.LastIndexOf("TryActivateAfterReveal();", StringComparison.Ordinal);

            Assert.IsTrue(dismissedGuard >= 0);
            Assert.IsTrue(show > dismissedGuard);
            Assert.IsTrue(activationGate > show);

            int helperStart = code.IndexOf("private void TryActivateAfterReveal", StringComparison.Ordinal);
            int helperEnd = code.IndexOf("private void FailClosedStaging", helperStart, StringComparison.Ordinal);
            string helper = code[helperStart..helperEnd];

            int activate = helper.IndexOf("Activate();", StringComparison.Ordinal);
            int focus = helper.IndexOf("Focus();", StringComparison.Ordinal);
            int listFocus = helper.IndexOf("ProfileList.Focus();", StringComparison.Ordinal);

            Assert.IsTrue(activate >= 0);
            Assert.IsTrue(focus > activate);
            Assert.IsTrue(listFocus > focus);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_LoadedStaticFinalWiring_PrecedesPostShowActivationSeam()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int sourceStart = code.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
            int sourceEnd = code.IndexOf("protected override void OnContentRendered", sourceStart, StringComparison.Ordinal);
            string source = code[sourceStart..sourceEnd];

            StringAssert.Contains(source, "Loaded += OnInitialLoaded;");
            StringAssert.Contains(source, "Opacity = 0.0;");

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];

            int show = refresh.IndexOf("Show();", StringComparison.Ordinal);
            int activationGate = refresh.LastIndexOf("TryActivateAfterReveal();", StringComparison.Ordinal);
            Assert.IsTrue(show >= 0 && activationGate > show);

            StringAssert.Contains(code, "QuickSwitchWindowPlacement.TryPlace(this, finalBounds)");
            StringAssert.Contains(code, "_finalStageRectExact = actualBounds == finalBounds");
            StringAssert.Contains(code, "Opacity = 1.0;");
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_DeactivatedDismissal_UsesBackgroundPhysicalCloseOnly()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void Window_Deactivated", StringComparison.Ordinal);
            int end = code.IndexOf("private void RefreshProfilesOnDispatcher", start, StringComparison.Ordinal);
            string deactivated = code[start..end];

            StringAssert.Contains(deactivated, "_interaction.Dismiss(ScheduleDeactivationClose);");
            StringAssert.Contains(deactivated, "private void ScheduleDeactivationClose()");
            StringAssert.Contains(deactivated, "Dispatcher.BeginInvoke(");
            StringAssert.Contains(deactivated, "DispatcherPriority.Background");
            StringAssert.Contains(deactivated, "if (!IsLoaded || !IsVisible)");
            StringAssert.Contains(deactivated, "Close();");
            Assert.IsFalse(deactivated.Contains("_interaction.Dismiss();", StringComparison.Ordinal));
            Assert.IsFalse(deactivated.Contains("DispatcherPriority.Normal", StringComparison.Ordinal));
            Assert.IsFalse(deactivated.Contains("DispatcherPriority.Send", StringComparison.Ordinal));
            Assert.IsFalse(deactivated.Contains("DispatcherPriority.Loaded", StringComparison.Ordinal));
            Assert.IsFalse(deactivated.Contains("Timer", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(deactivated.Contains("_desktopAcrylic", StringComparison.Ordinal));
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_LifecycleDiagnostics_AppearExactlyOnceAtQualifiedSeams()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            foreach (string phase in new[]
            {
                "loaded-subscribed",
                "show-return",
                "loaded-entry",
                "loaded-exception",
                "activated",
                "deactivated",
                "closed"
            })
            {
                Assert.AreEqual(1, CountOccurrences(code, $"phase={phase}"), $"Expected exactly one lifecycle diagnostic for {phase}.");
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_LifecycleDiagnostics_PreserveShowLoadedAndActivationOrdering()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];

            int show = refresh.IndexOf("Show();", StringComparison.Ordinal);
            int showReturn = refresh.IndexOf("phase=show-return", StringComparison.Ordinal);
            int preShowCheck = refresh.IndexOf("if (!_preShowParkingSucceeded)", StringComparison.Ordinal);
            int activationGate = refresh.LastIndexOf("TryActivateAfterReveal();", StringComparison.Ordinal);

            Assert.IsTrue(show >= 0);
            Assert.IsTrue(showReturn > show);
            Assert.IsTrue(preShowCheck > showReturn);
            Assert.IsTrue(activationGate > preShowCheck);

            int sourceStart = code.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
            int sourceEnd = code.IndexOf("protected override void OnContentRendered", sourceStart, StringComparison.Ordinal);
            string source = code[sourceStart..sourceEnd];
            int opaque = source.IndexOf("ApplyOpaqueBrushes();", StringComparison.Ordinal);
            int loadedSubscribe = source.IndexOf("Loaded += OnInitialLoaded;", StringComparison.Ordinal);
            int subscribedLog = source.IndexOf("phase=loaded-subscribed", StringComparison.Ordinal);
            Assert.IsTrue(opaque >= 0 && loadedSubscribe > opaque);
            Assert.IsTrue(subscribedLog > loadedSubscribe);

            int loadedStart = code.IndexOf("private void OnInitialLoaded", StringComparison.Ordinal);
            int loadedEnd = code.IndexOf("private void TryCompleteInitialStaticFinalReveal", loadedStart, StringComparison.Ordinal);
            string loaded = code[loadedStart..loadedEnd];
            int entryLog = loaded.IndexOf("phase=loaded-entry", StringComparison.Ordinal);
            int unsubscribe = loaded.IndexOf("Loaded -= OnInitialLoaded;", StringComparison.Ordinal);
            Assert.IsTrue(entryLog >= 0 && unsubscribe > entryLog);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_LifecycleDiagnostics_RethrowAndPrecedeDismissAndUnsubscription()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int loadedStart = code.IndexOf("private void OnInitialLoaded", StringComparison.Ordinal);
            int loadedEnd = code.IndexOf("private void FailClosedStaging", loadedStart, StringComparison.Ordinal);
            string loaded = code[loadedStart..loadedEnd];
            int exceptionLog = loaded.IndexOf("phase=loaded-exception", StringComparison.Ordinal);
            int rethrow = loaded.IndexOf("throw;", exceptionLog, StringComparison.Ordinal);
            Assert.IsTrue(exceptionLog >= 0 && rethrow > exceptionLog);

            int deactivatedStart = code.IndexOf("private void Window_Deactivated", StringComparison.Ordinal);
            int deactivatedEnd = code.IndexOf("private void RefreshProfilesOnDispatcher", deactivatedStart, StringComparison.Ordinal);
            string deactivated = code[deactivatedStart..deactivatedEnd];
            Assert.IsTrue(
                deactivated.IndexOf("phase=deactivated", StringComparison.Ordinal) <
                deactivated.IndexOf("_interaction.Dismiss(ScheduleDeactivationClose);", StringComparison.Ordinal));

            int closedStart = code.IndexOf("protected override void OnClosed", StringComparison.Ordinal);
            int closedEnd = code.IndexOf("private void RefreshProfiles()", closedStart, StringComparison.Ordinal);
            string closed = code[closedStart..closedEnd];
            int closedLog = closed.IndexOf("phase=closed", StringComparison.Ordinal);
            Assert.IsTrue(closedLog >= 0);
            Assert.IsTrue(closed.IndexOf("Loaded -= OnInitialLoaded;", StringComparison.Ordinal) > closedLog);
            Assert.IsTrue(closed.IndexOf("ContentRendered -= OnInitialContentRendered;", StringComparison.Ordinal) > closedLog);
        }


        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_MissedLoadedCatchup_UsesSharedRevealBeforeActivation()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];

            int show = refresh.IndexOf("Show();", StringComparison.Ordinal);
            int showReturned = refresh.IndexOf("_initialShowReturned = true;", StringComparison.Ordinal);
            int parkingGuard = refresh.IndexOf("if (!_preShowParkingSucceeded)", StringComparison.Ordinal);
            int alreadyLoaded = refresh.IndexOf("if (IsLoaded && !_visibilityReleased && !_stagingFailed)", StringComparison.Ordinal);
            int reveal = refresh.IndexOf("TryCompleteInitialStaticFinalReveal();", StringComparison.Ordinal);
            int activate = refresh.LastIndexOf("TryActivateAfterReveal();", StringComparison.Ordinal);

            Assert.IsTrue(show >= 0);
            Assert.IsTrue(showReturned > show);
            Assert.IsTrue(parkingGuard > showReturned);
            Assert.IsTrue(alreadyLoaded > parkingGuard);
            Assert.IsTrue(reveal > alreadyLoaded);
            Assert.IsTrue(activate > reveal);
            Assert.IsFalse(refresh.Contains("Activate();", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_NormalLoadedPath_UsesSameRevealAndActivatesOnlyAfterRelease()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int loadedStart = code.IndexOf("private void OnInitialLoaded", StringComparison.Ordinal);
            int loadedEnd = code.IndexOf("private void TryCompleteInitialStaticFinalReveal", loadedStart, StringComparison.Ordinal);
            string loaded = code[loadedStart..loadedEnd];

            int entry = loaded.IndexOf("phase=loaded-entry", StringComparison.Ordinal);
            int unsubscribe = loaded.IndexOf("Loaded -= OnInitialLoaded;", StringComparison.Ordinal);
            int reveal = loaded.IndexOf("TryCompleteInitialStaticFinalReveal();", StringComparison.Ordinal);
            int activate = loaded.IndexOf("TryActivateAfterReveal();", StringComparison.Ordinal);

            Assert.IsTrue(entry >= 0);
            Assert.IsTrue(unsubscribe > entry);
            Assert.IsTrue(reveal > unsubscribe);
            Assert.IsTrue(activate > reveal);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_ActivationRequiresShowReturnVisibilityReleaseAndNoFailure()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void TryActivateAfterReveal", StringComparison.Ordinal);
            int end = code.IndexOf("private void FailClosedStaging", start, StringComparison.Ordinal);
            string activation = code[start..end];

            StringAssert.Contains(activation, "if (_interaction.IsDismissed ||");
            StringAssert.Contains(activation, "!_activationPending || !_initialShowReturned || !_visibilityReleased || _stagingFailed)");
            int pendingClear = activation.IndexOf("_activationPending = false;", StringComparison.Ordinal);
            int activate = activation.IndexOf("Activate();", StringComparison.Ordinal);
            int focus = activation.IndexOf("Focus();", StringComparison.Ordinal);
            int listFocus = activation.IndexOf("ProfileList.Focus();", StringComparison.Ordinal);

            Assert.IsTrue(pendingClear >= 0);
            Assert.IsTrue(activate > pendingClear);
            Assert.IsTrue(focus > activate);
            Assert.IsTrue(listFocus > focus);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticRecovery_HasNoRegionTranslationOrAnimatedDismissRoute()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(xaml, "Opacity=\"0\"");
            Assert.IsFalse(xaml.Contains("PresentationTransform", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("QuickSwitchPresentationMotion", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("SetWindowRgn", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("CompositionTarget.Rendering", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("RequestAnimatedDismiss", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("_dismissStarted", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_StaticRecovery_PreservesMissedLoadedCatchupAndImmediateClose()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(code, "if (IsLoaded && !_visibilityReleased && !_stagingFailed)");
            StringAssert.Contains(code, "TryCompleteInitialStaticFinalReveal();");
            StringAssert.Contains(xaml, "ShowActivated=\"False\"");
            StringAssert.Contains(code, "void IQuickSwitchOverlay.Close() => Close();");

            int interactionStart = code.IndexOf("new QuickSwitchInteractionController(", StringComparison.Ordinal);
            int interactionEnd = code.IndexOf(");", interactionStart, StringComparison.Ordinal);
            string interaction = code[interactionStart..interactionEnd];

            StringAssert.Contains(interaction, "Close,");
            Assert.IsFalse(interaction.Contains("RequestAnimatedDismiss", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_OpaqueClosure_RemovesAcrylicRuntimePaths()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));
            string backdrop = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Interop", "SystemBackdrop.cs")));

            foreach (string forbidden in new[]
            {
                "ShouldUseQuickSwitchAcrylic",
                "TryAcquireCompositionTarget",
                "RestoreCompositionTarget",
                "ResetNativeBackdrop",
                "TryGetSystemBackdropType",
                "TransientSystemBackdropType",
                "EvaluateQuickSwitchPolicy",
                "EvaluatePresentation",
                "RevealBackdrop",
                "RefreshAcrylicPresentation",
                "QuickSwitchAcrylicMaterial",
                "DesktopAcrylicController",
                "DesktopWindowTarget",
                "DwmExtendFrameIntoClientArea",
                "DwmwaSystemBackdropType"
            })
            {
                Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), forbidden);
                Assert.IsFalse(backdrop.Contains(forbidden, StringComparison.Ordinal), forbidden);
            }

            Color fallback = QuickSwitchOverlayWindow.GetOpaqueFallbackColor(Color.FromArgb(56, 10, 20, 30));
            Assert.AreEqual(255, fallback.A);
            Assert.AreEqual(10, fallback.R);
            Assert.AreEqual(20, fallback.G);
            Assert.AreEqual(30, fallback.B);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_ThemeAndPreferenceRefresh_ReapplyOpaqueSurface()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int themeStart = code.IndexOf("private void OnThemeChanged", StringComparison.Ordinal);
            int themeEnd = code.IndexOf("private void RefreshGeneratedLayoutImages", themeStart, StringComparison.Ordinal);
            string theme = code[themeStart..themeEnd];
            StringAssert.Contains(theme, "RefreshGeneratedLayoutImages();");
            StringAssert.Contains(theme, "ApplyOpaqueBrushes();");

            int preferenceStart = code.IndexOf("private void OnUserPreferenceChanged", StringComparison.Ordinal);
            int preferenceEnd = code.IndexOf("private void RefreshOuterEdgePolicy", preferenceStart, StringComparison.Ordinal);
            string preference = code[preferenceStart..preferenceEnd];
            StringAssert.Contains(preference, "PositionOnWorkArea();");
            StringAssert.Contains(preference, "RefreshOuterEdgePolicy(");
            StringAssert.Contains(preference, "ApplyOpaqueBrushes();");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_ProfileListRemainsBoundedAndVerticallyScrollable()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml")));

            StringAssert.Contains(xaml, "MaxHeight=\"600\"");
            StringAssert.Contains(xaml, "x:Name=\"ProfileList\"");
            StringAssert.Contains(xaml, "MaxHeight=\"486\"");
            StringAssert.Contains(xaml, "ScrollViewer.VerticalScrollBarVisibility=\"Auto\"");
            StringAssert.Contains(xaml, "Style=\"{StaticResource OverlayScrollViewerStyle}\"");
            StringAssert.Contains(xaml, "presentation:SmoothScrollBehavior.IsEnabled=\"True\"");
            StringAssert.Contains(xaml, "Padding=\"0,0,12,0\"");
            StringAssert.Contains(xaml, "Margin=\"0,0,-12,0\"");
            StringAssert.Contains(xaml, "VerticalScrollBarVisibility=\"Auto\"");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_SettingsUiAndHandlers_PreserveRetainedSource()
        {
            string xaml = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml")));
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml.cs")));

            StringAssert.Contains(xaml, "Content=\"Use active monitor\"");
            StringAssert.Contains(xaml, "Content=\"Mouse position\"");
            StringAssert.Contains(xaml, "Content=\"Window focus\"");
            StringAssert.Contains(xaml, "x:Name=\"QuickSwitchActiveMonitorSourcePanel\"");
            StringAssert.Contains(xaml, "Margin=\"20,0,0,8\"");

            StringAssert.Contains(code, "settings.QuickSwitchActiveMonitorSource == QuickSwitchActiveMonitorSource.MousePosition");
            StringAssert.Contains(code, "settings.QuickSwitchActiveMonitorSource == QuickSwitchActiveMonitorSource.WindowFocus");
            StringAssert.Contains(code, "QuickSwitchActiveMonitorSourcePanel.IsEnabled = settings.QuickSwitchUseActiveMonitor");
            StringAssert.Contains(code, "SetQuickSwitchUseActiveMonitorAsync(");
            StringAssert.Contains(code, "SetQuickSwitchActiveMonitorSourceAsync(source)");
            Assert.IsFalse(code.Contains("_settingsService.Settings.QuickSwitchUseActiveMonitor =", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("_settingsService.Settings.QuickSwitchActiveMonitorSource =", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_FirstShowCapturesSettingsAndOpenInstanceStaysStable()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int refreshStart = code.IndexOf("internal void RefreshAndActivate()", StringComparison.Ordinal);
            int refreshEnd = code.IndexOf("event EventHandler IQuickSwitchOverlay.Closed", refreshStart, StringComparison.Ordinal);
            string refresh = code[refreshStart..refreshEnd];
            StringAssert.Contains(refresh, "if (!IsVisible)");
            StringAssert.Contains(refresh, "QuickSwitchWindowPlacement.CaptureTarget(");
            StringAssert.Contains(refresh, "_settings.ShouldQuickSwitchUseActiveMonitor()");
            StringAssert.Contains(refresh, "_settings.GetQuickSwitchActiveMonitorSource()");

            int settingsStart = code.IndexOf("private void OnSettingsChanged", StringComparison.Ordinal);
            int settingsEnd = code.IndexOf("private void OnUserPreferenceChanged", settingsStart, StringComparison.Ordinal);
            string settingsChanged = code[settingsStart..settingsEnd];
            Assert.IsFalse(settingsChanged.Contains("CaptureTarget", StringComparison.Ordinal));
            Assert.IsFalse(settingsChanged.Contains("PositionOnWorkArea", StringComparison.Ordinal));

            int preferenceStart = code.IndexOf("private void OnUserPreferenceChanged", StringComparison.Ordinal);
            int preferenceEnd = code.IndexOf("private void RefreshOuterEdgePolicy", preferenceStart, StringComparison.Ordinal);
            string preference = code[preferenceStart..preferenceEnd];
            StringAssert.Contains(preference, "CaptureForWindow(this)");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchMonitorTargeting_ProvidersAndSharedCapture_ArePinned()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchWindowPlacement.cs")));

            StringAssert.Contains(code, "MonitorFromPoint(origin, MonitorDefaultToPrimary)");
            StringAssert.Contains(code, "GetCursorPos(out var point)");
            StringAssert.Contains(code, "MonitorFromPoint(point, MonitorDefaultToNearest)");
            StringAssert.Contains(code, "GetForegroundWindow()");
            StringAssert.Contains(code, "MonitorFromWindow(hwnd, MonitorDefaultToNearest)");
            StringAssert.Contains(code, "return Capture(");
            StringAssert.Contains(code, "uint dpi = GetEffectiveDpi(monitor)");
            StringAssert.Contains(code, "ProbeSelectedMonitorShellTaskbar(monitor, monitorRect)");
            StringAssert.Contains(code, "FindWindow(\"Shell_TrayWnd\", null)");
            StringAssert.Contains(code, "FindWindowEx(IntPtr.Zero, previous, \"Shell_SecondaryTrayWnd\", null)");
            Assert.IsFalse(code.Contains("AbmGetTaskbarPos", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitchPlacementDiagnostic_ReportsTargetTaskbarAndStaticFinalProof()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(code, "targetPolicy=Primary");
            StringAssert.Contains(code, "targetPolicy=Active targetSource=");
            StringAssert.Contains(code, "targetFallback=Primary");
            StringAssert.Contains(code, "shellTaskbar=");
            StringAssert.Contains(code, "rcMonitor=");
            StringAssert.Contains(code, "rcWork=");
            StringAssert.Contains(code, "effectiveWork=");
            StringAssert.Contains(code, "targetDpi=");
            StringAssert.Contains(code, "foregroundDpi=");
            StringAssert.Contains(code, "entranceRoute=StaticFinal");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_ContentRendered_OnlySchedulesSettledDiagnostic()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int start = code.IndexOf("private void OnInitialContentRendered", StringComparison.Ordinal);
            int end = code.IndexOf("private void OnThemeChanged", start, StringComparison.Ordinal);
            string rendered = code[start..end];

            StringAssert.Contains(rendered, "ContentRendered -= OnInitialContentRendered;");
            StringAssert.Contains(rendered, "LogSettledPresentation");
            Assert.IsFalse(rendered.Contains("Reevaluate", StringComparison.Ordinal));
            Assert.IsFalse(rendered.Contains("Acrylic", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_LoadedSubscription_FollowsOpaqueFrameInitialization()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            int sourceStart = code.IndexOf("protected override void OnSourceInitialized", StringComparison.Ordinal);
            int sourceEnd = code.IndexOf("protected override void OnContentRendered", sourceStart, StringComparison.Ordinal);
            string source = code[sourceStart..sourceEnd];

            int frame = source.IndexOf("TryPrepareQuickSwitchNativeFrame(hwnd)", StringComparison.Ordinal);
            int opaque = source.IndexOf("ApplyOpaqueBrushes();", StringComparison.Ordinal);
            int loaded = source.IndexOf("Loaded += OnInitialLoaded;", StringComparison.Ordinal);

            Assert.IsTrue(frame >= 0);
            Assert.IsTrue(opaque > frame);
            Assert.IsTrue(loaded > opaque);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_SettledDiagnostic_ReportsOnlyStaticFrameAndOpaqueSurface()
        {
            string code = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "QuickSwitchOverlayWindow.xaml.cs")));

            StringAssert.Contains(code, "Quick Switch settled | entranceRoute=StaticFinal");
            StringAssert.Contains(code, "finalPlacementSucceeded=");
            StringAssert.Contains(code, "finalRectExact=");
            StringAssert.Contains(code, "windowBackgroundAlpha=");
            StringAssert.Contains(code, "backdropHostAlpha=");
            StringAssert.Contains(code, "TryGetWindowStyleState(hwnd)");
            StringAssert.Contains(code, "TryGetFrameMetrics(hwnd");
            StringAssert.Contains(code, "TryGetVisibleFrameBorderThickness(hwnd");
            StringAssert.Contains(code, "TryGetWindowCornerPreference(hwnd");
            Assert.IsFalse(code.Contains("requestedBackdrop=", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("nativeBackdrop", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("compositionAlpha", StringComparison.Ordinal));
            Assert.IsFalse(code.Contains("materialAuthority", StringComparison.Ordinal));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void QuickSwitch_ProjectsInstallerAndSettingsSurface_HaveNoAcrylicProductDependency()
        {
            string appProject = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager", "DisplayProfileManager.csproj")));
            string testProject = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager.Tests", "DisplayProfileManager.Tests.csproj")));
            string setup = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager", "DisplayProfileManager.Setup.iss")));
            string settings = File.ReadAllText(FindRepositoryFile(
                Path.Combine("DisplayProfileManager",  "UI", "Windows", "SettingsWindow.xaml")));

            StringAssert.Contains(appProject, "<TargetFramework>net10.0-windows</TargetFramework>");
            StringAssert.Contains(testProject, "<TargetFramework>net10.0-windows</TargetFramework>");
            Assert.IsFalse(appProject.Contains("Microsoft.WindowsAppSDK", StringComparison.Ordinal));
            Assert.IsFalse(appProject.Contains("WindowsAppSdk", StringComparison.Ordinal));
            Assert.IsFalse(testProject.Contains("WindowsAppSdk", StringComparison.Ordinal));
            Assert.IsFalse(setup.Contains("WindowsAppRuntime", StringComparison.Ordinal));
            Assert.IsFalse(setup.Contains("Windows App Runtime", StringComparison.Ordinal));
            Assert.IsFalse(settings.Contains("Acrylic", StringComparison.Ordinal));
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
