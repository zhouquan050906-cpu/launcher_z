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

public sealed class FloatingMessageService : IFloatingMessageService
{
    // Show 会被后台线程调用（MainViewModel 正因此才在渲染前切回 UI 线程），
    // 而下面这两个字段是共享状态，所以判定必须在锁内完成；事件一律在锁外触发，避免订阅方回调时死锁。
    private readonly object gate = new();
    // 拖放提示的去重集中在这里：因为提示不会自动消失，"消息相同"就等价于"已经显示着"，
    // 调用方各自缓存上一条消息会在浮层超时消失后失效，反而让提示再也不出现。
    private string currentDragHint = string.Empty;
    // 一次拖动会依次流过多个拖放处理器，未命中的处理器同样会调用清除。
    // 记录归属可以让它们只清自己的提示，否则"清掉再显示"会让进场动画在 DragOver 上反复重播。
    private object? dragHintSource;

    public event Action<FloatingMessageRequest>? MessageRequested;

    public void Show(string message)
    {
        lock (gate)
        {
            // 普通提示会顶掉拖放提示，因此同步作废去重状态，
            // 避免之后同一条拖放提示被误判为仍在显示。
            currentDragHint = string.Empty;
            dragHintSource = null;
        }

        MessageRequested?.Invoke(new FloatingMessageRequest(message));
    }

    public void ShowDragHint(object source, string message)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(message))
        {
            ClearDragHint(source);
            return;
        }

        lock (gate)
        {
            if (ReferenceEquals(dragHintSource, source)
                && string.Equals(currentDragHint, message, StringComparison.Ordinal))
            {
                return;
            }

            currentDragHint = message;
            dragHintSource = source;
        }

        MessageRequested?.Invoke(new FloatingMessageRequest(message, AutoHide: false));
    }

    public void ClearDragHint(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ClearDragHintCore(source);
    }

    public void ClearDragHint() => ClearDragHintCore(owner: null);

    private void ClearDragHintCore(object? owner)
    {
        lock (gate)
        {
            if (currentDragHint.Length == 0)
                return;
            // owner 为 null 表示拖放已结束，无论提示归属于谁都要清掉。
            if (owner is not null && !ReferenceEquals(dragHintSource, owner))
                return;

            currentDragHint = string.Empty;
            dragHintSource = null;
        }

        MessageRequested?.Invoke(new FloatingMessageRequest(string.Empty));
    }
}
