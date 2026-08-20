/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Common;

/// <summary>
/// 阴影的位图缓存只在硬件渲染层级、且纹理落在显存预算内时才启用，
/// 否则必须回退到直接绘制。
/// </summary>
public sealed class CardShadowChromeRenderCacheTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void RenderCacheOnlyAppliesOnHardwareTiers(int renderingTier, bool expected)
    {
        Assert.Equal(expected, CardShadowChrome.ShouldUseRenderCache(renderingTier, 320d, 180d, 1d));
    }

    [Theory]
    [InlineData(0d, 180d)]
    [InlineData(320d, 0d)]
    public void RenderCacheIsSkippedBeforeSizeIsKnown(double width, double height)
    {
        Assert.False(CardShadowChrome.ShouldUseRenderCache(2, width, height, 1d));
    }

    [Fact]
    public void RenderCacheIsSkippedWhenTextureExceedsBudget()
    {
        // 8MB 预算约合 1448x1448 像素；明显更大的表面必须回退。
        Assert.True(CardShadowChrome.ShouldUseRenderCache(2, 1400d, 1400d, 1d));
        Assert.False(CardShadowChrome.ShouldUseRenderCache(2, 2400d, 2400d, 1d));
    }

    [Fact]
    public void RenderCacheAccountsForDpiScale()
    {
        // 同一逻辑尺寸在高 DPI 下需要更大的纹理，预算判定必须跟着放大。
        Assert.True(CardShadowChrome.ShouldUseRenderCache(2, 1000d, 1000d, 1d));
        Assert.False(CardShadowChrome.ShouldUseRenderCache(2, 1000d, 1000d, 2d));
    }
}
