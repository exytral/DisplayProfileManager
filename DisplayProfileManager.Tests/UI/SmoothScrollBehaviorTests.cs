using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using DisplayProfileManager.UI.Presentation;

namespace DisplayProfileManager.Tests.UI
{
    [TestClass]
    public class SmoothScrollBehaviorTests
    {
        [TestCleanup]
        public void Cleanup() => SmoothScrollBehavior.ResetTestOverrides();

        [TestMethod]
        [TestCategory("Unit")]
        public void NormalizeWheelDelta_OrdinaryAndFractionalDelta_PreservesSignedUnits()
        {
            Assert.AreEqual(1.0, SmoothScrollBehavior.NormalizeWheelDelta(120), 0.0001);
            Assert.AreEqual(-1.0, SmoothScrollBehavior.NormalizeWheelDelta(-120), 0.0001);
            Assert.AreEqual(0.25, SmoothScrollBehavior.NormalizeWheelDelta(30), 0.0001);
            Assert.AreEqual(-0.25, SmoothScrollBehavior.NormalizeWheelDelta(-30), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DistancePerWheelUnit_ConfiguredLines_UsesSixteenDipsPerLine()
        {
            Assert.AreEqual(48.0, SmoothScrollBehavior.GetDistancePerWheelUnit(3, 500), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DistancePerWheelUnit_ZeroLines_DisablesMovement()
        {
            Assert.AreEqual(0.0, SmoothScrollBehavior.GetDistancePerWheelUnit(0, 500), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DistancePerWheelUnit_PageMode_UsesReceiverViewport()
        {
            Assert.AreEqual(360.0, SmoothScrollBehavior.GetDistancePerWheelUnit(-1, 360), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WheelRequest_PositiveDelta_MovesTowardTop()
        {
            var request = SmoothScrollBehavior.ComputeWheelRequest(100, 500, 1, 48);

            Assert.AreEqual(52.0, request.TargetOffset, 0.0001);
            Assert.AreEqual(1.0, request.ConsumedUnits, 0.0001);
            Assert.AreEqual(0.0, request.ResidualUnits, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WheelRequest_NegativeDelta_MovesTowardBottom()
        {
            var request = SmoothScrollBehavior.ComputeWheelRequest(100, 500, -1, 48);

            Assert.AreEqual(148.0, request.TargetOffset, 0.0001);
            Assert.AreEqual(-1.0, request.ConsumedUnits, 0.0001);
            Assert.AreEqual(0.0, request.ResidualUnits, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WheelRequest_TopAndBottom_ClampExactly()
        {
            var top = SmoothScrollBehavior.ComputeWheelRequest(10, 500, 1, 48);
            var bottom = SmoothScrollBehavior.ComputeWheelRequest(490, 500, -1, 48);

            Assert.AreEqual(0.0, top.TargetOffset, 0.0001);
            Assert.AreEqual(500.0, bottom.TargetOffset, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WheelRequest_BoundaryCrossing_HandsOnlyResidualFractionToAncestor()
        {
            var child = SmoothScrollBehavior.ComputeWheelRequest(90, 100, -1, 40);
            var parent = SmoothScrollBehavior.ComputeWheelRequest(200, 1000, child.ResidualUnits, 100);

            Assert.AreEqual(100.0, child.TargetOffset, 0.0001);
            Assert.AreEqual(-0.25, child.ConsumedUnits, 0.0001);
            Assert.AreEqual(-0.75, child.ResidualUnits, 0.0001);
            Assert.AreEqual(275.0, parent.TargetOffset, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void WheelRequest_AtBoundary_HandsFullGestureToAncestor()
        {
            var child = SmoothScrollBehavior.ComputeWheelRequest(100, 100, -0.5, 40);

            Assert.AreEqual(100.0, child.TargetOffset, 0.0001);
            Assert.AreEqual(0.0, child.ConsumedUnits, 0.0001);
            Assert.AreEqual(-0.5, child.ResidualUnits, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BaseTarget_SameDirectionBurst_UsesAccumulatedTarget()
        {
            double target = SmoothScrollBehavior.SelectBaseTarget(60, 140, true, -1, -1);

            Assert.AreEqual(140.0, target, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BaseTarget_DirectionReversal_RebasesToActualOffset()
        {
            double target = SmoothScrollBehavior.SelectBaseTarget(60, 140, true, -1, 1);

            Assert.AreEqual(60.0, target, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Duration_RemainingDistance_ClampsToAcceptedBounds()
        {
            Assert.AreEqual(100.0, SmoothScrollBehavior.CalculateDurationMs(0), 0.0001);
            Assert.AreEqual(125.0, SmoothScrollBehavior.CalculateDurationMs(100), 0.0001);
            Assert.AreEqual(180.0, SmoothScrollBehavior.CalculateDurationMs(1000), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Ease_EndpointsAndMidpoints_AreExactAndMonotonic()
        {
            double start = SmoothScrollBehavior.Ease(0);
            double first = SmoothScrollBehavior.Ease(0.25);
            double second = SmoothScrollBehavior.Ease(0.5);
            double end = SmoothScrollBehavior.Ease(1);

            Assert.AreEqual(0.0, start, 0.0001);
            Assert.IsTrue(first > start);
            Assert.IsTrue(second > first);
            Assert.IsTrue(end > second);
            Assert.AreEqual(1.0, end, 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RenderCadence_PendingInternalOffset_BackpressuresUntilAcknowledged()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 25);
                var first = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);

                SmoothScrollBehavior.RenderFrameForTests(viewer, 50);
                var blocked = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);

                Assert.IsTrue(first.HasPendingInternalOffset);
                Assert.AreEqual(first.PendingInternalOffset, blocked.PendingInternalOffset, 0.0001);

                FlushLayout(viewer);
                var acknowledged = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(acknowledged.IsActive);
                Assert.IsFalse(acknowledged.HasPendingInternalOffset);

                SmoothScrollBehavior.RenderFrameForTests(viewer, 75);
                var next = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(next.HasPendingInternalOffset);
                Assert.IsTrue(next.PendingInternalOffset > first.PendingInternalOffset);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClampOffset_ExtentShrink_ClampsTargetAndTerminalOffset()
        {
            Assert.AreEqual(120.0, SmoothScrollBehavior.ClampOffset(240, 120), 0.0001);
            Assert.AreEqual(0.0, SmoothScrollBehavior.ClampOffset(-30, 120), 0.0001);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AncestorOnly_RoutedWheel_LeavesChildUnchangedAndMovesParentOnce()
        {
            RunOnSta(() =>
            {
                var (parent, child) = CreateNestedScrollers();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                SmoothScrollBehavior.SetIsEnabled(parent, true);
                SmoothScrollBehavior.SetIsEnabled(child, true);
                SmoothScrollBehavior.SetWheelRouting(child, SmoothScrollWheelRouting.AncestorOnly);

                child.ScrollToVerticalOffset(40);
                parent.ScrollToVerticalOffset(60);
                FlushLayout(parent);
                double childBefore = child.VerticalOffset;
                double parentBefore = parent.VerticalOffset;

                var wheel = RaisePreviewWheel(child, -120);
                FlushLayout(parent);

                Assert.IsTrue(wheel.Handled);
                Assert.AreEqual(childBefore, child.VerticalOffset, 0.0001);
                Assert.IsTrue(parent.VerticalOffset > parentBefore);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_VisualTraversal_FindsRealizedDescendant()
        {
            RunOnSta(() =>
            {
                var (viewer, comboBox) = CreateScrollableViewerWithComboBox();

                bool found = SmoothScrollBehavior.HasOpenComboBoxForTests(
                    viewer,
                    candidate => ReferenceEquals(candidate, comboBox));

                Assert.IsTrue(found);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_RoutedWheel_BlocksAncestorUntilDropdownCloses()
        {
            RunOnSta(() =>
            {
                var (viewer, comboBox) = CreateScrollableViewerWithComboBox();
                var window = ShowInWindow(viewer);
                try
                {
                    SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                    SmoothScrollBehavior.SetIsEnabled(viewer, true);
                    viewer.ScrollToVerticalOffset(80);
                    FlushLayout(viewer);
                    double before = viewer.VerticalOffset;

                    comboBox.IsDropDownOpen = true;
                    FlushLayout(viewer);
                    Assert.IsTrue(GetComboBoxPopup(comboBox).IsOpen);

                    var blocked = RaisePreviewWheel(viewer, -120);
                    FlushLayout(viewer);

                    Assert.IsTrue(blocked.Handled);
                    Assert.AreEqual(before, viewer.VerticalOffset, 0.0001);
                    Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);

                    comboBox.IsDropDownOpen = false;
                    FlushLayout(viewer);

                    var resumed = RaisePreviewWheel(viewer, -120);
                    FlushLayout(viewer);

                    Assert.IsTrue(resumed.Handled);
                    Assert.IsTrue(viewer.VerticalOffset > before);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_RealPopupLifecycle_OwnsWheelAndDetachesCleanly()
        {
            RunOnSta(() =>
            {
                var (viewer, comboBox) = CreateScrollableViewerWithComboBox();
                var window = ShowInWindow(viewer);
                try
                {
                    SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                    SmoothScrollBehavior.SetIsEnabled(viewer, true);
                    Assert.AreEqual(1, SmoothScrollBehavior.GetComboBoxSubscriptionCountForTests(viewer));

                    comboBox.IsDropDownOpen = true;
                    FlushLayout(viewer);

                    var popup = GetComboBoxPopup(comboBox);
                    Assert.IsTrue(popup.IsOpen);
                    Assert.AreEqual(1, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));

                    var popupRoot = (FrameworkElement)popup.Child;
                    var popupScroller = FindDescendantScrollViewer(popupRoot);
                    Assert.IsNotNull(popupScroller);
                    FlushLayout(popupRoot);

                    double popupBefore = popupScroller.VerticalOffset;
                    SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                    Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);

                    var popupWheel = RaisePreviewWheel(popupScroller, -120);
                    FlushLayout(popupRoot);

                    Assert.IsTrue(popupWheel.Handled);
                    Assert.IsTrue(popupScroller.VerticalOffset > popupBefore);
                    Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);

                    popupScroller.ScrollToVerticalOffset(popupScroller.ScrollableHeight);
                    FlushLayout(popupRoot);
                    double edge = popupScroller.VerticalOffset;

                    var edgeWheel = RaisePreviewWheel(popupScroller, -120);
                    FlushLayout(popupRoot);

                    Assert.IsTrue(edgeWheel.Handled);
                    Assert.AreEqual(edge, popupScroller.VerticalOffset, 0.0001);

                    comboBox.IsDropDownOpen = false;
                    FlushLayout(viewer);
                    Assert.AreEqual(0, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));

                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        var replacementContent = new Border { Width = 120, Height = 80 };
                        window.Content = replacementContent;
                        PumpDispatcher(window.Dispatcher);
                        window.UpdateLayout();
                        PumpDispatcher(window.Dispatcher);

                        Assert.IsFalse(viewer.IsLoaded);
                        Assert.AreEqual(0, SmoothScrollBehavior.GetComboBoxSubscriptionCountForTests(viewer));
                        Assert.AreEqual(0, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));

                        viewer.InvalidateMeasure();
                        viewer.UpdateLayout();
                        replacementContent.InvalidateMeasure();
                        window.UpdateLayout();
                        PumpDispatcher(window.Dispatcher);
                        PumpDispatcher(window.Dispatcher);

                        Assert.AreEqual(0, SmoothScrollBehavior.GetComboBoxSubscriptionCountForTests(viewer));
                        Assert.AreEqual(0, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));

                        window.Content = viewer;
                        PumpDispatcher(window.Dispatcher);
                        viewer.UpdateLayout();
                        PumpDispatcher(window.Dispatcher);

                        Assert.IsTrue(viewer.IsLoaded);
                        Assert.AreEqual(1, SmoothScrollBehavior.GetComboBoxSubscriptionCountForTests(viewer));

                        comboBox.IsDropDownOpen = true;
                        FlushLayout(viewer);
                        Assert.AreEqual(1, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));

                        var reloadedPopup = GetComboBoxPopup(comboBox);
                        var reloadedRoot = (FrameworkElement)reloadedPopup.Child;
                        var reloadedScroller = FindDescendantScrollViewer(reloadedRoot);
                        Assert.IsNotNull(reloadedScroller);
                        reloadedScroller.ScrollToTop();
                        FlushLayout(reloadedRoot);
                        double reloadedBefore = reloadedScroller.VerticalOffset;

                        var reloadedWheel = RaisePreviewWheel(reloadedScroller, -120);
                        FlushLayout(reloadedRoot);

                        Assert.IsTrue(reloadedWheel.Handled);
                        Assert.IsTrue(reloadedScroller.VerticalOffset > reloadedBefore);

                        comboBox.IsDropDownOpen = false;
                        FlushLayout(viewer);
                        Assert.AreEqual(0, SmoothScrollBehavior.GetPopupWheelSubscriptionCountForTests(viewer));
                        Assert.AreEqual(1, SmoothScrollBehavior.GetComboBoxSubscriptionCountForTests(viewer));
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_PopupWheel_MovesPopupScrollerWithoutAncestorRouting()
        {
            RunOnSta(() =>
            {
                var popupScroller = CreateScrollableViewer();
                popupScroller.ScrollToVerticalOffset(80);
                FlushLayout(popupScroller);
                double before = popupScroller.VerticalOffset;

                SmoothScrollBehavior.ScrollComboBoxPopupForTests(popupScroller, -120, 3);
                FlushLayout(popupScroller);

                Assert.IsTrue(popupScroller.VerticalOffset > before);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_PopupWheelAtEdge_StaysClamped()
        {
            RunOnSta(() =>
            {
                var popupScroller = CreateScrollableViewer();
                popupScroller.ScrollToVerticalOffset(popupScroller.ScrollableHeight);
                FlushLayout(popupScroller);
                double before = popupScroller.VerticalOffset;

                SmoothScrollBehavior.ScrollComboBoxPopupForTests(popupScroller, -120, 3);
                FlushLayout(popupScroller);

                Assert.AreEqual(before, popupScroller.VerticalOffset, 0.0001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_ActiveTrajectory_CancelsBeforeFurtherRenderedMotion()
        {
            RunOnSta(() =>
            {
                var (viewer, comboBox) = CreateScrollableViewerWithComboBox();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                viewer.ScrollToVerticalOffset(80);
                FlushLayout(viewer);
                double before = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);

                SmoothScrollBehavior.OpenComboBoxOverrideForTests = true;
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                FlushLayout(viewer);

                var state = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(state.IsActive);
                Assert.IsFalse(state.RenderingSubscribed);
                Assert.AreEqual(before, viewer.VerticalOffset, 0.0001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OpenComboBox_DetachedPopupScroller_RemainsNativeAndDoesNotMoveAncestor()
        {
            RunOnSta(() =>
            {
                var (viewer, comboBox) = CreateScrollableViewerWithComboBox();
                var popupScroller = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                viewer.ScrollToVerticalOffset(80);
                FlushLayout(viewer);
                double before = viewer.VerticalOffset;

                SmoothScrollBehavior.OpenComboBoxOverrideForTests = true;
                var wheel = RaisePreviewWheel(popupScroller, -120);
                FlushLayout(viewer);

                Assert.IsFalse(SmoothScrollBehavior.GetIsEnabled(popupScroller));
                Assert.IsFalse(wheel.Handled);
                Assert.AreEqual(before, viewer.VerticalOffset, 0.0001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExternalScrollChanged_ActiveTrajectory_CancelsAndRebases()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);

                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);

                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);

                var state = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(state.IsActive);
                Assert.IsFalse(state.RenderingSubscribed);
                Assert.AreEqual(viewer.VerticalOffset, state.TargetOffset, 0.0001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void DelayedInternalScrollChanged_AcknowledgesWithoutCancellingTrajectory()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 50);

                var queued = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(queued.IsActive);
                Assert.IsTrue(queued.HasPendingInternalOffset);

                FlushLayout(viewer);

                var acknowledged = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(acknowledged.IsActive);
                Assert.IsTrue(acknowledged.RenderingSubscribed);
                Assert.IsFalse(acknowledged.HasPendingInternalOffset);
                Assert.AreEqual(queued.PendingInternalOffset, viewer.VerticalOffset, 0.0001);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModifiedWheel_RoutedEvent_RemainsUnclaimed()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.KeyboardModifiersOverrideForTests = ModifierKeys.Control;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                viewer.ScrollToVerticalOffset(80);
                FlushLayout(viewer);
                double before = viewer.VerticalOffset;

                var wheel = RaisePreviewWheel(viewer, -120);
                FlushLayout(viewer);

                Assert.IsFalse(wheel.Handled);
                Assert.AreEqual(before, viewer.VerticalOffset, 0.0001);
                Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClientAreaAnimationDisabled_ImmediatePath_HasNoRenderingSubscription()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                var queued = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);

                Assert.IsFalse(queued.RenderingSubscribed);
                Assert.IsTrue(queued.HasPendingInternalOffset);

                FlushLayout(viewer);
                var completed = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(completed.IsActive);
                Assert.IsFalse(completed.RenderingSubscribed);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ClientAreaAnimationDisabledDuringActiveTrajectory_CompletesWithoutRenderingSubscription()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).RenderingSubscribed);

                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                SmoothScrollBehavior.RenderFrameForTests(viewer, 20);

                var transitioned = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(transitioned.RenderingSubscribed);
                Assert.IsTrue(transitioned.HasPendingInternalOffset);

                FlushLayout(viewer);
                Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).IsActive);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Disable_ActiveTrajectory_RemovesStateAndRenderingSubscription()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);

                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).RenderingSubscribed);

                SmoothScrollBehavior.SetIsEnabled(viewer, false);

                Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).Exists);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Unload_ActiveTrajectory_CancelsRenderingSubscription()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);

                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).RenderingSubscribed);

                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

                var state = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(state.IsActive);
                Assert.IsFalse(state.RenderingSubscribed);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Reversal_PendingInternalOffset_SettlesBeforeReversedMotion()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                double transitionOffset = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                var oldRequest = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(oldRequest.HasPendingInternalOffset);
                Assert.IsTrue(oldRequest.PendingInternalOffset > transitionOffset);

                SmoothScrollBehavior.ApplyGestureForTests(viewer, 1, 3);
                var reversed = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.AreEqual(transitionOffset, reversed.PendingInternalOffset, 0.0001);
                Assert.IsTrue(reversed.TargetOffset < transitionOffset);

                bool oldDirectionObserved = false;
                viewer.ScrollChanged += (_, e) =>
                {
                    if (e.VerticalOffset > transitionOffset + 0.0001)
                        oldDirectionObserved = true;
                };

                FlushLayout(viewer);

                var settled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(oldDirectionObserved);
                Assert.AreEqual(transitionOffset, viewer.VerticalOffset, 0.0001);
                Assert.IsTrue(settled.IsActive);
                Assert.IsFalse(settled.HasPendingInternalOffset);

                SmoothScrollBehavior.RenderFrameForTests(viewer, 50);
                FlushLayout(viewer);

                Assert.IsTrue(viewer.VerticalOffset < transitionOffset);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReplacementToCurrent_NoVerticalScrollChanged_SettlesAndReleasesBackpressure()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                double transitionOffset = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).HasPendingInternalOffset);

                int verticalScrollChangedCount = 0;
                viewer.ScrollChanged += (_, e) =>
                {
                    if (Math.Abs(e.VerticalChange) > 0.0001)
                        verticalScrollChangedCount++;
                };

                SmoothScrollBehavior.ApplyGestureForTests(viewer, 1, 3);
                Assert.AreEqual(transitionOffset, SmoothScrollBehavior.GetStateSnapshotForTests(viewer).PendingInternalOffset, 0.0001);

                FlushLayout(viewer);

                var settled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.AreEqual(0, verticalScrollChangedCount);
                Assert.IsFalse(settled.HasPendingInternalOffset);
                Assert.IsTrue(settled.IsActive);

                SmoothScrollBehavior.RenderFrameForTests(viewer, 50);
                FlushLayout(viewer);

                Assert.IsTrue(viewer.VerticalOffset < transitionOffset);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ModifiedWheel_PendingInternalOffset_SettlesBeforeNativeTakeover()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                double transitionOffset = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).HasPendingInternalOffset);

                bool staleMovementObserved = false;
                viewer.ScrollChanged += (_, e) =>
                {
                    if (e.VerticalOffset > transitionOffset + 0.0001)
                        staleMovementObserved = true;
                };

                SmoothScrollBehavior.KeyboardModifiersOverrideForTests = ModifierKeys.Control;
                var wheel = RaisePreviewWheel(viewer, -120);
                var cancelled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);

                Assert.IsFalse(wheel.Handled);
                Assert.IsFalse(cancelled.IsActive);
                Assert.AreEqual(transitionOffset, cancelled.PendingInternalOffset, 0.0001);

                FlushLayout(viewer);

                var settled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(staleMovementObserved);
                Assert.AreEqual(transitionOffset, viewer.VerticalOffset, 0.0001);
                Assert.IsFalse(settled.IsActive);
                Assert.IsFalse(settled.HasPendingInternalOffset);
                Assert.IsFalse(settled.RenderingSubscribed);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ReducedMotion_PendingInternalOffset_ReplacesWithTerminalTarget()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                double target = SmoothScrollBehavior.GetStateSnapshotForTests(viewer).TargetOffset;
                SmoothScrollBehavior.RenderFrameForTests(viewer, 30);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).HasPendingInternalOffset);

                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = false;
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                var replaced = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);

                Assert.AreEqual(target, replaced.PendingInternalOffset, 0.0001);
                Assert.IsFalse(replaced.RenderingSubscribed);

                FlushLayout(viewer);

                var settled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.AreEqual(target, viewer.VerticalOffset, 0.0001);
                Assert.IsFalse(settled.IsActive);
                Assert.IsFalse(settled.HasPendingInternalOffset);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Disable_PendingInternalOffset_SettlesBeforeStateRemoval()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                double transitionOffset = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).HasPendingInternalOffset);

                bool staleMovementObserved = false;
                viewer.ScrollChanged += (_, e) =>
                {
                    if (e.VerticalOffset > transitionOffset + 0.0001)
                        staleMovementObserved = true;
                };

                SmoothScrollBehavior.SetIsEnabled(viewer, false);
                var settling = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsTrue(settling.Exists);
                Assert.IsTrue(settling.HasPendingInternalOffset);
                Assert.IsFalse(settling.RenderingSubscribed);

                FlushLayout(viewer);

                Assert.IsFalse(staleMovementObserved);
                Assert.AreEqual(transitionOffset, viewer.VerticalOffset, 0.0001);
                Assert.IsFalse(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).Exists);
            });
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Unload_PendingInternalOffset_SettlesWithoutStaleMovement()
        {
            RunOnSta(() =>
            {
                var viewer = CreateScrollableViewer();
                viewer.ScrollToVerticalOffset(120);
                FlushLayout(viewer);
                SmoothScrollBehavior.ClientAreaAnimationOverrideForTests = true;
                SmoothScrollBehavior.SetIsEnabled(viewer, true);
                double transitionOffset = viewer.VerticalOffset;

                SmoothScrollBehavior.ApplyGestureForTests(viewer, -1, 3);
                SmoothScrollBehavior.RenderFrameForTests(viewer, 40);
                Assert.IsTrue(SmoothScrollBehavior.GetStateSnapshotForTests(viewer).HasPendingInternalOffset);

                bool staleMovementObserved = false;
                viewer.ScrollChanged += (_, e) =>
                {
                    if (e.VerticalOffset > transitionOffset + 0.0001)
                        staleMovementObserved = true;
                };

                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                var cancelled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(cancelled.IsActive);
                Assert.IsTrue(cancelled.HasPendingInternalOffset);
                Assert.IsFalse(cancelled.RenderingSubscribed);

                FlushLayout(viewer);

                var settled = SmoothScrollBehavior.GetStateSnapshotForTests(viewer);
                Assert.IsFalse(staleMovementObserved);
                Assert.AreEqual(transitionOffset, viewer.VerticalOffset, 0.0001);
                Assert.IsTrue(settled.Exists);
                Assert.IsFalse(settled.IsActive);
                Assert.IsFalse(settled.HasPendingInternalOffset);
            });
        }

        private static ScrollViewer CreateScrollableViewer()
        {
            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Width = 180, Height = 600 }
            };

            Layout(viewer, new Size(200, 100));
            return viewer;
        }

        private static (ScrollViewer Viewer, ComboBox ComboBox) CreateScrollableViewerWithComboBox()
        {
            var comboBox = new ComboBox
            {
                Width = 160,
                ItemsSource = CreateComboBoxItems(),
                SelectedIndex = 0,
                MaxDropDownHeight = 96
            };
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = 120 });
            stack.Children.Add(comboBox);
            stack.Children.Add(new Border { Height = 600 });

            var viewer = new ScrollViewer
            {
                Width = 200,
                Height = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack
            };

            Layout(viewer, new Size(200, 140));
            return (viewer, comboBox);
        }

        private static Window ShowInWindow(FrameworkElement content)
        {
            var window = new Window
            {
                Width = 320,
                Height = 240,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = content
            };
            window.Show();
            PumpDispatcher(window.Dispatcher);
            content.UpdateLayout();
            return window;
        }

        private static string[] CreateComboBoxItems()
        {
            var items = new string[30];
            for (int i = 0; i < items.Length; i++)
                items[i] = $"Item {i + 1}";

            return items;
        }

        private static Popup GetComboBoxPopup(ComboBox comboBox)
        {
            comboBox.ApplyTemplate();
            return comboBox.Template.FindName("Popup", comboBox) as Popup
                ?? comboBox.Template.FindName("PART_Popup", comboBox) as Popup
                ?? throw new AssertFailedException("ComboBox template popup was not found");
        }

        private static ScrollViewer FindDescendantScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer)
            {
                return viewer;
            }

            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var found = FindDescendantScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static (ScrollViewer Parent, ScrollViewer Child) CreateNestedScrollers()
        {
            var child = new ScrollViewer
            {
                Width = 180,
                Height = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Width = 160, Height = 500 }
            };
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = 100 });
            stack.Children.Add(child);
            stack.Children.Add(new Border { Height = 700 });

            var parent = new ScrollViewer
            {
                Width = 200,
                Height = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stack
            };

            Layout(parent, new Size(200, 220));
            return (parent, child);
        }

        private static MouseWheelEventArgs RaisePreviewWheel(UIElement source, int delta)
        {
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent,
                Source = source
            };
            source.RaiseEvent(wheel);
            return wheel;
        }

        private static void Layout(FrameworkElement root, Size size)
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            PumpDispatcher(root.Dispatcher);
            root.UpdateLayout();
        }

        private static void FlushLayout(FrameworkElement root)
        {
            root.UpdateLayout();
            PumpDispatcher(root.Dispatcher);
            root.UpdateLayout();
            PumpDispatcher(root.Dispatcher);
        }

        private static void PumpDispatcher(Dispatcher dispatcher)
        {
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        private static void RunOnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    SmoothScrollBehavior.SuppressRenderingCallbacksForTests = true;
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    SmoothScrollBehavior.ResetTestOverrides();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
