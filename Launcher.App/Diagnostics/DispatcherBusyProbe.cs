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
using System.Reflection;
using System.Windows.Threading;

namespace Launcher.App.Diagnostics;

/// <summary>
/// 统计采样期间 UI 线程被 Dispatcher 操作占用的时长，用来区分长帧的两种成因：
/// 被 UI 线程上的工作挤掉，还是 UI 线程空闲、卡在渲染线程或 GPU 上。
/// 只在交互采样开启时挂钩，普通运行不订阅任何 Dispatcher 事件。
/// </summary>
internal sealed class DispatcherBusyProbe : IDisposable
{
    internal const string NoOperationDetail = "none";

    private static FieldInfo? operationMethodField;
    private static bool hasResolvedOperationMethodField;

    private readonly DispatcherHooks hooks;
    private int nestingDepth;
    private bool hasOutermostOperation;
    private long outermostStartedAt;
    private DispatcherOperation? outermostOperation;
    private bool isDisposed;

    private DispatcherBusyProbe(DispatcherHooks hooks)
    {
        this.hooks = hooks;
        hooks.OperationStarted += Hooks_OperationStarted;
        hooks.OperationCompleted += Hooks_OperationCompleted;
        hooks.OperationAborted += Hooks_OperationAborted;
    }

    /// <summary>当前帧窗口内，UI 线程执行 Dispatcher 操作的累计时长。</summary>
    internal double FrameBusyMs { get; private set; }

    internal int FrameOperationCount { get; private set; }

    internal double FrameLongestOperationMs { get; private set; }

    internal string FrameLongestOperationDetail { get; private set; } = NoOperationDetail;

    /// <summary>整段交互的累计值，用于交互结束时的摘要行。</summary>
    internal double TotalBusyMs { get; private set; }

    internal int TotalOperationCount { get; private set; }

    internal double WorstOperationMs { get; private set; }

    internal string WorstOperationDetail { get; private set; } = NoOperationDetail;

    internal static DispatcherBusyProbe? TryAttach(Dispatcher? dispatcher)
    {
        if (dispatcher is null)
            return null;

        try
        {
            return new DispatcherBusyProbe(dispatcher.Hooks);
        }
        catch (Exception)
        {
            // 诊断设施不得影响正常运行：挂钩失败就退化成不统计。
            return null;
        }
    }

    /// <summary>结算一个帧窗口并归零，准备统计下一帧。</summary>
    internal void ResetFrame()
    {
        FrameBusyMs = 0d;
        FrameOperationCount = 0;
        FrameLongestOperationMs = 0d;
        FrameLongestOperationDetail = NoOperationDetail;
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        hooks.OperationStarted -= Hooks_OperationStarted;
        hooks.OperationCompleted -= Hooks_OperationCompleted;
        hooks.OperationAborted -= Hooks_OperationAborted;
    }

    private void Hooks_OperationStarted(object? sender, DispatcherHookEventArgs e)
    {
        // 只计最外层操作：内层嵌套的时间已经包含在外层里，重复累加会超过帧长。
        if (nestingDepth == 0)
        {
            outermostStartedAt = Stopwatch.GetTimestamp();
            outermostOperation = e.Operation;
            hasOutermostOperation = true;
        }

        nestingDepth++;
    }

    private void Hooks_OperationCompleted(object? sender, DispatcherHookEventArgs e) => CompleteOperation();

    private void Hooks_OperationAborted(object? sender, DispatcherHookEventArgs e) => CompleteOperation();

    private void CompleteOperation()
    {
        // 在某个操作执行途中挂钩时，会先收到一个没有配对开始事件的完成事件，忽略即可。
        if (nestingDepth == 0)
            return;

        nestingDepth--;
        if (nestingDepth > 0 || !hasOutermostOperation)
            return;

        var elapsedMs = Stopwatch.GetElapsedTime(outermostStartedAt).TotalMilliseconds;
        var operation = outermostOperation;
        hasOutermostOperation = false;
        outermostOperation = null;

        FrameBusyMs += elapsedMs;
        FrameOperationCount++;
        TotalBusyMs += elapsedMs;
        TotalOperationCount++;

        // 只有刷新纪录时才解析委托名，让每个操作的常规开销保持在几次算术之内。
        if (elapsedMs <= FrameLongestOperationMs && elapsedMs <= WorstOperationMs)
            return;

        var detail = Describe(operation);
        if (elapsedMs > FrameLongestOperationMs)
        {
            FrameLongestOperationMs = elapsedMs;
            FrameLongestOperationDetail = detail;
        }

        if (elapsedMs > WorstOperationMs)
        {
            WorstOperationMs = elapsedMs;
            WorstOperationDetail = detail;
        }
    }

    private static string Describe(DispatcherOperation? operation)
    {
        if (operation is null)
            return NoOperationDetail;

        var priority = operation.Priority.ToString();
        var methodName = TryGetMethodName(operation);
        return methodName is null ? priority : $"{priority}/{methodName}";
    }

    /// <summary>
    /// 反射读取操作持有的委托，把长帧直接落到具体回调上。
    /// 这是 WPF 的私有字段，取不到时退化成只报优先级，不影响其余统计。
    /// </summary>
    private static string? TryGetMethodName(DispatcherOperation operation)
    {
        if (!hasResolvedOperationMethodField)
        {
            hasResolvedOperationMethodField = true;
            operationMethodField = typeof(DispatcherOperation).GetField(
                "_method",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (operationMethodField is null)
            return null;

        try
        {
            if (operationMethodField.GetValue(operation) is not Delegate method)
                return null;

            var targetMethod = method.Method;
            var declaringType = targetMethod.DeclaringType;
            // lambda 会被编译进闭包类，取外层类型才能看出是谁排的队。
            var ownerType = declaringType?.DeclaringType ?? declaringType;
            return $"{ownerType?.Name ?? "?"}.{targetMethod.Name}";
        }
        catch (Exception)
        {
            operationMethodField = null;
            return null;
        }
    }
}
