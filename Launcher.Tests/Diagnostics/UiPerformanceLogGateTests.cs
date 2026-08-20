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
/// 性能采样会订阅合成帧回调并强制连续渲染，因此“没有显式请求时不得采样”是必须保住的约束。
/// 埋点不再跟随诊断日志开关，而是额外要求环境变量显式开启。
/// </summary>
[Collection(UiPerformanceDiagnosticsTestCollection.Name)]
public sealed class UiPerformanceLogGateTests
{
    /// <summary>
    /// 诊断日志关闭时日志根本写不出去，采样必须无条件关闭，
    /// 不受环境变量影响。
    /// </summary>
    [Fact]
    public void SamplingStaysOffWhileDiagnosticLoggingIsDisabled()
    {
        WithDiagnosticLogging(false, () => Assert.False(UiPerformanceLog.IsEnabled));
    }

    /// <summary>
    /// 这是本次改动的核心约束：即使用户打开了诊断日志，
    /// 只要没有显式请求埋点，性能采样就应该保持静默。
    /// </summary>
    [Fact]
    public void DiagnosticLoggingAloneDoesNotTurnOnSampling()
    {
        var instrumentationRequested = UiPerformanceLog.IsTruthy(
            Environment.GetEnvironmentVariable(UiPerformanceLog.InstrumentationVariableName));

        WithDiagnosticLogging(true, () => Assert.Equal(instrumentationRequested, UiPerformanceLog.IsEnabled));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("  1  ", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    public void InstrumentationRequestParsesOnlyExplicitOptIn(string? value, bool expected)
    {
        Assert.Equal(expected, UiPerformanceLog.IsTruthy(value));
    }

    private static void WithDiagnosticLogging(bool enabled, Action assert)
    {
        var originalLogger = Log.Logger;
        var levelSwitch = new LoggingLevelSwitch(LauncherLogLevelController.ResolveMinimumLevel(enabled));
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .CreateLogger();

        try
        {
            assert();
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
