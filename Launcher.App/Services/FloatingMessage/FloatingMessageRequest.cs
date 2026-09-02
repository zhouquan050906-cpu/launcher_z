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

/// <summary>
/// 浮层消息请求。<see cref="AutoHide"/> 为 false 时消息会一直显示到显式清除，
/// 拖放提示需要这种语义：提示必须覆盖整个拖动过程，而不是几秒后自行消失。
/// </summary>
/// <param name="Message">要显示的文本；空字符串表示隐藏当前浮层。</param>
/// <param name="AutoHide">是否在固定时长后自动隐藏。</param>
public readonly record struct FloatingMessageRequest(string Message, bool AutoHide = true);
