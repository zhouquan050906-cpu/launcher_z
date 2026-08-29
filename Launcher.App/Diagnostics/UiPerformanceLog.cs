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

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.Application.Services;
using Serilog;
using Serilog.Events;

namespace Launcher.App.Diagnostics;

/// <summary>
/// 界面性能诊断日志的统一入口。所有采样都以诊断日志开关为闸门，
/// 关闭时不订阅合成帧、不计时、不分配，正常运行的日志和渲染负载保持不变。
/// </summary>
internal static class UiPerformanceLog
{
    private static readonly ConcurrentDictionary<string, byte> LoggedFallbacks = new(StringComparer.Ordinal);
    private static readonly HashSet<ScrollViewer> LoggedScrollSurfaces = [];
    private static readonly HashSet<FrameworkElement> LoggedPageSurfaces = [];
    private static int hasLoggedRenderEnvironment;

    /// <summary>
    /// 界面性能埋点默认完全关闭，不随诊断日志开关一起输出：
    /// 它的采样字段是为性能调查准备的，混进常规诊断日志只会淹没真正有用的信息。
    /// 需要排查卡顿时，把环境变量 <c>BHL_UI_PERF_DIAG</c> 设为 1（或 true/yes/on）
    /// 并同时打开诊断日志开关，采样即恢复。
    /// </summary>
    internal static bool IsEnabled => IsInstrumentationRequested && Log.IsEnabled(LogEventLevel.Debug);

    internal const string InstrumentationVariableName = "BHL_UI_PERF_DIAG";

    /// <summary>
    /// 环境变量只在进程启动时读一次：埋点判断位于每帧路径上，
    /// 不能承担反复查询环境变量的开销。
    /// </summary>
    private static readonly bool IsInstrumentationRequested =
        IsTruthy(Environment.GetEnvironmentVariable(InstrumentationVariableName));

    internal static bool IsTruthy(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null
            && (trimmed.Equals("1", StringComparison.Ordinal)
                || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 开始一次交互采样。关闭诊断日志或不在 UI 线程时返回不做任何事的作用域。
    /// </summary>
    internal static UiInteractionScope BeginInteraction(
        string kind,
        string detail,
        FrameworkElement? layoutProbe = null)
    {
        return new UiInteractionScope(kind, detail, IsEnabled && HasUiThreadAccess(), layoutProbe);
    }

    /// <summary>
    /// 记录一个滚动区域的实际配置和视觉树规模。这些是设备无关的结构事实，
    /// 每个 ScrollViewer 只记一次，用来把"滚动慢"归因到具体结构而不是猜测。
    /// </summary>
    internal static void LogScrollSurface(ScrollViewer scrollViewer, string detail)
    {
        if (!IsEnabled || !LoggedScrollSurfaces.Add(scrollViewer))
            return;

        var content = scrollViewer.Content as DependencyObject;
        var treeSize = VisualTreeStats.Measure(scrollViewer);
        Log.Debug(
            "Scroll surface inspected. Detail={ScrollDetail} CanContentScroll={CanContentScroll} "
            + "ContentPanel={ContentPanel} ExtentHeight={ExtentHeight:F0} ViewportHeight={ViewportHeight:F0} "
            + "ScrollableHeight={ScrollableHeight:F0} VisualElements={VisualElementCount} VisualDepth={VisualDepth} "
            + "TreeTruncated={TreeTruncated} Effects={EffectCount} VisibleEffects={VisibleEffectCount} "
            + "DropShadows={DropShadowCount} CardShadows={CardShadowCount} "
            + "EffectBreakdown={EffectBreakdown} VisibleEffectHosts={VisibleEffectHosts}",
            detail,
            scrollViewer.CanContentScroll,
            content?.GetType().Name ?? "<none>",
            scrollViewer.ExtentHeight,
            scrollViewer.ViewportHeight,
            scrollViewer.ScrollableHeight,
            treeSize.ElementCount,
            treeSize.MaxDepth,
            treeSize.IsTruncated,
            treeSize.EffectCount,
            treeSize.VisibleEffectCount,
            treeSize.DropShadowCount,
            treeSize.CardShadowCount,
            treeSize.EffectBreakdown,
            treeSize.VisibleEffectHosts);
    }

    /// <summary>
    /// 记录一个页面的视觉树规模。用于把"某些页面就是更容易顿"和具体的结构成本对应上：
    /// 元素数决定遍历与布局成本，Effect 数量决定每帧的中间渲染层数，表面积决定填充率。
    /// 每个页面只记一次。
    /// </summary>
    internal static void LogPageSurface(FrameworkElement page, string detail)
    {
        if (!IsEnabled || !LoggedPageSurfaces.Add(page))
            return;

        var size = VisualTreeStats.Measure(page);
        Log.Debug(
            "Page surface inspected. Page={PageDetail} Width={SurfaceWidth:F0} Height={SurfaceHeight:F0} "
            + "Megapixels={SurfaceMegapixels:F3} VisualElements={VisualElementCount} VisualDepth={VisualDepth} "
            + "TreeTruncated={TreeTruncated} Effects={EffectCount} VisibleEffects={VisibleEffectCount} "
            + "DropShadows={DropShadowCount} CardShadows={CardShadowCount} EffectBreakdown={EffectBreakdown}",
            detail,
            page.ActualWidth,
            page.ActualHeight,
            page.ActualWidth * page.ActualHeight / 1_000_000d,
            size.ElementCount,
            size.MaxDepth,
            size.IsTruncated,
            size.EffectCount,
            size.VisibleEffectCount,
            size.DropShadowCount,
            size.CardShadowCount,
            size.EffectBreakdown);
    }

    /// <summary>
    /// 在启动时记录一次渲染环境快照。渲染层级和软件渲染是老机器卡顿的首要判定依据，
    /// 所以它跟随诊断日志开关而不是埋点环境变量：用户报障时只会被引导着打开诊断日志，
    /// 藏在环境变量后面等于拿不到。单行且每进程只写一次，常规运行不会看到它。
    /// </summary>
    internal static void LogRenderEnvironment(SystemMemorySnapshot? memorySnapshot, Visual? dpiSource)
    {
        if (!Log.IsEnabled(LogEventLevel.Debug))
            return;

        if (Interlocked.Exchange(ref hasLoggedRenderEnvironment, 1) != 0)
            return;

        var dpiScale = TryGetDpiScale(dpiSource);
        Log.Debug(
            "Render environment evaluated. RenderTier={RenderTier} ProcessRenderMode={ProcessRenderMode} "
            + "PixelShader30Supported={PixelShader30Supported} MaxTextureSize={MaxTextureWidth}x{MaxTextureHeight} "
            + "DpiScale={DpiScale} ProcessorCount={ProcessorCount} TotalMemoryMb={TotalMemoryMb} "
            + "AvailableMemoryMb={AvailableMemoryMb} ProcessArchitecture={ProcessArchitecture} OsDescription={OsDescription}",
            RenderCapability.Tier >> 16,
            RenderOptions.ProcessRenderMode,
            RenderCapability.IsPixelShaderVersionSupported(3, 0),
            (int)RenderCapability.MaxHardwareTextureSize.Width,
            (int)RenderCapability.MaxHardwareTextureSize.Height,
            dpiScale?.ToString("F2") ?? "unknown",
            Environment.ProcessorCount,
            memorySnapshot is null ? -1 : memorySnapshot.TotalMemoryBytes / (1024 * 1024),
            memorySnapshot is null ? -1 : memorySnapshot.AvailableMemoryBytes / (1024 * 1024),
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSDescription);
    }

    /// <summary>
    /// 记录过渡位图缓存不可用的原因。同一组合只记录一次，
    /// 因为它反映的是设备能力而不是单次交互，重复写入没有额外诊断价值。
    /// </summary>
    internal static void LogTransitionRenderCacheFallback(
        string transitionKind,
        string fallbackReason,
        long estimatedBytes)
    {
        if (!IsEnabled)
            return;

        if (!LoggedFallbacks.TryAdd($"{transitionKind}|{fallbackReason}", 0))
            return;

        Log.Debug(
            "Transition render cache unavailable; falling back to live rendering. TransitionKind={TransitionKind} "
            + "Reason={FallbackReason} EstimatedBytes={EstimatedBytes} RenderTier={RenderTier}",
            transitionKind,
            fallbackReason,
            estimatedBytes,
            RenderCapability.Tier >> 16);
    }

    /// <summary>
    /// 记录一次连续背板刷新租约的开销。连续刷新会在动画期间强制每帧重建模糊，
    /// 是低端设备上最贵的一条路径，需要能和掉帧摘要对上。
    /// </summary>
    internal static void LogContinuousBackdropRefresh(
        double durationMs,
        int scopeCount,
        long batchCount,
        long refreshCount)
    {
        if (!IsEnabled)
            return;

        Log.Debug(
            "Continuous backdrop refresh completed. DurationMs={DurationMs:F1} ScopeCount={ScopeCount} "
            + "BatchCount={BatchCount} RefreshCount={RefreshCount} RefreshPerBatch={RefreshPerBatch:F2}",
            durationMs,
            scopeCount,
            batchCount,
            refreshCount,
            batchCount == 0 ? 0d : (double)refreshCount / batchCount);
    }

    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref hasLoggedRenderEnvironment, 0);
        LoggedFallbacks.Clear();
        LoggedScrollSurfaces.Clear();
    }

    private static bool HasUiThreadAccess()
    {
        return global::System.Windows.Application.Current?.Dispatcher.CheckAccess() == true;
    }

    private static double? TryGetDpiScale(Visual? dpiSource)
    {
        if (dpiSource is null)
            return null;

        try
        {
            return VisualTreeHelper.GetDpi(dpiSource).DpiScaleX;
        }
        catch (InvalidOperationException)
        {
            // 视觉树尚未连接到渲染目标时不可用；DPI 只是补充信息，不值得让启动流程失败。
            return null;
        }
    }
}

/// <summary>
/// 一次过渡实际走到的渲染路径。位图缓存最省，连续背板刷新最贵，
/// 记录它才能解释同一台机器上不同页面的掉帧差异。
/// </summary>
internal static class UiRenderPaths
{
    internal const string BitmapCache = "BitmapCache";
    internal const string ContinuousBackdropRefresh = "ContinuousBackdropRefresh";
    internal const string Live = "Live";

    internal static string Resolve(bool usesBitmapCache, bool usesContinuousBackdropRefresh)
    {
        if (usesBitmapCache)
            return BitmapCache;

        return usesContinuousBackdropRefresh ? ContinuousBackdropRefresh : Live;
    }
}
