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

using System.Diagnostics;
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
    // EaseOut 指数。与原先的 CubicEase 一致，冲量在开始时最快、随后平滑衰减。
    private const double EasingExponent = 3d;
    private const double MinimumFrameDelta = 0.001d;

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

    // 每个滚动区域的动量状态：一组仍在衰减的滚轮冲量，以及它们累积出的偏移。
    private static readonly DependencyProperty MomentumProperty =
        DependencyProperty.RegisterAttached(
            "Momentum",
            typeof(ScrollMomentum),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(null));

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
        // 取消时丢弃全部剩余冲量，并把动量同步到当前实际位置，
        // 下一次滚轮从用户看到的位置重新开始累积。
        ReleaseInteractionScope(scrollViewer);
        if (scrollViewer.GetValue(MomentumProperty) is ScrollMomentum momentum)
        {
            momentum.Impulses.Clear();
            momentum.Offset = scrollViewer.VerticalOffset;
            ReleaseRenderingHook(scrollViewer, momentum);
        }

        SetIsAnimating(scrollViewer, false);
    }

    public static bool CancelAnimationFromDescendant(DependencyObject root)
    {
        if (FindDescendant<ScrollViewer>(root) is not { } scrollViewer)
            return false;

        CancelAnimation(scrollViewer);
        return true;
    }


    private static bool GetIsInternalScrollUpdate(DependencyObject element) => (bool)element.GetValue(IsInternalScrollUpdateProperty);

    private static void SetIsInternalScrollUpdate(DependencyObject element, bool value) => element.SetValue(IsInternalScrollUpdateProperty, value);

    private static bool GetIsAnimating(DependencyObject element) => (bool)element.GetValue(IsAnimatingProperty);

    private static void SetIsAnimating(DependencyObject element, bool value) => element.SetValue(IsAnimatingProperty, value);

    private static void BeginScrollInteractionScope(ScrollViewer scrollViewer)
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
            EnsureMomentum(scrollViewer).Offset = scrollViewer.VerticalOffset;
            return;
        }

        scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
        scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        scrollViewer.Unloaded -= ScrollViewer_Unloaded;
        CancelAnimation(scrollViewer);
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
            CancelAnimation(scrollViewer);
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

        if (scrollViewer.GetValue(MomentumProperty) is ScrollMomentum momentum)
        {
            momentum.Impulses.Clear();
            momentum.Offset = scrollViewer.VerticalOffset;
            ReleaseRenderingHook(scrollViewer, momentum);
        }
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
        // 每次滚轮叠加一个独立衰减的冲量，而不是重建一个指向新目标的动画。
        // 连续滚动时多个冲量同时生效，速度保持连续，不会出现"快-慢-快"的顿挫。
        if (scrollViewer.ScrollableHeight <= 0
            || (scrollViewer.CanContentScroll && !GetAllowContentScroll(optionsSource)))
        {
            return false;
        }

        var wheelStep = GetScrollAmount(optionsSource) * Math.Max(1d, Math.Abs(e.Delta) / 120d);
        var delta = e.Delta > 0 ? -wheelStep : wheelStep;
        var momentum = EnsureMomentum(scrollViewer);
        var projected = Math.Clamp(
            momentum.Offset + GetRemainingDelta(momentum),
            0d,
            scrollViewer.ScrollableHeight);

        // 已经贴住边界且继续朝同方向滚动时不再累积，避免空转的冲量。
        if ((delta < 0d && projected <= 0.1d)
            || (delta > 0d && projected >= scrollViewer.ScrollableHeight - 0.1d))
        {
            e.Handled = true;
            return true;
        }

        var durationMilliseconds = GetWheelAnimationDurationMilliseconds(optionsSource);
        if (GetAllowContentScroll(optionsSource))
            durationMilliseconds = Math.Clamp(durationMilliseconds, 100d, 160d);

        momentum.Impulses.Add(new ScrollImpulse
        {
            TotalDelta = delta,
            StartedAt = Stopwatch.GetTimestamp(),
            DurationMilliseconds = Math.Max(durationMilliseconds, 1d)
        });

        SetIsAnimating(scrollViewer, true);
        BeginScrollInteractionScope(scrollViewer);
        EnsureRenderingHook(scrollViewer, momentum);
        e.Handled = true;
        return true;
    }

    private static ScrollMomentum EnsureMomentum(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(MomentumProperty) is ScrollMomentum existing)
            return existing;

        var momentum = new ScrollMomentum { Offset = scrollViewer.VerticalOffset };
        scrollViewer.SetValue(MomentumProperty, momentum);
        return momentum;
    }

    /// <summary>
    /// 所有在途冲量尚未兑现的位移之和，用于在累积新冲量前判断是否已经到边界。
    /// </summary>
    private static double GetRemainingDelta(ScrollMomentum momentum)
    {
        var remaining = 0d;
        foreach (var impulse in momentum.Impulses)
            remaining += impulse.TotalDelta * (1d - impulse.AppliedProgress);
        return remaining;
    }

    private static void EnsureRenderingHook(ScrollViewer scrollViewer, ScrollMomentum momentum)
    {
        if (momentum.RenderingHandler is not null)
            return;

        momentum.RenderingHandler = (_, _) => AdvanceMomentum(scrollViewer, momentum);
        CompositionTarget.Rendering += momentum.RenderingHandler;
    }

    private static void ReleaseRenderingHook(ScrollViewer scrollViewer, ScrollMomentum momentum)
    {
        if (momentum.RenderingHandler is null)
            return;

        CompositionTarget.Rendering -= momentum.RenderingHandler;
        momentum.RenderingHandler = null;
    }

    /// <summary>
    /// 每个合成帧推进一次：累加所有在途冲量在这一帧新增的位移，然后一次性提交。
    /// </summary>
    private static void AdvanceMomentum(ScrollViewer scrollViewer, ScrollMomentum momentum)
    {
        var frameDelta = 0d;
        for (var index = momentum.Impulses.Count - 1; index >= 0; index--)
        {
            var impulse = momentum.Impulses[index];
            var elapsed = Stopwatch.GetElapsedTime(impulse.StartedAt).TotalMilliseconds;
            var linear = Math.Clamp(elapsed / impulse.DurationMilliseconds, 0d, 1d);
            var progress = 1d - Math.Pow(1d - linear, EasingExponent);
            frameDelta += impulse.TotalDelta * (progress - impulse.AppliedProgress);
            impulse.AppliedProgress = progress;
            if (linear >= 1d)
                momentum.Impulses.RemoveAt(index);
        }

        if (Math.Abs(frameDelta) > MinimumFrameDelta)
        {
            momentum.Offset = Math.Clamp(
                momentum.Offset + frameDelta,
                0d,
                scrollViewer.ScrollableHeight);
            SetIsInternalScrollUpdate(scrollViewer, true);
            scrollViewer.ScrollToVerticalOffset(momentum.Offset);
            SetIsInternalScrollUpdate(scrollViewer, false);
        }

        if (momentum.Impulses.Count > 0)
            return;

        ReleaseRenderingHook(scrollViewer, momentum);
        momentum.Offset = scrollViewer.VerticalOffset;
        SetIsAnimating(scrollViewer, false);
        ReleaseInteractionScope(scrollViewer);
    }

    private sealed class ScrollMomentum
    {
        internal List<ScrollImpulse> Impulses { get; } = [];

        internal double Offset { get; set; }

        internal EventHandler? RenderingHandler { get; set; }
    }

    private sealed class ScrollImpulse
    {
        internal double TotalDelta { get; init; }

        internal long StartedAt { get; init; }

        internal double DurationMilliseconds { get; init; }

        /// <summary>已经兑现的缓动进度，用于把总位移拆成逐帧增量。</summary>
        internal double AppliedProgress { get; set; }
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
