/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;

namespace Launcher.Tests.Helpers;

/// <summary>
/// 构造自动发现结果的简写，避免每个测试都写全 <see cref="MinecraftDirectoryDiscovery"/> 构造。
/// </summary>
public static class TestMinecraftDirectoryDiscoveries
{
    public static MinecraftDirectoryDiscovery Official(string directoryPath) =>
        new(directoryPath, MinecraftDirectoryKind.Official);

    public static MinecraftDirectoryDiscovery LauncherDefault(string directoryPath) =>
        new(directoryPath, MinecraftDirectoryKind.LauncherDefault);
}
