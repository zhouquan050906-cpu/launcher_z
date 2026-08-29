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
using System.Windows.Threading;
using Serilog;

namespace Launcher.App.Diagnostics;

/// <summary>
/// 测量一次 UI 交互（页面切换、层切换、滚动）期间的实际出帧情况，并在结束时输出一行摘要。
/// 只有开启诊断日志时才会真正订阅合成帧回调，避免默认运行被强制进入连续渲染。
/// </summary>
internal sealed class UiInteractionScope : IDisposable
{
    private readonly string kind;
    private readonly string detail;
    private readonly bool isSampling;
    // 长帧才单独记录：正常帧数以千计，全记会把日志淹掉。
    private const double LongFrameCycleFactor = 3d;

    private readonly FrameTimeAccumulator? accumulator;
    private readonly DispatcherBusyProbe? busyProbe;
    private readonly FrameworkElement? layoutProbe;
    private readonly long startedAtTimestamp;
    private int layoutPassCount;
    private (long Batches, long Refreshes) backdropStart;
    private (int Gen0, int Gen1, int Gen2) gcStart;
    private double scrollOffsetLast;
    private double scrollDistanceTotal;
    private bool hasScrollBaseline;
    private bool isDisposed;

    internal UiInteractionScope(
        string kind,
        string detail,
        bool isSampling,
        FrameworkElement? layoutProbe = null)
    {
        this.kind = kind;
        this.detail = detail;
        this.isSampling = isSampling;
        if (!isSampling)
            return;

        accumulator = new FrameTimeAccumulator();
        busyProbe = DispatcherBusyProbe.TryAttach(
            global::System.Windows.Application.Current?.Dispatcher);
        gcStart = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        startedAtTimestamp = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += CompositionTarget_Rendering;

        // LayoutUpdated 在每次全局布局 pass 结束后触发，
        // 因此它与帧数的比值直接区分"每帧重新布局"和"只是重新合成"。
        this.layoutProbe = layoutProbe;
        if (layoutProbe is not null)
            layoutProbe.LayoutUpdated += LayoutProbe_LayoutUpdated;
    }

    /// <summary>
    /// 交互实际使用的渲染优化路径，例如位图缓存或连续背板刷新，用于把掉帧与优化选择对应起来。
    /// </summary>
    internal string RenderPath { get; set; } = "Unknown";

    /// <summary>
    /// 动画开始前的预热耗时。预热不计入出帧统计，单独记录才能看出开销是被消除了还是只是挪了位置。
    /// </summary>
    internal double WarmupMs { get; set; }

    /// <summary>
    /// 预热窗口内 UI 线程执行 Dispatcher 操作的累计时长，以及其中最长的那个操作。
    /// 用来区分预热到底是在等渲染线程，还是 UI 线程自己在忙——两者的修法完全不同。
    /// </summary>
    internal double WarmupBusyMs { get; set; }

    internal double WarmupWorstOperationMs { get; set; }

    internal string WarmupWorstOperation { get; set; } = DispatcherBusyProbe.NoOperationDetail;

    /// <summary>
    /// 交互期间被反复重绘的表面尺寸。若瓶颈是像素填充率而非元素处理，
    /// 帧时间应当随这块面积变化，而与元素数量无关。
    /// </summary>
    internal double SurfaceWidth { get; set; }

    internal double SurfaceHeight { get; set; }

    /// <summary>
    /// 交互期间背板模糊的批次/刷新增量，以及该表面关联的背板控件数。
    /// </summary>
    internal Func<(long Batches, long Refreshes)>? BackdropCounterReader { get; set; }

    internal int BackdropControlCount { get; set; }

    /// <summary>
    /// 交互开始时，该表面是否处在某个位图缓存之下。用于把帧数据按缓存状态自动分组。
    /// </summary>
    internal bool HasAncestorBitmapCache { get; set; }

    internal bool HasOpacityMask { get; set; }

    /// <summary>
    /// 交互期间滚动位移的读取器。滚动速度直接决定每帧的重绘量，
    /// 不记录它就无法判断两段测量是否可比。
    /// </summary>
    internal Func<double>? ScrollOffsetReader { get; set; }

    internal int SampledFrameCount => accumulator?.FrameCount ?? 0;

    /// <summary>
    /// 采样开始后由调用方补齐背板计数基线；必须在计数读取器就位之后调用。
    /// </summary>
    internal void CaptureBackdropBaseline()
    {
        backdropStart = BackdropCounterReader?.Invoke() ?? (0L, 0L);
        if (ScrollOffsetReader is not null)
        {
            scrollOffsetLast = ScrollOffsetReader();
            hasScrollBaseline = true;
        }
    }

    public void Dispose()
    {
        if (isDisposed || !isSampling)
            return;

        isDisposed = true;
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        if (layoutProbe is not null)
            layoutProbe.LayoutUpdated -= LayoutProbe_LayoutUpdated;
        // 先摘钩再读累计值：Dispose 只解除订阅，统计结果仍然可读。
        busyProbe?.Dispose();
        if (accumulator is null)
            return;

        var durationMs = Stopwatch.GetElapsedTime(startedAtTimestamp).TotalMilliseconds;
        var backdropEnd = BackdropCounterReader?.Invoke() ?? (0L, 0L);
        AccumulateScrollDistance();
        var scrollDistance = scrollDistanceTotal;

        // 没有出帧的交互通常是刚开始就被取消的，记录它只会淹没真正的卡顿样本。
        if (accumulator.FrameCount == 0)
            return;

        Log.Debug(
            "UI interaction frames sampled. Kind={InteractionKind} Detail={InteractionDetail} RenderPath={RenderPath} "
            + "DurationMs={DurationMs:F1} FrameCount={FrameCount} Fps={FramesPerSecond:F1} AvgFrameMs={AverageFrameMs:F2} "
            + "P95FrameMs={P95FrameMs:F2} MaxFrameMs={MaxFrameMs:F2} JankFrames={JankFrameCount} DisplayFrameMs={DisplayFrameMs:F2} "
            + "LayoutPasses={LayoutPassCount} LayoutPerFrame={LayoutPerFrame:F2} "
            + "SurfaceWidth={SurfaceWidth:F0} SurfaceHeight={SurfaceHeight:F0} SurfaceMegapixels={SurfaceMegapixels:F3} "
            + "BackdropControls={BackdropControlCount} BackdropBatches={BackdropBatches} BackdropRefreshes={BackdropRefreshes} "
            + "BackdropRefreshPerFrame={BackdropRefreshPerFrame:F2} BitmapCache={HasAncestorBitmapCache} OpacityMask={HasOpacityMask} "
            + "Cycle1={Cycle1} Cycle2={Cycle2} Cycle3={Cycle3} Cycle4Plus={Cycle4Plus} LongRun={MaxConsecutiveLongFrames} "
            + "Gc0={Gc0} Gc1={Gc1} Gc2={Gc2} ScrollPx={ScrollPx:F0} ScrollPxPerSec={ScrollPxPerSec:F0} "
            + "UiBusyMs={UiBusyMs:F1} UiBusyShare={UiBusyShare:F2} DispatcherOps={DispatcherOperationCount} "
            + "WorstOpMs={WorstOperationMs:F1} WorstOp={WorstOperation} WarmupMs={WarmupMs:F1} "
            + "WarmupBusyMs={WarmupBusyMs:F1} WarmupWorstOpMs={WarmupWorstOperationMs:F1} "
            + "WarmupWorstOp={WarmupWorstOperation}",
            kind,
            detail,
            RenderPath,
            durationMs,
            accumulator.FrameCount,
            accumulator.EstimatedFramesPerSecond,
            accumulator.AverageIntervalMs,
            accumulator.GetPercentileIntervalMs(95d),
            accumulator.MaxIntervalMs,
            accumulator.JankFrameCount,
            DisplayFrameIntervalEstimator.CurrentIntervalMs,
            layoutPassCount,
            accumulator.FrameCount == 0 ? 0d : (double)layoutPassCount / accumulator.FrameCount,
            SurfaceWidth,
            SurfaceHeight,
            SurfaceWidth * SurfaceHeight / 1_000_000d,
            BackdropControlCount,
            backdropEnd.Item1 - backdropStart.Batches,
            backdropEnd.Item2 - backdropStart.Refreshes,
            accumulator.FrameCount == 0
                ? 0d
                : (double)(backdropEnd.Item2 - backdropStart.Refreshes) / accumulator.FrameCount,
            HasAncestorBitmapCache,
            HasOpacityMask,
            accumulator.GetCycleFrameCount(1),
            accumulator.GetCycleFrameCount(2),
            accumulator.GetCycleFrameCount(3),
            accumulator.GetCycleFrameCount(4),
            accumulator.MaxConsecutiveLongFrames,
            GC.CollectionCount(0) - gcStart.Gen0,
            GC.CollectionCount(1) - gcStart.Gen1,
            GC.CollectionCount(2) - gcStart.Gen2,
            scrollDistance,
            durationMs <= 0d ? 0d : scrollDistance * 1000d / durationMs,
            busyProbe?.TotalBusyMs ?? 0d,
            durationMs <= 0d ? 0d : (busyProbe?.TotalBusyMs ?? 0d) / durationMs,
            busyProbe?.TotalOperationCount ?? 0,
            busyProbe?.WorstOperationMs ?? 0d,
            busyProbe?.WorstOperationDetail ?? DispatcherBusyProbe.NoOperationDetail,
            WarmupMs,
            WarmupBusyMs,
            WarmupWorstOperationMs,
            WarmupWorstOperation);
    }

    private void LayoutProbe_LayoutUpdated(object? sender, EventArgs e)
    {
        layoutPassCount++;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs renderingEventArgs)
        {
            var intervalMs = accumulator?.AddRenderingTime(renderingEventArgs.RenderingTime) ?? 0d;
            if (intervalMs > 0d)
                LogLongFrame(intervalMs);
        }

        // 统计窗口以合成帧为界，因此归零必须发生在这一帧记录之后。
        busyProbe?.ResetFrame();

        // 逐帧累加位移绝对值：来回滚动时首尾差接近零，只有累计值能反映真实渲染压力。
        AccumulateScrollDistance();
    }

    /// <summary>
    /// 记录一个明显长于显示周期的帧，并给出这一帧里 UI 线程的忙碌情况。
    /// UiBusyShare 接近 1 说明这一帧是被 UI 线程上的工作挤掉的，接近 0 说明 UI 线程空闲，
    /// 时间花在渲染线程或 GPU 上——两者的处理方向完全不同。
    /// </summary>
    private void LogLongFrame(double intervalMs)
    {
        if (busyProbe is null)
            return;

        var displayFrameMs = DisplayFrameIntervalEstimator.CurrentIntervalMs;
        if (intervalMs <= displayFrameMs * LongFrameCycleFactor)
            return;

        Log.Debug(
            "UI long frame observed. Kind={InteractionKind} Detail={InteractionDetail} FrameMs={FrameMs:F1} "
            + "DisplayFrameMs={DisplayFrameMs:F2} Cycles={FrameCycles} UiBusyMs={UiBusyMs:F1} "
            + "UiBusyShare={UiBusyShare:F2} DispatcherOps={DispatcherOperationCount} "
            + "LongestOpMs={LongestOperationMs:F1} LongestOp={LongestOperation}",
            kind,
            detail,
            intervalMs,
            displayFrameMs,
            displayFrameMs <= 0d ? 0 : (int)Math.Round(intervalMs / displayFrameMs),
            busyProbe.FrameBusyMs,
            busyProbe.FrameBusyMs / intervalMs,
            busyProbe.FrameOperationCount,
            busyProbe.FrameLongestOperationMs,
            busyProbe.FrameLongestOperationDetail);
    }

    private void AccumulateScrollDistance()
    {
        if (!hasScrollBaseline || ScrollOffsetReader is null)
            return;

        var current = ScrollOffsetReader();
        scrollDistanceTotal += Math.Abs(current - scrollOffsetLast);
        scrollOffsetLast = current;
    }
}
