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

using System.Windows.Threading;

namespace Launcher.App.Services;

/// <summary>
/// 过渡动画期间的全局闸门。动画只有 240ms，但它独占 UI 线程的出帧节奏，
/// 任何在这段时间里插进来的工作都会直接变成掉帧——实测一次列表物化就占掉 258ms。
/// 可延后的工作统一走 <see cref="RunWhenIdle"/>，动画结束后再执行。
/// </summary>
internal static class UiTransitionGate
{
    // 带截止时间：推迟只是让位给动画，绝不能让工作永远排不出去。
    private static readonly List<DeferredAction> DeferredActions = [];
    private static int activeTransitionCount;
    // 显式持有 UI 线程 Dispatcher：闸门可能被工作线程调用，而 Application.Current
    // 在单元测试里不存在，依赖它会让闸门在测试中静默退化成"立即执行"。
    private static Dispatcher? uiDispatcher;

    /// <summary>由过渡服务在构造时登记 UI 线程 Dispatcher。</summary>
    internal static void AttachDispatcher(Dispatcher dispatcher)
    {
        uiDispatcher ??= dispatcher;
    }

    private static Dispatcher? ResolveDispatcher() =>
        uiDispatcher ?? global::System.Windows.Application.Current?.Dispatcher;

    /// <summary>
    /// 推迟的上限。无论是等待方还是排队的工作，超过它一律放行：
    /// 闸门若因异常路径没能配对 Exit、或用户连续切页不停，工作都不能永远排不出去。
    /// </summary>
    internal static readonly TimeSpan MaximumDeferral = TimeSpan.FromSeconds(2);

    /// <summary>是否有过渡正在进行。主页面切换与页内分区切换可能重叠，因此按计数判断。</summary>
    internal static bool IsTransitionActive => activeTransitionCount > 0;

    internal static int PendingCount => DeferredActions.Count;

    internal static void Enter()
    {
        activeTransitionCount++;
    }

    internal static void Exit()
    {
        if (activeTransitionCount == 0)
            return;

        activeTransitionCount--;
        if (activeTransitionCount > 0)
            return;

        DrainDeferredActions();
    }

    /// <summary>
    /// 过渡进行中则推迟到动画结束后执行，否则按 Background 优先级照常排队。
    /// 不阻塞调用方，因此可以从工作线程调用。
    /// </summary>
    internal static void RunWhenIdle(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = ResolveDispatcher();
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            // 队列本身不是线程安全的，先回到 UI 线程再判断，避免与 Exit 竞争。
            dispatcher.BeginInvoke(() => RunWhenIdle(action), DispatcherPriority.Background);
            return;
        }

        var deferred = new DeferredAction(action, DateTime.UtcNow + MaximumDeferral);
        if (!IsTransitionActive)
        {
            // 排队只是"稍后执行"，轮到它时过渡可能已经开始——页面 Visibility 绑定就先于
            // MoveTo 触发，此刻判定为空闲，等真正执行时动画已在播。因此执行前再判一次。
            dispatcher.BeginInvoke(
                () =>
                {
                    if (IsTransitionActive && DateTime.UtcNow < deferred.Deadline)
                        DeferredActions.Add(deferred);
                    else
                        deferred.Action();
                },
                DispatcherPriority.Background);
            return;
        }

        DeferredActions.Add(deferred);
    }

    /// <summary>
    /// 等到没有过渡在进行。供有 await 契约的加载路径使用：调用方 await 它之后再落地数据，
    /// 任务完成的时机仍然表示"数据已就绪"，不会因为推迟而破坏契约。
    /// </summary>
    internal static async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + MaximumDeferral;
        while (IsTransitionActive)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return;

            var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RunWhenIdle(() => released.TrySetResult());
            var completed = await Task.WhenAny(
                released.Task,
                Task.Delay(remaining, cancellationToken)).ConfigureAwait(true);
            if (!ReferenceEquals(completed, released.Task))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            // 放行只是排队，续体真正执行时下一次过渡可能已经开始——
            // 实测正是这样让 92ms 的视口初始化又落回了动画里。因此回到循环重新判断。
        }
    }

    /// <summary>测试用：把闸门恢复到初始状态，并指定要使用的 Dispatcher。</summary>
    internal static void ResetForTesting(Dispatcher? dispatcher = null)
    {
        activeTransitionCount = 0;
        DeferredActions.Clear();
        uiDispatcher = dispatcher;
    }

    private static void DrainDeferredActions()
    {
        if (DeferredActions.Count == 0)
            return;

        var pending = DeferredActions.ToArray();
        DeferredActions.Clear();
        var dispatcher = ResolveDispatcher();
        foreach (var deferred in pending)
        {
            if (dispatcher is null)
            {
                deferred.Action();
                continue;
            }

            // 放行只是排队，真正执行要等到 Background 轮到它。用户连续快切时，
            // 下一次过渡往往已经开始——实测放出来的活直接砸进下一段动画。
            // 因此执行前再判一次，还在过渡中就重新推迟；但超过截止时间必须放行，
            // 否则连续切页会把工作无限期饿死，那比掉几帧严重得多。
            dispatcher.BeginInvoke(
                () =>
                {
                    if (IsTransitionActive && DateTime.UtcNow < deferred.Deadline)
                        DeferredActions.Add(deferred);
                    else
                        deferred.Action();
                },
                DispatcherPriority.Background);
        }
    }

    private readonly record struct DeferredAction(Action Action, DateTime Deadline);
}
