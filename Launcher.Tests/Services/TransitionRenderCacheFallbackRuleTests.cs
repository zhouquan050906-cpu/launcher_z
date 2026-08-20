/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;

namespace Launcher.Tests.Services;

/// <summary>
/// 位图缓存拿不到时是否需要连续刷新兜底，取决于失败原因是不是一次性的。
/// </summary>
public sealed class TransitionRenderCacheFallbackRuleTests
{
    [Fact]
    public void OneShotFailuresDoNotRequireContinuousRefresh()
    {
        // 首次布局尚未完成属于一次性时序问题：此时作用域内没有可刷新的背板模糊，
        // 开租约只会让整段动画处于强制连续渲染。
        Assert.False(TransitionRenderCacheScope.RequiresContinuousRefreshFallback(
            TransitionRenderCacheFallbackReason.ElementNotReady));
        Assert.False(TransitionRenderCacheScope.RequiresContinuousRefreshFallback(
            TransitionRenderCacheFallbackReason.None));
    }

    [Fact]
    public void PersistentFailuresKeepContinuousRefresh()
    {
        // 这些原因不会随下一次过渡自行消失，低端设备每次过渡都会走到，
        // 去掉兜底会让动画期间的背板模糊错位。
        var persistent = new[]
        {
            TransitionRenderCacheFallbackReason.RenderingTierTooLow,
            TransitionRenderCacheFallbackReason.TextureTooLarge,
            TransitionRenderCacheFallbackReason.MemoryBudgetExceeded,
            TransitionRenderCacheFallbackReason.CacheCreationFailed,
            TransitionRenderCacheFallbackReason.NoElements
        };

        Assert.All(
            persistent,
            reason => Assert.True(
                TransitionRenderCacheScope.RequiresContinuousRefreshFallback(reason)));
    }
}
