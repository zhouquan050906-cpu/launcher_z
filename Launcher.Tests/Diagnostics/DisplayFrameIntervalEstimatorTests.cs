/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Diagnostics;

namespace Launcher.Tests.Diagnostics;

[Collection(UiPerformanceDiagnosticsTestCollection.Name)]
public sealed class DisplayFrameIntervalEstimatorTests
{
    [Fact]
    public void ConvergesDownToTheObservedRefreshInterval()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();

        DisplayFrameIntervalEstimator.Observe(8.3d);

        Assert.Equal(8.3d, DisplayFrameIntervalEstimator.CurrentIntervalMs, 3);
    }

    [Fact]
    public void KeepsTheLowerBoundWhenLaterFramesAreSlow()
    {
        DisplayFrameIntervalEstimator.ResetForTesting();
        DisplayFrameIntervalEstimator.Observe(8.3d);

        DisplayFrameIntervalEstimator.Observe(33d);

        Assert.Equal(8.3d, DisplayFrameIntervalEstimator.CurrentIntervalMs, 3);
    }

    [Theory]
    [InlineData(0.5d)]
    [InlineData(120d)]
    [InlineData(double.NaN)]
    public void IgnoresIntervalsOutsideTheSupportedRefreshRange(double intervalMs)
    {
        DisplayFrameIntervalEstimator.ResetForTesting();

        DisplayFrameIntervalEstimator.Observe(intervalMs);

        Assert.Equal(DisplayFrameIntervalEstimator.DefaultIntervalMs, DisplayFrameIntervalEstimator.CurrentIntervalMs, 3);
    }
}
