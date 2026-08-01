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

namespace Launcher.App.Services;

public sealed class PageTransitionService
{
    private const double TransitionOffset = 22;

    private static readonly Duration TransitionDuration = TimeSpan.FromMilliseconds(240);
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
    private string? currentPage;
    private int transitionToken;
    private IDisposable? blurRefreshLease;

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
    {
        this.dispatcher = dispatcher;
        this.resolvePageRoot = resolvePageRoot;
        this.pageOrder = pageOrder is { Count: > 0 } ? pageOrder : DefaultPageOrder;
        currentPage = initialPage;
    }

    public void MoveTo(string newPage)
    {
        if (string.Equals(currentPage, newPage, StringComparison.OrdinalIgnoreCase))
            return;

        ReleaseBlurRefreshLease();
        var oldPage = currentPage;
        currentPage = newPage;
        var startOffset = GetTransitionStartOffset(oldPage, newPage);

        var target = resolvePageRoot(newPage);
        if (target is null)
            return;

        var token = ++transitionToken;
        PreparePageForTransition(target, startOffset);
        dispatcher.BeginInvoke(
            () => AnimatePage(newPage, target, startOffset, token),
            DispatcherPriority.Render);
    }

    public void SyncTo(string? page)
    {
        ReleaseBlurRefreshLease();
        transitionToken++;
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
        blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(target);

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
                transform.Y = 0;
                ReleaseBlurRefreshLease();
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
}
