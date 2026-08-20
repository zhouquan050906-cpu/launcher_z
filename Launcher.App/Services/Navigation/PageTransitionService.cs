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
        PreparePageForTransition(target, startOffset);
        activeTarget = target;
        target.Unloaded += ActiveTarget_Unloaded;
        dispatcher.BeginInvoke(
            () => AnimatePage(newPage, target, startOffset, token),
            DispatcherPriority.Render);
    }

    public void SyncTo(string? page)
    {
        transitionToken++;
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
        target.Opacity = 0;

        var transform = EnsureTranslateTransform(target);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = startOffset;
    }

    private void AnimatePage(string page, FrameworkElement target, double startOffset, int token)
    {
        if (token != transitionToken || !string.Equals(currentPage, page, StringComparison.OrdinalIgnoreCase))
            return;

        var transform = EnsureTranslateTransform(target);
        target.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        target.Opacity = 0;
        transform.Y = startOffset;
        if (BackdropBlurRefreshCoordinator.HasActiveImageBackdropBlur(target))
        {
            blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(target);
        }
        else
        {
            renderCacheScope = renderCacheFactory($"Page:{page}", [target]);
            if (TransitionRenderCacheScope.RequiresContinuousRefreshFallback(
                    renderCacheScope.FallbackReason))
            {
                blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(target);
            }
        }

        // 采样必须覆盖整段动画，因此在渲染路径确定之后、动画开始之前打开。
        interactionScope = UiPerformanceLog.BeginInteraction("PageTransition", page, target);
        interactionScope.RenderPath = UiRenderPaths.Resolve(
            renderCacheScope?.IsActive is true,
            blurRefreshLease is not null);

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
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

    private void CancelActiveTransition(bool requestFinalRefresh)
    {
        if (activeTarget is not { } target)
        {
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
