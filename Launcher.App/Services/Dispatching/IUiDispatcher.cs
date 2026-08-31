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

namespace Launcher.App.Services;

public interface IUiDispatcher
{
    bool HasAccess { get; }

    void Post(Action action);

    /// <summary>
    /// 回到 UI 线程执行，但先让位给正在播放的页面切换动画。
    /// 只用于"晚几十毫秒无所谓、却足以压掉整段动画"的批量界面更新，
    /// 例如把一批列表项装进虚拟化列表。需要立即生效的更新应当使用 <see cref="Post"/>。
    /// </summary>
    void PostAfterTransition(Action action);

    /// <summary>
    /// 与 <see cref="PostAfterTransition"/> 相同，但返回的任务在 action 真正执行完成后才结束。
    /// </summary>
    /// <remarks>
    /// 供有 await 契约的加载路径使用：调用方 await 它之后，界面状态才算真的落地了。
    /// 只排队不等待会让 <c>await LoadAsync()</c> 提前返回，任务完成不再代表列表已更新。
    /// </remarks>
    Task PostAfterTransitionAsync(Action action);

    void Invoke(Action action);

    Task InvokeAsync(Func<Task> action);
}
