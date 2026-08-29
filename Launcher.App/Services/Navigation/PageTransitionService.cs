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
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Diagnostics;

namespace Launcher.App.Services;

public sealed class PageTransitionService
{
    internal const double TransitionOffset = 22;

    internal static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly string[] DefaultPageOrder =
    [
        "Account",
        "Home",
        "Download",
        "Install",
        "GameSettings",
        "Resources",
        "Settings"
    ];

    /// <summary>
    /// 预热阶段使用的透明度。必须严格大于 0：完全透明的子树会被渲染层剔除，
    /// 首次栅格化就会推迟到淡入动画把透明度抬离 0 的那一帧，正是要避免的情况。
    /// 取 8 位色深下的最小一档 alpha：既不为零，合成结果也只有 1/255，肉眼不可见。
    /// </summary>
    internal const double WarmupOpacity = 1d / 255d;

    /// <summary>
    /// 每一段等待的合成帧数。启动动画前一共等两段：第一段让新页面完成布局与首次栅格化，
    /// 装上位图缓存后再等第二段，让页面被画进缓存纹理。等待期间页面处于 WarmupOpacity，不可见。
    /// 在 60Hz 显示器上两段合计约 33ms，这是动画启动延迟与首帧卡顿之间的取舍。
    /// </summary>
    private const int WarmupCompositionFrames = 1;

    private readonly Dispatcher dispatcher;
    private readonly Func<string, FrameworkElement?> resolvePageRoot;
    private readonly IReadOnlyList<string> pageOrder;
    private readonly TransitionRenderCacheFactory renderCacheFactory;
    private string? currentPage;
    private int transitionToken;
    private IDisposable? blurRefreshLease;
    private TransitionRenderCacheScope? renderCacheScope;
    private UiInteractionScope? interactionScope;
    private FrameworkElement? activeTarget;
    private EventHandler? pendingCompositionWait;
    private long warmupStartedAtTimestamp;
    private DispatcherBusyProbe? warmupBusyProbe;
    private bool hasEnteredTransitionGate;

    public PageTransitionService(
        Dispatcher dispatcher,
        Func<string, FrameworkElement?> resolvePageRoot,
        string? initialPage)
        : this(dispatcher, resolvePageRoot, initialPage, null)
    {
    }

    public PageTransitionService(
        Dispatcher dispatcher,
        Func<string, FrameworkElement?> resolvePageRoot,
        string? initialPage,
        IReadOnlyList<string>? pageOrder)
        : this(dispatcher, resolvePageRoot, initialPage, pageOrder, TransitionRenderCacheScope.TryAcquire)
    {
    }

    internal PageTransitionService(
        Dispatcher dispatcher,
        Func<string, FrameworkElement?> resolvePageRoot,
        string? initialPage,
        IReadOnlyList<string>? pageOrder,
        TransitionRenderCacheFactory renderCacheFactory)
    {
        this.dispatcher = dispatcher;
        UiTransitionGate.AttachDispatcher(dispatcher);
        this.resolvePageRoot = resolvePageRoot;
        this.pageOrder = pageOrder is { Count: > 0 } ? pageOrder : DefaultPageOrder;
        this.renderCacheFactory = renderCacheFactory;
        currentPage = initialPage;
    }

    public void MoveTo(string newPage)
    {
        if (string.Equals(currentPage, newPage, StringComparison.OrdinalIgnoreCase))
            return;

        CancelActiveTransition(requestFinalRefresh: true);
        var oldPage = currentPage;
        currentPage = newPage;
        var startOffset = GetTransitionStartOffset(oldPage, newPage);

        var target = resolvePageRoot(newPage);
        if (target is null)
            return;

        var token = ++transitionToken;
        EnterTransitionGate();
        PreparePageForTransition(target, startOffset);
        activeTarget = target;
        target.Unloaded += ActiveTarget_Unloaded;
        warmupStartedAtTimestamp = Stopwatch.GetTimestamp();
        // 预热窗口发生在交互采样开启之前，其中的 UI 线程占用不会被 UiInteractionScope 统计到。
        // 单独挂一个探针把这段补上，否则无法判断预热是在等渲染线程还是 UI 线程自己在忙。
        ReleaseWarmupBusyProbe();
        if (UiPerformanceLog.IsEnabled)
            warmupBusyProbe = DispatcherBusyProbe.TryAttach(dispatcher);

        // 新页面此前是折叠的，从未被绘制过。等一个 dispatcher 轮次并不能保证渲染线程
        // 已经把它栅格化，动画一起步就会撞上这次整页栅格化——实测长帧正是集中在这里。
        // 因此改为等真实的合成帧：先栅格化一次，再装缓存，再等一帧让缓存被填满，最后才开始动画。
        WaitForCompositionFrames(
            WarmupCompositionFrames,
            () =>
            {
                if (!IsTransitionCurrent(newPage, token))
                    return;

                PrepareRenderPathForTransition(newPage, target);
                WaitForCompositionFrames(
                    WarmupCompositionFrames,
                    () => AnimatePage(newPage, target, startOffset, token));
            });
    }

    private bool IsTransitionCurrent(string page, int token) =>
        token == transitionToken
        && string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 等待指定数量的合成帧后执行回调。订阅期间 WPF 会保持连续渲染，
    /// 因此只用于过渡起步这种短暂等待，并且新的过渡必须取消尚未触发的等待。
    /// </summary>
    private void WaitForCompositionFrames(int frameCount, Action continuation)
    {
        CancelPendingCompositionWait();
        var remaining = Math.Max(frameCount, 1);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (--remaining > 0)
                return;

            CompositionTarget.Rendering -= handler;
            if (ReferenceEquals(pendingCompositionWait, handler))
                pendingCompositionWait = null;
            continuation();
        };

        pendingCompositionWait = handler;
        CompositionTarget.Rendering += handler;
    }

    private void ReleaseWarmupBusyProbe()
    {
        warmupBusyProbe?.Dispose();
        warmupBusyProbe = null;
    }

    private void CancelPendingCompositionWait()
    {
        if (pendingCompositionWait is null)
            return;

        CompositionTarget.Rendering -= pendingCompositionWait;
        pendingCompositionWait = null;
    }

    public void SyncTo(string? page)
    {
        transitionToken++;
        ExitTransitionGate();
        CancelPendingCompositionWait();
        CancelActiveTransition(requestFinalRefresh: true);
        currentPage = page;
    }

    private double GetTransitionStartOffset(string? oldPage, string newPage)
    {
        if (string.IsNullOrWhiteSpace(oldPage))
            return TransitionOffset;

        var oldIndex = IndexOfPage(oldPage);
        var newIndex = IndexOfPage(newPage);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return TransitionOffset;

        return newIndex > oldIndex ? TransitionOffset : -TransitionOffset;
    }

    private int IndexOfPage(string page)
    {
        for (var index = 0; index < pageOrder.Count; index++)
        {
            if (string.Equals(pageOrder[index], page, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private static TranslateTransform EnsureTranslateTransform(FrameworkElement target)
    {
        if (target.RenderTransform is TranslateTransform transform)
            return transform;

        transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
    }

    private static void PreparePageForTransition(FrameworkElement target, double startOffset)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = WarmupOpacity;

        var transform = EnsureTranslateTransform(target);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = startOffset;
    }

    /// <summary>
    /// 选定渲染路径。必须在动画开始前完成，好让接下来的那一帧把页面画进位图缓存，
    /// 而不是把整页栅格化推到动画的头几帧里。
    /// </summary>
    private void PrepareRenderPathForTransition(string page, FrameworkElement target)
    {
        if (BackdropBlurRefreshCoordinator.HasActiveImageBackdropBlur(target))
        {
            blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(target);
            return;
        }

        renderCacheScope = renderCacheFactory($"Page:{page}", [target]);
        if (TransitionRenderCacheScope.RequiresContinuousRefreshFallback(
                renderCacheScope.FallbackReason))
        {
            blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(target);
        }
    }

    private void AnimatePage(string page, FrameworkElement target, double startOffset, int token)
    {
        if (!IsTransitionCurrent(page, token))
            return;

        var transform = EnsureTranslateTransform(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        target.Opacity = WarmupOpacity;
        transform.Y = startOffset;

        // 采样必须覆盖整段动画，因此在渲染路径确定之后、动画开始之前打开。
        interactionScope = UiPerformanceLog.BeginInteraction("PageTransition", page, target);
        interactionScope.RenderPath = UiRenderPaths.Resolve(
            renderCacheScope?.IsActive is true,
            blurRefreshLease is not null);
        // 过渡期间被反复合成的就是整个页面，因此表面积直接对应每帧的填充率成本。
        interactionScope.SurfaceWidth = target.ActualWidth;
        interactionScope.SurfaceHeight = target.ActualHeight;
        interactionScope.HasAncestorBitmapCache = renderCacheScope?.IsActive is true;
        // 预热被排除在出帧统计之外，单独记录，避免把开销从动画里挪走却看不出挪到了哪。
        interactionScope.WarmupMs = warmupStartedAtTimestamp == 0L
            ? 0d
            : Stopwatch.GetElapsedTime(warmupStartedAtTimestamp).TotalMilliseconds;
        warmupStartedAtTimestamp = 0L;
        if (warmupBusyProbe is not null)
        {
            interactionScope.WarmupBusyMs = warmupBusyProbe.TotalBusyMs;
            interactionScope.WarmupWorstOperationMs = warmupBusyProbe.WorstOperationMs;
            interactionScope.WarmupWorstOperation = warmupBusyProbe.WorstOperationDetail;
            ReleaseWarmupBusyProbe();
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnimation = new DoubleAnimation
        {
            From = WarmupOpacity,
            To = 1,
            Duration = TransitionDuration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        fadeAnimation.Completed += (_, _) =>
        {
            if (token == transitionToken && string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
                target.Opacity = 1;
        };

        var slideAnimation = new DoubleAnimation
        {
            From = startOffset,
            To = 0,
            Duration = TransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        slideAnimation.Completed += (_, _) =>
        {
            if (token == transitionToken && string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
            {
                CompleteTransition(target, transform);
            }
        };

        target.BeginAnimation(UIElement.OpacityProperty, fadeAnimation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, slideAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ReleaseBlurRefreshLease()
    {
        blurRefreshLease?.Dispose();
        blurRefreshLease = null;
    }

    private void ReleaseInteractionScope()
    {
        interactionScope?.Dispose();
        interactionScope = null;
    }

    private void CompleteTransition(FrameworkElement target, TranslateTransform transform)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 1;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
        ReleaseTransitionResources(target, requestFinalRefresh: true);
    }

    private void EnterTransitionGate()
    {
        if (hasEnteredTransitionGate)
            return;

        hasEnteredTransitionGate = true;
        UiTransitionGate.Enter();
    }

    /// <summary>闸门必须与 Enter 严格配对，否则被推迟的工作会永远排不出去。</summary>
    private void ExitTransitionGate()
    {
        if (!hasEnteredTransitionGate)
            return;

        hasEnteredTransitionGate = false;
        UiTransitionGate.Exit();
    }

    private void CancelActiveTransition(bool requestFinalRefresh)
    {
        CancelPendingCompositionWait();
        ReleaseWarmupBusyProbe();
        if (activeTarget is not { } target)
        {
            ExitTransitionGate();
            ReleaseInteractionScope();
            ReleaseBlurRefreshLease();
            renderCacheScope?.Dispose();
            renderCacheScope = null;
            return;
        }

        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = 1;
        var transform = EnsureTranslateTransform(target);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
        ReleaseTransitionResources(target, requestFinalRefresh);
    }

    private void ReleaseTransitionResources(FrameworkElement target, bool requestFinalRefresh)
    {
        ExitTransitionGate();
        target.Unloaded -= ActiveTarget_Unloaded;
        ReleaseInteractionScope();
        ReleaseBlurRefreshLease();
        var usedRenderCache = renderCacheScope?.IsActive is true;
        renderCacheScope?.Dispose();
        renderCacheScope = null;
        activeTarget = null;

        if (requestFinalRefresh && usedRenderCache)
            BackdropBlurRefreshCoordinator.RequestScopeRefresh(target);
    }

    private void ActiveTarget_Unloaded(object sender, RoutedEventArgs e)
    {
        transitionToken++;
        CancelActiveTransition(requestFinalRefresh: false);
    }
}
