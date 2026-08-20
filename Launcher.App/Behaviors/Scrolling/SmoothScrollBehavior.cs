/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Controls;
using Launcher.App.Diagnostics;

namespace Launcher.App.Behaviors;

/// <summary>
/// 为 ScrollViewer 及其延迟生成的内部滚动宿主提供可取消、可接续的滚轮平滑动画。
/// </summary>
public static class SmoothScrollBehavior
{
    // 附加属性保存目标偏移和动画版本，使行为无需全局字典即可随控件生命周期回收。
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScrollAmountProperty =
        DependencyProperty.RegisterAttached(
            "ScrollAmount",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(84d));

    public static readonly DependencyProperty AllowContentScrollProperty =
        DependencyProperty.RegisterAttached(
            "AllowContentScroll",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty WheelAnimationDurationMillisecondsProperty =
        DependencyProperty.RegisterAttached(
            "WheelAnimationDurationMilliseconds",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(130d));

    private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVerticalOffset",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(0d, OnAnimatedVerticalOffsetChanged));

    private static readonly DependencyProperty TargetVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetVerticalOffset",
            typeof(double),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(double.NaN));

    private static readonly DependencyProperty IsInternalScrollUpdateProperty =
        DependencyProperty.RegisterAttached(
            "IsInternalScrollUpdate",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimating",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false));

    private static readonly DependencyProperty AnimationVersionProperty =
        DependencyProperty.RegisterAttached(
            "AnimationVersion",
            typeof(int),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(0));

    // 一次滚动会话覆盖连续滚轮产生的多段动画，作用域随控件生命周期存放在附加属性上。
    private static readonly DependencyProperty InteractionScopeProperty =
        DependencyProperty.RegisterAttached(
            "InteractionScope",
            typeof(UiInteractionScope),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetScrollAmount(DependencyObject element) => (double)element.GetValue(ScrollAmountProperty);

    public static void SetScrollAmount(DependencyObject element, double value) => element.SetValue(ScrollAmountProperty, value);

    public static bool GetAllowContentScroll(DependencyObject element) => (bool)element.GetValue(AllowContentScrollProperty);

    public static void SetAllowContentScroll(DependencyObject element, bool value) => element.SetValue(AllowContentScrollProperty, value);

    public static double GetWheelAnimationDurationMilliseconds(DependencyObject element) => (double)element.GetValue(WheelAnimationDurationMillisecondsProperty);

    public static void SetWheelAnimationDurationMilliseconds(DependencyObject element, double value) => element.SetValue(WheelAnimationDurationMillisecondsProperty, value);

    public static void CancelAnimation(ScrollViewer scrollViewer)
    {
        // 取消时将目标同步到当前实际位置，下一次滚轮从用户看到的位置继续。
        ReleaseInteractionScope(scrollViewer);
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
        SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetIsAnimating(scrollViewer, false);
    }

    public static bool CancelAnimationFromDescendant(DependencyObject root)
    {
        if (FindDescendant<ScrollViewer>(root) is not { } scrollViewer)
            return false;

        CancelAnimation(scrollViewer);
        return true;
    }

    private static double GetAnimatedVerticalOffset(DependencyObject element) => (double)element.GetValue(AnimatedVerticalOffsetProperty);

    private static void SetAnimatedVerticalOffset(DependencyObject element, double value) => element.SetValue(AnimatedVerticalOffsetProperty, value);

    private static double GetTargetVerticalOffset(DependencyObject element) => (double)element.GetValue(TargetVerticalOffsetProperty);

    private static void SetTargetVerticalOffset(DependencyObject element, double value) => element.SetValue(TargetVerticalOffsetProperty, value);

    private static bool GetIsInternalScrollUpdate(DependencyObject element) => (bool)element.GetValue(IsInternalScrollUpdateProperty);

    private static void SetIsInternalScrollUpdate(DependencyObject element, bool value) => element.SetValue(IsInternalScrollUpdateProperty, value);

    private static bool GetIsAnimating(DependencyObject element) => (bool)element.GetValue(IsAnimatingProperty);

    private static void SetIsAnimating(DependencyObject element, bool value) => element.SetValue(IsAnimatingProperty, value);

    private static void EnsureInteractionScope(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(InteractionScopeProperty) is UiInteractionScope)
            return;

        var detail = ResolveScrollDetail(scrollViewer);
        UiPerformanceLog.LogScrollSurface(scrollViewer, detail);
        var scope = UiPerformanceLog.BeginInteraction("Scroll", detail, scrollViewer);
        scope.RenderPath = UiRenderPaths.Live;
        scope.SurfaceWidth = scrollViewer.ViewportWidth;
        scope.SurfaceHeight = scrollViewer.ViewportHeight;
        var coordinator = BackdropBlurRefreshCoordinator.TryGet(scrollViewer);
        if (coordinator is not null)
        {
            scope.BackdropControlCount = coordinator.GetScrollViewerControlCount(scrollViewer);
            scope.BackdropCounterReader = () => (coordinator.TotalBatchCount, coordinator.TotalRefreshCount);
        }

        scope.HasAncestorBitmapCache = HasBitmapCache(scrollViewer);
        scope.HasOpacityMask = HasOpacityMask(scrollViewer);
        scope.ScrollOffsetReader = () => scrollViewer.VerticalOffset;
        scope.CaptureBackdropBaseline();
        scrollViewer.SetValue(InteractionScopeProperty, scope);
    }

    /// <summary>
    /// 位图缓存既可能挂在滚动内容上（ScrollViewer 的子），也可能挂在整页上（祖先），两处都要查。
    /// </summary>
    private static bool HasBitmapCache(ScrollViewer scrollViewer)
    {
        if (scrollViewer.Content is UIElement { CacheMode: BitmapCache })
            return true;

        return HasAncestor(scrollViewer, static element => element.CacheMode is BitmapCache);
    }

    private static bool HasOpacityMask(ScrollViewer scrollViewer) =>
        HasAncestor(scrollViewer, static element => element.OpacityMask is not null);

    private static bool HasAncestor(DependencyObject element, Func<UIElement, bool> predicate)
    {
        for (DependencyObject? current = element;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement uiElement && predicate(uiElement))
                return true;
        }

        return false;
    }

    private static void ReleaseInteractionScope(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(InteractionScopeProperty) is not UiInteractionScope scope)
            return;

        scrollViewer.ClearValue(InteractionScopeProperty);
        scope.Dispose();
    }

    /// <summary>
    /// 用最近的具名元素标识滚动区域，便于把掉帧摘要对应到具体列表。
    /// </summary>
    private static string ResolveScrollDetail(ScrollViewer scrollViewer)
    {
        for (DependencyObject? current = scrollViewer;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Name.Length: > 0 } named)
                return named.Name;
        }

        return scrollViewer.GetType().Name;
    }

    private static int GetAnimationVersion(DependencyObject element) => (int)element.GetValue(AnimationVersionProperty);

    private static void SetAnimationVersion(DependencyObject element, int value) => element.SetValue(AnimationVersionProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // ItemsControl 的 ScrollViewer 可能在模板内部延迟生成，需要查找后把行为下沉到真实宿主。
        if (d is not ScrollViewer scrollViewer)
        {
            UpdateDescendantScrollHost(d, (bool)e.NewValue);
            return;
        }

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            scrollViewer.Unloaded += ScrollViewer_Unloaded;
            SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
            SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
            return;
        }

        scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
        scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        scrollViewer.Unloaded -= ScrollViewer_Unloaded;
        ReleaseInteractionScope(scrollViewer);
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
        SetIsAnimating(scrollViewer, false);
    }

    private static void UpdateDescendantScrollHost(DependencyObject d, bool isEnabled)
    {
        if (d is not FrameworkElement element)
            return;

        if (isEnabled)
        {
            element.PreviewMouseWheel += DescendantScrollHost_PreviewMouseWheel;
            element.Unloaded += DescendantScrollHost_Unloaded;
            return;
        }

        element.PreviewMouseWheel -= DescendantScrollHost_PreviewMouseWheel;
        element.Unloaded -= DescendantScrollHost_Unloaded;
        CancelAnimationFromDescendant(element);
    }

    private static void DescendantScrollHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject root)
            HandleMouseWheelFromDescendant(root, e);
    }

    private static void DescendantScrollHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject root)
            CancelAnimationFromDescendant(root);
    }

    private static void ScrollViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            ReleaseInteractionScope(scrollViewer);
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetIsAnimating(scrollViewer, false);
        }
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 拖动滚动条或调用 ScrollTo 属于外部滚动，会终止正在运行的插值动画。
        if (sender is not ScrollViewer scrollViewer
            || GetIsInternalScrollUpdate(scrollViewer)
            || GetIsAnimating(scrollViewer))
        {
            return;
        }

        SetAnimatedVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer
            || !HandleMouseWheel(scrollViewer, e))
        {
            return;
        }
    }

    public static bool HandleMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e)
    {
        return HandleMouseWheel(scrollViewer, e, scrollViewer);
    }

    private static bool HandleMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e, DependencyObject optionsSource)
    {
        // 多次滚轮在当前目标值上累加，而不是每次从实际偏移重启，快速滚动仍保持连续。
        if (scrollViewer.ScrollableHeight <= 0
            || (scrollViewer.CanContentScroll && !GetAllowContentScroll(optionsSource)))
        {
            return false;
        }

        var currentOffset = GetAnimatedVerticalOffset(scrollViewer);
        if (double.IsNaN(currentOffset) || double.IsInfinity(currentOffset))
            currentOffset = scrollViewer.VerticalOffset;

        var targetOffset = GetTargetVerticalOffset(scrollViewer);
        if (double.IsNaN(targetOffset) || double.IsInfinity(targetOffset))
            targetOffset = currentOffset;

        var wheelStep = GetScrollAmount(optionsSource) * Math.Max(1d, Math.Abs(e.Delta) / 120d);
        var delta = e.Delta > 0 ? -wheelStep : wheelStep;
        var nextOffset = Math.Clamp(targetOffset + delta, 0d, scrollViewer.ScrollableHeight);

        if (Math.Abs(nextOffset - targetOffset) < 0.1d && Math.Abs(nextOffset - currentOffset) < 0.1d)
        {
            e.Handled = true;
            return true;
        }

        SetTargetVerticalOffset(scrollViewer, nextOffset);
        SetAnimatedVerticalOffset(scrollViewer, currentOffset);
        SetIsAnimating(scrollViewer, true);
        // 连续滚轮共用同一个会话，掉帧摘要覆盖整段滚动而不是单次滚轮。
        EnsureInteractionScope(scrollViewer);
        // 版本号使旧动画 Completed 回调失效，避免它清理刚启动的新动画状态。
        var animationVersion = GetAnimationVersion(scrollViewer) + 1;
        SetAnimationVersion(scrollViewer, animationVersion);

        var durationMilliseconds = GetWheelAnimationDurationMilliseconds(optionsSource);
        if (GetAllowContentScroll(optionsSource))
            durationMilliseconds = Math.Clamp(durationMilliseconds, 100d, 160d);

        var animation = new DoubleAnimation
        {
            From = currentOffset,
            To = nextOffset,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            if (GetAnimationVersion(scrollViewer) != animationVersion)
                return;

            SetIsInternalScrollUpdate(scrollViewer, true);
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            SetAnimatedVerticalOffset(scrollViewer, nextOffset);
            scrollViewer.ScrollToVerticalOffset(nextOffset);
            SetTargetVerticalOffset(scrollViewer, nextOffset);
            SetIsInternalScrollUpdate(scrollViewer, false);
            SetIsAnimating(scrollViewer, false);
            ReleaseInteractionScope(scrollViewer);
        };

        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        e.Handled = true;
        return true;
    }

    public static bool HandleMouseWheelFromDescendant(
        DependencyObject root,
        MouseWheelEventArgs e,
        bool handleWhenUnavailable = false)
    {
        if (FindDescendant<ScrollViewer>(root) is not { } scrollViewer)
        {
            if (handleWhenUnavailable)
                e.Handled = true;
            return false;
        }

        var optionsSource = GetIsEnabled(root) ? root : scrollViewer;
        var handled = HandleMouseWheel(scrollViewer, e, optionsSource);
        if (!handled && handleWhenUnavailable)
            e.Handled = true;
        return handled;
    }

    private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 标记内部更新，ScrollChanged 才能区分动画帧和用户/代码发起的真实滚动。
        if (d is not ScrollViewer scrollViewer || GetIsInternalScrollUpdate(scrollViewer))
            return;

        var offset = (double)e.NewValue;
        if (double.IsNaN(offset) || double.IsInfinity(offset))
            return;

        SetIsInternalScrollUpdate(scrollViewer, true);
        scrollViewer.ScrollToVerticalOffset(offset);
        SetIsInternalScrollUpdate(scrollViewer, false);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
