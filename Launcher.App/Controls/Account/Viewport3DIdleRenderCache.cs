/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;

namespace Launcher.App.Controls.Account;

/// <summary>
/// 静止时只缓存单个 Viewport3D 的最终像素；场景或轮播动画变化时立即恢复实时 3D 渲染。
/// </summary>
internal sealed class Viewport3DIdleRenderCache
{
    internal const long MaximumTextureBytes = 8L * 1024 * 1024;

    private readonly Viewport3D viewport;
    private readonly string viewportName;
    private int generation;

    internal Viewport3DIdleRenderCache(Viewport3D viewport, string viewportName)
    {
        this.viewport = viewport;
        this.viewportName = viewportName;
    }

    internal bool IsActive => viewport.CacheMode is BitmapCache;

    internal void Disable(string reason)
    {
        generation++;
        if (viewport.CacheMode is not BitmapCache)
            return;

        viewport.CacheMode = null;
    }

    internal void QueueEnable(Func<bool> isSceneStable)
    {
        var requestedGeneration = ++generation;
        viewport.Dispatcher.BeginInvoke(
            () => TryEnable(requestedGeneration, isSceneStable),
            DispatcherPriority.Render);
    }

    private void TryEnable(int requestedGeneration, Func<bool> isSceneStable)
    {
        if (requestedGeneration != generation
            || IsActive
            || !viewport.IsLoaded
            || !isSceneStable())
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(viewport);
        if (!TryEstimateTextureBytes(
                viewport.ActualWidth,
                viewport.ActualHeight,
                dpi.DpiScaleX,
                dpi.DpiScaleY,
                RenderCapability.Tier,
                out _,
                out _))
        {
            return;
        }

        try
        {
            viewport.CacheMode = CreateCache();
        }
        catch (Exception ex)
        {
            viewport.CacheMode = null;
            Log.Warning(
                ex,
                "Account {ViewportName} Viewport3D cache initialization failed; continuing with live rendering",
                viewportName);
        }
    }

    internal static BitmapCache CreateCache()
    {
        var cache = new BitmapCache
        {
            RenderAtScale = 1d,
            EnableClearType = false,
            SnapsToDevicePixels = true
        };
        cache.Freeze();
        return cache;
    }

    internal static bool TryEstimateTextureBytes(
        double width,
        double height,
        double dpiScaleX,
        double dpiScaleY,
        int renderTier,
        out long estimatedTextureBytes,
        out string reason)
    {
        estimatedTextureBytes = 0;
        if ((renderTier >> 16) < 2)
        {
            reason = "RenderTierBelow2";
            return false;
        }

        if (!double.IsFinite(width)
            || !double.IsFinite(height)
            || !double.IsFinite(dpiScaleX)
            || !double.IsFinite(dpiScaleY)
            || width <= 0d
            || height <= 0d
            || dpiScaleX <= 0d
            || dpiScaleY <= 0d)
        {
            reason = "InvalidSizeOrDpi";
            return false;
        }

        var pixelWidth = (long)Math.Ceiling(width * dpiScaleX);
        var pixelHeight = (long)Math.Ceiling(height * dpiScaleY);
        try
        {
            estimatedTextureBytes = checked(pixelWidth * pixelHeight * 4L);
        }
        catch (OverflowException)
        {
            estimatedTextureBytes = long.MaxValue;
        }

        if (estimatedTextureBytes > MaximumTextureBytes)
        {
            reason = "TextureBudgetExceeded";
            return false;
        }

        reason = "None";
        return true;
    }
}
