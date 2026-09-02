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

public interface IFloatingMessageService
{
    event Action<FloatingMessageRequest>? MessageRequested;

    /// <summary>显示一条会在固定时长后自动消失的浮层提示。</summary>
    void Show(string message);

    /// <summary>
    /// 显示拖放提示。提示不会自动消失，必须显式清除；重复传入同一条消息不会重新触发动画，
    /// 因此调用方无需自行去重。<paramref name="source"/> 标识提示的归属：
    /// 同一次拖动会依次经过多个拖放处理器，只有持有者才能清除自己的提示。
    /// </summary>
    void ShowDragHint(object source, string message);

    /// <summary>清除 <paramref name="source"/> 自己的拖放提示；当前提示属于别的来源时不做任何事。</summary>
    void ClearDragHint(object source);

    /// <summary>无条件清除拖放提示，用于拖放生命周期结束时收尾。</summary>
    void ClearDragHint();
}
