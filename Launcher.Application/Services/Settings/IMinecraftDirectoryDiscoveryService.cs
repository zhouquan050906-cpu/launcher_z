/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public interface IMinecraftDirectoryDiscoveryService
{
    /// <summary>
    /// 探测已知位置上真实存在的 Minecraft 目录。
    /// </summary>
    /// <remarks>
    /// 异步是必须的：官方目录位于 %APPDATA% 下，漫游配置文件会把它重定向到网络路径，
    /// 断连时单次 <c>Directory.Exists</c> 就能挂住几十秒。这一步发生在主窗口出现之前，
    /// 同步实现会让用户面对一个迟迟不出现的启动器。
    /// </remarks>
    Task<IReadOnlyList<MinecraftDirectoryDiscovery>> DiscoverExistingDirectoriesAsync(
        CancellationToken cancellationToken = default);
}
