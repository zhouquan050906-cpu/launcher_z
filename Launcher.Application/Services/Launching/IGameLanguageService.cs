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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IGameLanguageService
{
    /// <summary>
    /// 把启动器语言同步到实例的游戏设置里，返回实际写入的 Minecraft 语言代码。
    /// </summary>
    /// <remarks>
    /// 语言代码的写法随游戏版本变化，返回值便于调用方记录实际生效的值。
    /// </remarks>
    Task<string> ApplyLauncherLanguageAsync(
        GameInstance instance,
        string launcherLanguage,
        CancellationToken cancellationToken = default);
}
