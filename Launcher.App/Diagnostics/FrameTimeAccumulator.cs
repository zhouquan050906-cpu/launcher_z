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

namespace Launcher.App.Diagnostics;

/// <summary>
/// 累积一次交互期间的合成帧间隔，用于在交互结束时输出一行掉帧摘要。
/// 每帧只做常数级算术，不分配对象，避免采样本身影响被测量的动画。
/// </summary>
internal sealed class FrameTimeAccumulator
{
    // 一次交互通常在 1 秒内结束；容量足以覆盖高刷新率显示器，
    // 超出后聚合值继续累积，只有分位数改为基于已采样区间估算。
    internal const int MaximumSampledIntervals = 512;

    private const double JankIntervalFactor = 1.5d;

    private readonly double[] sampledIntervalsMs = new double[MaximumSampledIntervals];
    // 帧间隔被 vsync 量化成刷新周期的整数倍，因此按周期分桶才能看出节奏是否稳定。
    // 索引 1..3 为对应周期数，索引 4 汇总 4 个及以上周期。
    private readonly int[] cycleBuckets = new int[5];
    private int sampledCount;
    private int consecutiveLongFrames;
    private TimeSpan? lastRenderingTime;

    internal int FrameCount { get; private set; }

    internal int JankFrameCount { get; private set; }

    internal double MaxIntervalMs { get; private set; }

    internal double TotalIntervalMs { get; private set; }

    /// <summary>
    /// 连续出现 3 个及以上刷新周期的最长长帧串。成簇出现说明是离散事件
    /// （GC、缓存重建），均匀散布说明是持续的渲染压力。
    /// </summary>
    internal int MaxConsecutiveLongFrames { get; private set; }

    internal int GetCycleFrameCount(int cycles) =>
        cycles >= 1 && cycles < cycleBuckets.Length ? cycleBuckets[cycles] : 0;

    internal bool IsSampleBufferSaturated => sampledCount >= MaximumSampledIntervals;

    internal double AverageIntervalMs => FrameCount == 0 ? 0d : TotalIntervalMs / FrameCount;

    internal double EstimatedFramesPerSecond =>
        TotalIntervalMs <= 0d ? 0d : FrameCount * 1000d / TotalIntervalMs;

    /// <summary>
    /// 记录一次合成帧回调。重复或回退的渲染时间戳会被忽略，与背板刷新协调器的去重口径一致。
    /// </summary>
    /// <returns>与上一帧的间隔（毫秒）；本次调用未计入统计时返回 0。</returns>
    internal double AddRenderingTime(TimeSpan renderingTime)
    {
        if (lastRenderingTime is not { } previousRenderingTime)
        {
            lastRenderingTime = renderingTime;
            return 0d;
        }

        if (renderingTime <= previousRenderingTime)
            return 0d;

        lastRenderingTime = renderingTime;
        var intervalMs = (renderingTime - previousRenderingTime).TotalMilliseconds;
        DisplayFrameIntervalEstimator.Observe(intervalMs);

        FrameCount++;
        TotalIntervalMs += intervalMs;
        if (intervalMs > MaxIntervalMs)
            MaxIntervalMs = intervalMs;
        if (intervalMs > DisplayFrameIntervalEstimator.CurrentIntervalMs * JankIntervalFactor)
            JankFrameCount++;
        if (sampledCount < MaximumSampledIntervals)
            sampledIntervalsMs[sampledCount++] = intervalMs;

        var cycles = Math.Clamp(
            (int)Math.Round(intervalMs / DisplayFrameIntervalEstimator.CurrentIntervalMs),
            1,
            cycleBuckets.Length - 1);
        cycleBuckets[cycles]++;
        if (cycles >= 3)
        {
            consecutiveLongFrames++;
            if (consecutiveLongFrames > MaxConsecutiveLongFrames)
                MaxConsecutiveLongFrames = consecutiveLongFrames;
        }
        else
        {
            consecutiveLongFrames = 0;
        }

        return intervalMs;
    }

    /// <summary>
    /// 返回已采样帧间隔的分位数；没有样本时返回 0。
    /// </summary>
    internal double GetPercentileIntervalMs(double percentile)
    {
        if (sampledCount == 0)
            return 0d;

        var ordered = new double[sampledCount];
        Array.Copy(sampledIntervalsMs, ordered, sampledCount);
        Array.Sort(ordered);
        var rank = (int)Math.Ceiling(percentile / 100d * sampledCount) - 1;
        return ordered[Math.Clamp(rank, 0, sampledCount - 1)];
    }
}
