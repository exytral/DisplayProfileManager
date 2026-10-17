using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace DisplayProfileManager.UI.Presentation
{
    public enum SmoothScrollWheelRouting
    {
        SelfThenAncestor,
        AncestorOnly
    }

    public static class SmoothScrollBehavior
    {
        private const double LineDistance = 16.0;
        private const double MinimumDurationMs = 100.0;
        private const double MaximumDurationMs = 180.0;
        private const double Epsilon = 0.0001;
        private static readonly ConditionalWeakTable<ScrollViewer, AnimationState> States = [];

        internal static bool? ClientAreaAnimationOverrideForTests;
        internal static ModifierKeys? KeyboardModifiersOverrideForTests;
        internal static bool? OpenComboBoxOverrideForTests;
        internal static bool SuppressRenderingCallbacksForTests;

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty WheelRoutingProperty = DependencyProperty.RegisterAttached(
            "WheelRouting", typeof(SmoothScrollWheelRouting), typeof(SmoothScrollBehavior),
            new PropertyMetadata(SmoothScrollWheelRouting.SelfThenAncestor));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        public static SmoothScrollWheelRouting GetWheelRouting(DependencyObject element) =>
            (SmoothScrollWheelRouting)element.GetValue(WheelRoutingProperty);

        public static void SetWheelRouting(DependencyObject element, SmoothScrollWheelRouting value) =>
            element.SetValue(WheelRoutingProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer viewer) return;

            if ((bool)e.NewValue)
            {
                var state = States.GetOrCreateValue(viewer);
                state.RemoveWhenSettled = false;
                viewer.PreviewMouseWheel -= Viewer_PreviewMouseWheel;
                viewer.PreviewMouseWheel += Viewer_PreviewMouseWheel;
                viewer.ScrollChanged -= Viewer_ScrollChanged;
                viewer.ScrollChanged += Viewer_ScrollChanged;
                state.LayoutUpdatedHandler ??= (_, _) => RefreshComboBoxSubscriptions(viewer, state);
                viewer.Loaded -= Viewer_Loaded;
                viewer.Loaded += Viewer_Loaded;
                viewer.Unloaded -= Viewer_Unloaded;
                viewer.Unloaded += Viewer_Unloaded;
                if (viewer.IsLoaded)
                    AttachComboBoxDiscovery(viewer, state);
            }
            else
            {
                viewer.PreviewMouseWheel -= Viewer_PreviewMouseWheel;
                viewer.Loaded -= Viewer_Loaded;
                viewer.Unloaded -= Viewer_Unloaded;

                if (!States.TryGetValue(viewer, out var state))
                {
                    viewer.ScrollChanged -= Viewer_ScrollChanged;
                    return;
                }

                DetachComboBoxDiscovery(viewer, state);
                DetachComboBoxSubscriptions(state);
                state.RemoveWhenSettled = true;
                CancelAnimation(viewer);
                if (!state.HasPendingInternalOffset)
                    RemoveSettledState(viewer);
            }
        }

        private static void Viewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            ModifierKeys modifiers = KeyboardModifiersOverrideForTests ?? Keyboard.Modifiers;

            if (modifiers != ModifierKeys.None)
            {
                CancelAnimation(viewer);
                return;
            }

            if (TryHandleOpenComboBoxWheel(viewer, e))
                return;

            if (HasNestedWheelOwner(viewer, e.OriginalSource as DependencyObject)) return;

            double units = NormalizeWheelDelta(e.Delta);
            if (Math.Abs(units) <= Epsilon)
            {
                e.Handled = true;
                return;
            }

            if (GetWheelRouting(viewer) == SmoothScrollWheelRouting.AncestorOnly)
            {
                var ancestor = FindEnabledAncestor(viewer);
                if (ancestor == null) return;

                ApplyGesture(ancestor, units, SystemParameters.WheelScrollLines);
                e.Handled = true;
                return;
            }

            double residual = ApplyGesture(viewer, units, SystemParameters.WheelScrollLines);
            if (Math.Abs(residual) > Epsilon)
            {
                var ancestor = FindEnabledAncestor(viewer);
                if (ancestor != null)
                    ApplyGesture(ancestor, residual, SystemParameters.WheelScrollLines);
            }

            e.Handled = true;
        }

        private static double ApplyGesture(ScrollViewer viewer, double normalizedUnits, int wheelScrollLines)
        {
            var state = States.GetOrCreateValue(viewer);

            if (wheelScrollLines == 0)
            {
                CancelAnimation(viewer);
                return 0;
            }

            double distancePerUnit = GetDistancePerWheelUnit(wheelScrollLines, viewer.ViewportHeight);
            if (distancePerUnit <= Epsilon)
            {
                return normalizedUnits;
            }

            int direction = Math.Sign(normalizedUnits);
            double baseTarget = SelectBaseTarget(
                viewer.VerticalOffset, state.TargetOffset, state.IsActive, state.LastWheelDirection, direction);

            var request = ComputeWheelRequest(baseTarget, viewer.ScrollableHeight, normalizedUnits, distancePerUnit);
            if (Math.Abs(request.ConsumedUnits) <= Epsilon)
            {
                return request.ResidualUnits;
            }

            StartAnimation(viewer, state, request.TargetOffset, direction);
            return request.ResidualUnits;
        }

        private static void StartAnimation(ScrollViewer viewer, AnimationState state, double targetOffset, int direction)
        {
            double actualOffset = ClampOffset(viewer.VerticalOffset, viewer.ScrollableHeight);
            targetOffset = ClampOffset(targetOffset, viewer.ScrollableHeight);

            if (state.IsActive && state.LastWheelDirection != 0 && state.LastWheelDirection != direction)
                ReconcilePendingInternalOffset(viewer, state, actualOffset);

            state.StartOffset = actualOffset;
            state.TargetOffset = targetOffset;
            state.StartTimestamp = Stopwatch.GetTimestamp();
            state.DurationMs = CalculateDurationMs(Math.Abs(targetOffset - actualOffset));
            state.LastWheelDirection = direction;
            state.CompleteAtTarget = false;

            if (Math.Abs(targetOffset - actualOffset) <= Epsilon)
            {
                CancelAnimation(viewer);
                return;
            }

            state.IsActive = true;

            if (!ClientAreaAnimationEnabled)
            {
                CompleteAtTarget(viewer, state);
                return;
            }

            SubscribeRendering(viewer, state);
        }

        private static void Viewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (!States.TryGetValue(viewer, out var state)) return;

            if (state.HasPendingInternalOffset)
            {
                double expectedOffset = ClampOffset(state.PendingInternalOffset, viewer.ScrollableHeight);
                if (Math.Abs(e.VerticalOffset - expectedOffset) <= Epsilon)
                {
                    state.HasPendingInternalOffset = false;
                    state.HasSupersededInternalOffset = false;
                    CancelPendingSettlement(state);
                    AcknowledgeInternalOffset(viewer, state, e.VerticalOffset);
                    return;
                }

                if (state.HasSupersededInternalOffset)
                {
                    double supersededOffset = ClampOffset(state.SupersededInternalOffset, viewer.ScrollableHeight);
                    if (Math.Abs(e.VerticalOffset - supersededOffset) <= Epsilon)
                    {
                        state.HasSupersededInternalOffset = false;
                        return;
                    }
                }

                if (Math.Abs(e.VerticalChange) > Epsilon)
                {
                    state.HasPendingInternalOffset = false;
                    state.HasSupersededInternalOffset = false;
                    CancelPendingSettlement(state);
                    if (state.RemoveWhenSettled)
                    {
                        RemoveSettledState(viewer);
                        return;
                    }

                    if (state.IsActive)
                        CancelAnimation(viewer, reconcilePendingInternalOffset: false);
                    else
                        state.TargetOffset = ClampOffset(e.VerticalOffset, viewer.ScrollableHeight);

                    return;
                }
            }

            if (!state.IsActive || Math.Abs(e.VerticalChange) <= Epsilon) return;

            CancelAnimation(viewer, reconcilePendingInternalOffset: false);
        }

        private static void Viewer_Loaded(object sender, RoutedEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (States.TryGetValue(viewer, out var state) && GetIsEnabled(viewer))
                AttachComboBoxDiscovery(viewer, state);
        }

        private static void Viewer_Unloaded(object sender, RoutedEventArgs e)
        {
            var viewer = (ScrollViewer)sender;
            if (States.TryGetValue(viewer, out var state))
            {
                DetachComboBoxDiscovery(viewer, state);
                DetachComboBoxSubscriptions(state);
            }

            CancelAnimation(viewer);
        }

        private static void AttachComboBoxDiscovery(ScrollViewer viewer, AnimationState state)
        {
            if (state.LayoutUpdatedHandler == null) return;

            viewer.LayoutUpdated -= state.LayoutUpdatedHandler;
            viewer.LayoutUpdated += state.LayoutUpdatedHandler;
            RefreshComboBoxSubscriptions(viewer, state);
        }

        private static void DetachComboBoxDiscovery(ScrollViewer viewer, AnimationState state)
        {
            if (state.LayoutUpdatedHandler != null)
                viewer.LayoutUpdated -= state.LayoutUpdatedHandler;
        }

        private static void SubscribeRendering(ScrollViewer viewer, AnimationState state)
        {
            if (state.RenderingSubscribed) return;

            state.RenderingHandler = (_, _) =>
            {
                if (!SuppressRenderingCallbacksForTests)
                    RenderFrame(viewer, state);
            };
            CompositionTarget.Rendering += state.RenderingHandler;
            state.RenderingSubscribed = true;
        }

        private static void RenderFrame(ScrollViewer viewer, AnimationState state)
        {
            double elapsedMs = Stopwatch.GetElapsedTime(state.StartTimestamp).TotalMilliseconds;
            RenderFrame(viewer, state, elapsedMs);
        }

        private static void RenderFrame(ScrollViewer viewer, AnimationState state, double elapsedMs)
        {
            if (!state.IsActive)
            {
                UnsubscribeRendering(state);
                return;
            }

            if (HasOpenComboBox(viewer))
            {
                CancelAnimation(viewer);
                return;
            }

            state.TargetOffset = ClampOffset(state.TargetOffset, viewer.ScrollableHeight);

            if (!ClientAreaAnimationEnabled)
            {
                CompleteAtTarget(viewer, state);
                return;
            }

            if (state.HasPendingInternalOffset) return;

            double progress = state.DurationMs <= Epsilon ? 1.0 : Math.Clamp(elapsedMs / state.DurationMs, 0, 1);
            if (progress >= 1.0)
            {
                CompleteAtTarget(viewer, state);
                return;
            }

            double offset = Interpolate(state.StartOffset, state.TargetOffset, progress);
            RequestVerticalOffset(viewer, state, ClampOffset(offset, viewer.ScrollableHeight));
        }

        private static void CompleteAtTarget(ScrollViewer viewer, AnimationState state)
        {
            state.CompleteAtTarget = true;
            UnsubscribeRendering(state);

            double targetOffset = ClampOffset(state.TargetOffset, viewer.ScrollableHeight);
            if (state.HasPendingInternalOffset)
            {
                ReplacePendingInternalOffset(viewer, state, targetOffset);
                return;
            }

            if (Math.Abs(viewer.VerticalOffset - targetOffset) <= Epsilon)
            {
                FinishAnimation(viewer, state);
                return;
            }

            RequestVerticalOffset(viewer, state, targetOffset);
        }

        private static void AcknowledgeInternalOffset(ScrollViewer viewer, AnimationState state, double actualOffset)
        {
            if (state.RemoveWhenSettled)
            {
                RemoveSettledState(viewer);
                return;
            }

            if (!state.IsActive)
            {
                state.TargetOffset = ClampOffset(actualOffset, viewer.ScrollableHeight);
                return;
            }

            if (!state.CompleteAtTarget) return;

            double targetOffset = ClampOffset(state.TargetOffset, viewer.ScrollableHeight);
            if (Math.Abs(actualOffset - targetOffset) <= Epsilon)
            {
                FinishAnimation(viewer, state);
                return;
            }

            RequestVerticalOffset(viewer, state, targetOffset);
        }

        private static void ReconcilePendingInternalOffset(ScrollViewer viewer, AnimationState state, double recoveryOffset)
        {
            if (!state.HasPendingInternalOffset) return;
            ReplacePendingInternalOffset(viewer, state, ClampOffset(recoveryOffset, viewer.ScrollableHeight));
        }

        private static void ReplacePendingInternalOffset(ScrollViewer viewer, AnimationState state, double offset)
        {
            offset = ClampOffset(offset, viewer.ScrollableHeight);
            if (!state.HasPendingInternalOffset)
            {
                RequestVerticalOffset(viewer, state, offset);
                return;
            }

            if (Math.Abs(state.PendingInternalOffset - offset) <= Epsilon)
            {
                SchedulePendingSettlement(viewer, state);
                return;
            }

            if (!state.HasSupersededInternalOffset)
            {
                state.SupersededInternalOffset = state.PendingInternalOffset;
                state.HasSupersededInternalOffset = true;
            }

            state.PendingInternalOffset = offset;
            viewer.ScrollToVerticalOffset(offset);
            SchedulePendingSettlement(viewer, state);
        }

        private static void SchedulePendingSettlement(ScrollViewer viewer, AnimationState state)
        {
            if (state.SettlementOperation != null) return;

            state.SettlementOperation = viewer.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                state.SettlementOperation = null;
                if (!States.TryGetValue(viewer, out var current) || !ReferenceEquals(current, state) || !state.HasPendingInternalOffset)
                    return;

                double actualOffset = ClampOffset(viewer.VerticalOffset, viewer.ScrollableHeight);
                double expectedOffset = ClampOffset(state.PendingInternalOffset, viewer.ScrollableHeight);
                state.HasPendingInternalOffset = false;
                state.HasSupersededInternalOffset = false;

                if (Math.Abs(actualOffset - expectedOffset) <= Epsilon)
                {
                    AcknowledgeInternalOffset(viewer, state, actualOffset);
                    return;
                }

                if (state.RemoveWhenSettled)
                {
                    RemoveSettledState(viewer);
                    return;
                }

                if (state.IsActive)
                    CancelAnimation(viewer, reconcilePendingInternalOffset: false);
                else
                    state.TargetOffset = actualOffset;
            }));
        }

        private static void CancelPendingSettlement(AnimationState state)
        {
            if (state.SettlementOperation == null) return;

            state.SettlementOperation.Abort();
            state.SettlementOperation = null;
        }

        private static bool RequestVerticalOffset(ScrollViewer viewer, AnimationState state, double offset)
        {
            if (state.HasPendingInternalOffset)
            {
                return false;
            }

            state.PendingInternalOffset = ClampOffset(offset, viewer.ScrollableHeight);
            state.HasPendingInternalOffset = true;
            viewer.ScrollToVerticalOffset(state.PendingInternalOffset);
            return true;
        }

        private static void FinishAnimation(ScrollViewer viewer, AnimationState state)
        {
            state.IsActive = false;
            state.CompleteAtTarget = false;
            state.TargetOffset = ClampOffset(viewer.VerticalOffset, viewer.ScrollableHeight);
            state.LastWheelDirection = 0;
            UnsubscribeRendering(state);
        }

        private static void CancelAnimation(ScrollViewer viewer, bool reconcilePendingInternalOffset = true)
        {
            if (!States.TryGetValue(viewer, out var state)) return;

            if (reconcilePendingInternalOffset)
                ReconcilePendingInternalOffset(viewer, state, viewer.VerticalOffset);

            state.IsActive = false;
            state.CompleteAtTarget = false;
            state.TargetOffset = ClampOffset(viewer.VerticalOffset, viewer.ScrollableHeight);
            state.LastWheelDirection = 0;
            UnsubscribeRendering(state);
        }

        private static void RemoveSettledState(ScrollViewer viewer)
        {
            if (States.TryGetValue(viewer, out var state))
                CancelPendingSettlement(state);

            viewer.ScrollChanged -= Viewer_ScrollChanged;
            States.Remove(viewer);
        }

        private static void UnsubscribeRendering(AnimationState state)
        {
            if (!state.RenderingSubscribed || state.RenderingHandler == null) return;

            CompositionTarget.Rendering -= state.RenderingHandler;
            state.RenderingHandler = null;
            state.RenderingSubscribed = false;
        }

        private static bool HasOpenComboBox(ScrollViewer viewer)
        {
            if (OpenComboBoxOverrideForTests.HasValue)
            {
                return OpenComboBoxOverrideForTests.Value;
            }

            return FindOpenComboBox(viewer, comboBox => comboBox.IsDropDownOpen) != null;
        }

        private static bool HasOpenComboBox(ScrollViewer viewer, Func<ComboBox, bool> isOpen) => FindOpenComboBox(viewer, isOpen) != null;

        private static ComboBox FindOpenComboBox(ScrollViewer viewer, Func<ComboBox, bool> isOpen)
        {
            var pending = new Stack<DependencyObject>();
            pending.Push(viewer);

            while (pending.Count > 0)
            {
                DependencyObject current = pending.Pop();
                if (!ReferenceEquals(current, viewer) && current is ComboBox comboBox && isOpen(comboBox))
                {
                    return comboBox;
                }

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                    pending.Push(VisualTreeHelper.GetChild(current, i));
            }

            return null;
        }

        private static bool TryHandleOpenComboBoxWheel(ScrollViewer viewer, MouseWheelEventArgs e)
        {
            if (!HasOpenComboBox(viewer))
            {
                return false;
            }

            CancelAnimation(viewer);
            e.Handled = true;
            return true;
        }

        private static void RefreshComboBoxSubscriptions(ScrollViewer viewer, AnimationState state)
        {
            var realized = new HashSet<ComboBox>();
            CollectDescendantComboBoxes(viewer, realized);

            foreach (var comboBox in realized)
            {
                if (!state.ComboBoxes.ContainsKey(comboBox))
                    AttachComboBoxSubscription(viewer, state, comboBox);
            }

            foreach (var comboBox in new List<ComboBox>(state.ComboBoxes.Keys))
            {
                if (!realized.Contains(comboBox))
                    DetachComboBoxSubscription(state, comboBox);
            }
        }

        private static void CollectDescendantComboBoxes(DependencyObject root, HashSet<ComboBox> result)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is ComboBox comboBox)
                    result.Add(comboBox);

                CollectDescendantComboBoxes(child, result);
            }
        }

        private static void AttachComboBoxSubscription(ScrollViewer viewer, AnimationState state, ComboBox comboBox)
        {
            var popupState = new ComboBoxPopupState(viewer, comboBox);
            popupState.DropDownOpenedHandler = (_, _) => OpenComboBoxPopup(popupState);
            popupState.DropDownClosedHandler = (_, _) => CloseComboBoxPopup(popupState);
            popupState.UnloadedHandler = (_, _) => DetachComboBoxSubscription(state, comboBox);
            popupState.LayoutUpdatedHandler = (_, _) => RefreshPopupWheelOwner(popupState);
            popupState.PopupWheelHandler = (_, e) => HandlePopupWheel(popupState, e);

            comboBox.DropDownOpened += popupState.DropDownOpenedHandler;
            comboBox.DropDownClosed += popupState.DropDownClosedHandler;
            comboBox.Unloaded += popupState.UnloadedHandler;
            state.ComboBoxes.Add(comboBox, popupState);

            if (comboBox.IsDropDownOpen)
                OpenComboBoxPopup(popupState);
        }

        private static void DetachComboBoxSubscriptions(AnimationState state)
        {
            foreach (var comboBox in new List<ComboBox>(state.ComboBoxes.Keys))
                DetachComboBoxSubscription(state, comboBox);
        }

        private static void DetachComboBoxSubscription(AnimationState state, ComboBox comboBox)
        {
            if (!state.ComboBoxes.TryGetValue(comboBox, out var popupState)) return;

            CloseComboBoxPopup(popupState);
            comboBox.DropDownOpened -= popupState.DropDownOpenedHandler;
            comboBox.DropDownClosed -= popupState.DropDownClosedHandler;
            comboBox.Unloaded -= popupState.UnloadedHandler;
            state.ComboBoxes.Remove(comboBox);
        }

        private static void OpenComboBoxPopup(ComboBoxPopupState state)
        {
            CancelAnimation(state.PageViewer);
            state.ComboBox.LayoutUpdated -= state.LayoutUpdatedHandler;
            state.ComboBox.LayoutUpdated += state.LayoutUpdatedHandler;
            RefreshPopupWheelOwner(state);
        }

        private static void CloseComboBoxPopup(ComboBoxPopupState state)
        {
            state.ComboBox.LayoutUpdated -= state.LayoutUpdatedHandler;
            DetachPopupWheelOwner(state);
        }

        private static void RefreshPopupWheelOwner(ComboBoxPopupState state)
        {
            if (!state.ComboBox.IsDropDownOpen)
            {
                DetachPopupWheelOwner(state);
                return;
            }

            var popup = state.ComboBox.Template?.FindName("Popup", state.ComboBox) as Popup
                ?? state.ComboBox.Template?.FindName("PART_Popup", state.ComboBox) as Popup;
            var popupRoot = popup?.Child;
            var popupScroller = popupRoot == null ? null : FindDescendantScrollViewer(popupRoot);
            if (ReferenceEquals(state.PopupRoot, popupRoot) && ReferenceEquals(state.PopupScroller, popupScroller))
                return;

            DetachPopupWheelOwner(state);
            if (popupRoot == null || popupScroller == null) return;

            state.PopupRoot = popupRoot;
            state.PopupScroller = popupScroller;
            popupRoot.AddHandler(Mouse.PreviewMouseWheelEvent, state.PopupWheelHandler, true);
        }

        private static void DetachPopupWheelOwner(ComboBoxPopupState state)
        {
            state.PopupRoot?.RemoveHandler(Mouse.PreviewMouseWheelEvent, state.PopupWheelHandler);

            state.PopupRoot = null;
            state.PopupScroller = null;
        }

        private static void HandlePopupWheel(ComboBoxPopupState state, MouseWheelEventArgs e)
        {
            CancelAnimation(state.PageViewer);
            if (state.PopupScroller != null)
                ScrollComboBoxPopup(state.PopupScroller, e.Delta, SystemParameters.WheelScrollLines);

            e.Handled = true;
        }

        private static ScrollViewer FindDescendantScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer)
            {
                return viewer;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void ScrollComboBoxPopup(ScrollViewer viewer, int delta, int wheelScrollLines)
        {
            if (delta == 0 || wheelScrollLines == 0) return;

            bool towardTop = delta > 0;
            double wheelUnits = Math.Abs(NormalizeWheelDelta(delta));
            if (wheelScrollLines < 0)
            {
                if (towardTop) viewer.PageUp();
                else viewer.PageDown();
                return;
            }

            int lineCount = Math.Max(1, (int)Math.Round(wheelUnits * wheelScrollLines, MidpointRounding.AwayFromZero));
            for (int i = 0; i < lineCount; i++)
            {
                if (towardTop) viewer.LineUp();
                else viewer.LineDown();
            }
        }

        private static bool HasNestedWheelOwner(ScrollViewer viewer, DependencyObject source)
        {
            for (DependencyObject current = source; current != null && !ReferenceEquals(current, viewer); current = GetParent(current))
            {
                if (current is not ScrollViewer nested) continue;
                if (GetIsEnabled(nested) || nested.ScrollableHeight > Epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static ScrollViewer FindEnabledAncestor(ScrollViewer viewer)
        {
            for (DependencyObject current = GetParent(viewer); current != null; current = GetParent(current))
            {
                if (current is ScrollViewer ancestor && GetIsEnabled(ancestor))
                {
                    return ancestor;
                }
            }

            return null;
        }

        private static DependencyObject GetParent(DependencyObject element)
        {
            if (element is Visual || element is Visual3D)
            {
                return VisualTreeHelper.GetParent(element);
            }
            if (element is FrameworkContentElement contentElement)
            {
                return contentElement.Parent;
            }
            return LogicalTreeHelper.GetParent(element);
        }

        private static bool ClientAreaAnimationEnabled => ClientAreaAnimationOverrideForTests ?? SystemParameters.ClientAreaAnimation;

        internal static double NormalizeWheelDelta(int delta) => (double)delta / Mouse.MouseWheelDeltaForOneLine;

        internal static double GetDistancePerWheelUnit(int wheelScrollLines, double viewportHeight)
        {
            if (wheelScrollLines == 0)
            {
                return 0;
            }
            if (wheelScrollLines < 0)
            {
                return Math.Max(0, viewportHeight);
            }
            return wheelScrollLines * LineDistance;
        }

        internal static WheelRequest ComputeWheelRequest(double baseOffset, double scrollableHeight, double normalizedUnits, double distancePerUnit)
        {
            if (distancePerUnit <= Epsilon)
            {
                return new WheelRequest(ClampOffset(baseOffset, scrollableHeight), 0, normalizedUnits);
            }

            double clampedBase = ClampOffset(baseOffset, scrollableHeight);
            double targetOffset = ClampOffset(clampedBase - normalizedUnits * distancePerUnit, scrollableHeight);
            double consumedUnits = -(targetOffset - clampedBase) / distancePerUnit;
            double residualUnits = normalizedUnits - consumedUnits;
            if (Math.Abs(residualUnits) <= Epsilon) residualUnits = 0;

            return new WheelRequest(targetOffset, consumedUnits, residualUnits);
        }

        internal static double SelectBaseTarget(double actualOffset, double accumulatedTarget, bool isActive, int previousDirection, int newDirection)
        {
            if (!isActive)
            {
                return actualOffset;
            }
            if (previousDirection != 0 && previousDirection != newDirection)
            {
                return actualOffset;
            }
            return accumulatedTarget;
        }

        internal static double ClampOffset(double offset, double scrollableHeight) => Math.Clamp(offset, 0, Math.Max(0, scrollableHeight));

        internal static double CalculateDurationMs(double remainingDistance) => Math.Clamp(MinimumDurationMs + Math.Max(0, remainingDistance) / 4.0, MinimumDurationMs, MaximumDurationMs);

        internal static double Ease(double progress)
        {
            double t = Math.Clamp(progress, 0, 1);
            double remaining = 1 - t;
            return 1 - remaining * remaining * remaining;
        }

        internal static double Interpolate(double startOffset, double targetOffset, double progress) => startOffset + (targetOffset - startOffset) * Ease(progress);

        internal static void ApplyGestureForTests(ScrollViewer viewer, double normalizedUnits, int wheelScrollLines) => ApplyGesture(viewer, normalizedUnits, wheelScrollLines);

        internal static void RenderFrameForTests(ScrollViewer viewer, double elapsedMs)
        {
            if (States.TryGetValue(viewer, out var state))
                RenderFrame(viewer, state, elapsedMs);
        }

        internal static bool HasOpenComboBoxForTests(ScrollViewer viewer, Func<ComboBox, bool> isOpen) => HasOpenComboBox(viewer, isOpen);

        internal static int GetPopupWheelSubscriptionCountForTests(ScrollViewer viewer)
        {
            if (!States.TryGetValue(viewer, out var state))
            {
                return 0;
            }

            int count = 0;
            foreach (var popupState in state.ComboBoxes.Values)
            {
                if (popupState.PopupRoot != null)
                    count++;
            }

            return count;
        }

        internal static int GetComboBoxSubscriptionCountForTests(ScrollViewer viewer) => States.TryGetValue(viewer, out var state) ? state.ComboBoxes.Count : 0;

        internal static void ScrollComboBoxPopupForTests(ScrollViewer viewer, int delta, int wheelScrollLines) => ScrollComboBoxPopup(viewer, delta, wheelScrollLines);

        internal static ScrollStateSnapshot GetStateSnapshotForTests(ScrollViewer viewer)
        {
            if (!States.TryGetValue(viewer, out var state))
            {
                return new ScrollStateSnapshot(false, false, false, false, 0, 0);
            }

            return new ScrollStateSnapshot(
                true,
                state.IsActive,
                state.RenderingSubscribed,
                state.HasPendingInternalOffset,
                state.TargetOffset,
                state.PendingInternalOffset);
        }

        internal static void ResetTestOverrides()
        {
            ClientAreaAnimationOverrideForTests = null;
            KeyboardModifiersOverrideForTests = null;
            OpenComboBoxOverrideForTests = null;
            SuppressRenderingCallbacksForTests = false;
        }

        internal readonly record struct WheelRequest(double TargetOffset, double ConsumedUnits, double ResidualUnits);

        internal readonly record struct ScrollStateSnapshot(
            bool Exists,
            bool IsActive,
            bool RenderingSubscribed,
            bool HasPendingInternalOffset,
            double TargetOffset,
            double PendingInternalOffset);

        private sealed class AnimationState
        {
            public double StartOffset;
            public double TargetOffset;
            public long StartTimestamp;
            public double DurationMs;
            public int LastWheelDirection;
            public bool IsActive;
            public bool CompleteAtTarget;
            public bool HasPendingInternalOffset;
            public double PendingInternalOffset;
            public bool HasSupersededInternalOffset;
            public double SupersededInternalOffset;
            public bool RemoveWhenSettled;
            public DispatcherOperation SettlementOperation;
            public bool RenderingSubscribed;
            public EventHandler RenderingHandler;
            public EventHandler LayoutUpdatedHandler;
            public Dictionary<ComboBox, ComboBoxPopupState> ComboBoxes { get; } = [];
        }

        private sealed class ComboBoxPopupState(ScrollViewer pageViewer, ComboBox comboBox)
        {
            public ScrollViewer PageViewer { get; } = pageViewer;
            public ComboBox ComboBox { get; } = comboBox;
            public UIElement PopupRoot;
            public ScrollViewer PopupScroller;
            public EventHandler DropDownOpenedHandler;
            public EventHandler DropDownClosedHandler;
            public RoutedEventHandler UnloadedHandler;
            public EventHandler LayoutUpdatedHandler;
            public MouseWheelEventHandler PopupWheelHandler;
        }
    }
}
