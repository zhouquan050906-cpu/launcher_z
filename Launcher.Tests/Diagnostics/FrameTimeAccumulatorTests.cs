/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Diagnostics;

namespace Launcher.Tests.Diagnostics;

[Collection(UiPerformanceDiagnosticsTestCollection.Name)]
public sealed class FrameTimeAccumulatorTests
{
    [Fact]
    public void FirstRenderingTimeOnlyEstablishesTheBaseline()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        var accumulator = new FrameTimeAccumulator();

        accumulator.AddRenderingTime(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, accumulator.FrameCount);
        Assert.Equal(0d, accumulator.TotalIntervalMs);
    }

    [Fact]
    public void RepeatedAndRegressingRenderingTimesAreIgnored()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        var accumulator = new FrameTimeAccumulator();
        accumulator.AddRenderingTime(TimeSpan.FromMilliseconds(100));

        accumulator.AddRenderingTime(TimeSpan.FromMilliseconds(116));
        accumulator.AddRenderingTime(TimeSpan.FromMilliseconds(116));
        accumulator.AddRenderingTime(TimeSpan.FromMilliseconds(110));

        Assert.Equal(1, accumulator.FrameCount);
        Assert.Equal(16d, accumulator.TotalIntervalMs, 3);
    }

    [Fact]
    public void CountsOnlyIntervalsBeyondOneAndAHalfDisplayFramesAsJank()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        var accumulator = new FrameTimeAccumulator();
        var renderingTime = TimeSpan.Zero;

        // 16.7ms 与 20ms 属于正常抖动，100ms 才是用户能看到的停顿。
        foreach (var intervalMs in new[] { 16.7d, 20d, 100d, 16.7d })
        {
            renderingTime += TimeSpan.FromMilliseconds(intervalMs);
            accumulator.AddRenderingTime(renderingTime);
        }

        Assert.Equal(3, accumulator.FrameCount);
        Assert.Equal(1, accumulator.JankFrameCount);
        Assert.Equal(100d, accumulator.MaxIntervalMs, 3);
    }

    [Fact]
    public void ReportsPercentileAndAverageFromSampledIntervals()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        var accumulator = new FrameTimeAccumulator();
        var renderingTime = TimeSpan.Zero;
        foreach (var intervalMs in new[] { 10d, 20d, 30d, 40d })
        {
            renderingTime += TimeSpan.FromMilliseconds(intervalMs);
            accumulator.AddRenderingTime(renderingTime);
        }

        Assert.Equal(3, accumulator.FrameCount);
        Assert.Equal(30d, accumulator.AverageIntervalMs, 3);
        Assert.Equal(40d, accumulator.GetPercentileIntervalMs(95d), 3);
        Assert.Equal(20d, accumulator.GetPercentileIntervalMs(1d), 3);
    }

    [Fact]
    public void KeepsAggregatingAfterTheSampleBufferIsSaturated()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        var accumulator = new FrameTimeAccumulator();
        var renderingTime = TimeSpan.Zero;
        var frameCount = FrameTimeAccumulator.MaximumSampledIntervals + 10;
        for (var index = 0; index <= frameCount; index++)
        {
            renderingTime += TimeSpan.FromMilliseconds(16d);
            accumulator.AddRenderingTime(renderingTime);
        }

        Assert.True(accumulator.IsSampleBufferSaturated);
        Assert.Equal(frameCount, accumulator.FrameCount);
        Assert.Equal(16d, accumulator.AverageIntervalMs, 3);
    }
}
