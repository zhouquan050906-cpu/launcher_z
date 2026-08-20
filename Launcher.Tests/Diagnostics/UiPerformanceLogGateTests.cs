/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Diagnostics;
using Launcher.App.Logging;
using Serilog;
using Serilog.Core;

namespace Launcher.Tests.Diagnostics;

/// <summary>
/// 性能采样会订阅合成帧回调并强制连续渲染，因此“关闭诊断日志时不得采样”是必须保住的约束。
/// </summary>
[Collection(UiPerformanceDiagnosticsTestCollection.Name)]
public sealed class UiPerformanceLogGateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SamplingFollowsTheDiagnosticLoggingLevel(bool diagnosticLoggingEnabled, bool expectedEnabled)
    {
        var originalLogger = Log.Logger;
        var levelSwitch = new LoggingLevelSwitch(
            LauncherLogLevelController.ResolveMinimumLevel(diagnosticLoggingEnabled));
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .CreateLogger();

        try
        {
            Assert.Equal(expectedEnabled, UiPerformanceLog.IsEnabled);
        }
        finally
        {
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public void DisabledInteractionScopeNeverSamplesFrames()
    {
        using var scope = new UiInteractionScope("PageTransition", "Home", isSampling: false);

        Assert.Equal(0, scope.SampledFrameCount);
    }
}
