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

/// <summary>
/// 自动发现到的 Minecraft 目录的来源，用于决定登记时使用的默认显示名。
/// </summary>
public enum MinecraftDirectoryKind
{
    /// <summary>启动器自身根目录下的 .minecraft。</summary>
    LauncherDefault,

    /// <summary>官方 Minecraft 启动器使用的 %APPDATA%\.minecraft。</summary>
    Official
}

public sealed record MinecraftDirectoryDiscovery(
    string DirectoryPath,
    MinecraftDirectoryKind Kind);
