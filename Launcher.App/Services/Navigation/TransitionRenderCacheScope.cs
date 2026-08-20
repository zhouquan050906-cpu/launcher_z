/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Launcher.App.Diagnostics;
using Serilog;

namespace Launcher.App.Services;

internal enum TransitionRenderCacheFallbackReason
{
    None,
    NoElements,
    RenderingTierTooLow,
    ElementNotReady,
    TextureTooLarge,
    MemoryBudgetExceeded,
    CacheCreationFailed
}

internal readonly record struct TransitionRenderCacheCapabilities(
    int RenderingTier,
    Size MaximumTextureSize,
    long MaximumEstimatedBytes)
{
    internal const long DefaultMaximumEstimatedBytes = 64L * 1024L * 1024L;

    internal static TransitionRenderCacheCapabilities Current => new(
        RenderCapability.Tier >> 16,
        RenderCapability.MaxHardwareTextureSize,
        DefaultMaximumEstimatedBytes);
}

internal delegate TransitionRenderCacheScope TransitionRenderCacheFactory(
    string transitionKind,
    IReadOnlyList<FrameworkElement> elements);

/// <summary>
/// Temporarily promotes one or more transition layers into WPF bitmap caches.
/// Existing caches are preserved and nested scopes share the installed cache.
/// </summary>
internal sealed class TransitionRenderCacheScope : IDisposable
{
    private static readonly ConditionalWeakTable<FrameworkElement, CacheState> CacheStates = new();
    private static readonly object CacheStateGate = new();

    private readonly FrameworkElement[] elements;
    private int isDisposed;

    private TransitionRenderCacheScope(
        FrameworkElement[] elements,
        long estimatedBytes,
        TransitionRenderCacheFallbackReason fallbackReason)
    {
        this.elements = elements;
        EstimatedBytes = estimatedBytes;
        FallbackReason = fallbackReason;

        foreach (var element in elements)
            element.Unloaded += Element_Unloaded;
    }

    internal bool IsActive => Volatile.Read(ref isDisposed) == 0
                              && elements.Length > 0
                              && FallbackReason is TransitionRenderCacheFallbackReason.None;

    internal long EstimatedBytes { get; }

    internal TransitionRenderCacheFallbackReason FallbackReason { get; }

    internal static int ActiveOwnedCacheCount { get; private set; }

    /// <summary>
    /// 位图缓存拿不到时是否仍需连续背板刷新兜底。
    /// ElementNotReady 只是首次布局尚未完成的一次性时序问题，页面切过一次就能拿到缓存；
    /// 实测这种降级开出的租约在整段动画里一次刷新都没有，纯粹让动画处于强制连续渲染。
    /// 其余原因（尤其渲染层级过低）是持久的，低端设备每次过渡都会走到，
    /// 必须保留兜底，否则动画期间模糊会错位。
    /// </summary>
    internal static bool RequiresContinuousRefreshFallback(
        TransitionRenderCacheFallbackReason reason) =>
        reason is not (TransitionRenderCacheFallbackReason.None
            or TransitionRenderCacheFallbackReason.ElementNotReady);

    internal static TransitionRenderCacheScope TryAcquire(
        string transitionKind,
        IReadOnlyList<FrameworkElement> elements)
    {
        return TryAcquire(transitionKind, elements, TransitionRenderCacheCapabilities.Current);
    }

    internal static TransitionRenderCacheScope TryAcquire(
        string transitionKind,
        IReadOnlyList<FrameworkElement> elements,
        TransitionRenderCacheCapabilities capabilities)
    {
        var scope = TryAcquireCore(transitionKind, elements, capabilities);
        if (scope.FallbackReason is not TransitionRenderCacheFallbackReason.None)
        {
            UiPerformanceLog.LogTransitionRenderCacheFallback(
                transitionKind,
                scope.FallbackReason.ToString(),
                scope.EstimatedBytes);
        }

        return scope;
    }

    private static TransitionRenderCacheScope TryAcquireCore(
        string transitionKind,
        IReadOnlyList<FrameworkElement> elements,
        TransitionRenderCacheCapabilities capabilities)
    {
        var distinctElements = elements
            .Where(static element => element is not null)
            .Distinct()
            .ToArray();
        if (distinctElements.Length == 0)
            return CreateFallback(TransitionRenderCacheFallbackReason.NoElements);

        if (capabilities.RenderingTier < 2)
            return CreateFallback(TransitionRenderCacheFallbackReason.RenderingTierTooLow);

        var estimatedBytes = 0L;
        foreach (var element in distinctElements)
        {
            if (!element.IsLoaded
                || !element.IsVisible
                || element.ActualWidth <= 0d
                || element.ActualHeight <= 0d)
            {
                return CreateFallback(TransitionRenderCacheFallbackReason.ElementNotReady);
            }

            var dpi = VisualTreeHelper.GetDpi(element);
            var pixelWidth = Math.Max(1L, (long)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
            var pixelHeight = Math.Max(1L, (long)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
            if (capabilities.MaximumTextureSize.Width <= 0d
                || capabilities.MaximumTextureSize.Height <= 0d
                || pixelWidth > capabilities.MaximumTextureSize.Width
                || pixelHeight > capabilities.MaximumTextureSize.Height)
            {
                return CreateFallback(TransitionRenderCacheFallbackReason.TextureTooLarge);
            }

            if (pixelWidth > long.MaxValue / pixelHeight / 4L)
                return CreateFallback(TransitionRenderCacheFallbackReason.MemoryBudgetExceeded);

            var elementBytes = pixelWidth * pixelHeight * 4L;
            if (estimatedBytes > capabilities.MaximumEstimatedBytes - elementBytes)
                return CreateFallback(TransitionRenderCacheFallbackReason.MemoryBudgetExceeded);

            estimatedBytes += elementBytes;
        }

        var acquiredElements = new List<FrameworkElement>(distinctElements.Length);
        try
        {
            foreach (var element in distinctElements)
            {
                AcquireElement(element);
                acquiredElements.Add(element);
            }
        }
        catch (Exception exception)
        {
            for (var index = acquiredElements.Count - 1; index >= 0; index--)
                ReleaseElement(acquiredElements[index]);

            Log.Warning(
                exception,
                "Transition render cache initialization failed; continuing with live rendering. TransitionKind={TransitionKind}",
                transitionKind);
            return CreateFallback(TransitionRenderCacheFallbackReason.CacheCreationFailed);
        }

        return new TransitionRenderCacheScope(
            distinctElements,
            estimatedBytes,
            TransitionRenderCacheFallbackReason.None);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            return;

        foreach (var element in elements)
            element.Unloaded -= Element_Unloaded;
        for (var index = elements.Length - 1; index >= 0; index--)
            ReleaseElement(elements[index]);
    }

    private static TransitionRenderCacheScope CreateFallback(TransitionRenderCacheFallbackReason reason)
    {
        return new TransitionRenderCacheScope([], 0L, reason);
    }

    private static void AcquireElement(FrameworkElement element)
    {
        lock (CacheStateGate)
        {
            if (CacheStates.TryGetValue(element, out var existingState))
            {
                existingState.ReferenceCount++;
                return;
            }

            var originalCacheMode = element.CacheMode;
            BitmapCache? installedCache = null;
            var ownedCountIncremented = false;

            try
            {
                if (originalCacheMode is null)
                {
                    installedCache = new BitmapCache
                    {
                        RenderAtScale = 1d,
                        EnableClearType = false,
                        SnapsToDevicePixels = true
                    };
                    element.CacheMode = installedCache;
                    ActiveOwnedCacheCount++;
                    ownedCountIncremented = true;
                }

                CacheStates.Add(
                    element,
                    new CacheState(originalCacheMode, installedCache)
                    {
                        ReferenceCount = 1
                    });
            }
            catch
            {
                if (installedCache is not null)
                {
                    if (ReferenceEquals(element.CacheMode, installedCache))
                        element.CacheMode = originalCacheMode;
                    if (ownedCountIncremented)
                        ActiveOwnedCacheCount--;
                }

                throw;
            }
        }
    }

    private static void ReleaseElement(FrameworkElement element)
    {
        lock (CacheStateGate)
        {
            if (!CacheStates.TryGetValue(element, out var state))
                return;

            state.ReferenceCount--;
            if (state.ReferenceCount > 0)
                return;

            CacheStates.Remove(element);
            if (state.InstalledCache is null)
                return;

            if (ReferenceEquals(element.CacheMode, state.InstalledCache))
                element.CacheMode = state.OriginalCacheMode;
            ActiveOwnedCacheCount--;
        }
    }

    private void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private sealed class CacheState(CacheMode? originalCacheMode, BitmapCache? installedCache)
    {
        internal CacheMode? OriginalCacheMode { get; } = originalCacheMode;

        internal BitmapCache? InstalledCache { get; } = installedCache;

        internal int ReferenceCount { get; set; }
    }
}
