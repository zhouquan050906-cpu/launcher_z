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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed class MinecraftDirectoryStartupRecoveryService(
    IMinecraftDirectoryFileSystem fileSystem,
    MinecraftDirectoryManagementService managementService)
{
    private readonly IMinecraftDirectoryFileSystem liveFileSystem = fileSystem;

    /// <param name="probedFileSystem">
    /// 调用方预先探好的可用性快照（见 <see cref="MinecraftDirectoryStartupProbe"/>）。
    /// 本方法是同步的，会把已登记的目录逐个探一遍；直接用真实文件系统时，一个断连的
    /// 网络路径就能让调用挂住几十秒。启动路径必须传快照，其余场景传 null 走实时探测。
    /// </param>
    public MinecraftDirectoryStartupRecoveryResult? Recover(
        LauncherSettings settings,
        string defaultDirectory,
        IMinecraftDirectoryFileSystem? probedFileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fileSystem = probedFileSystem ?? liveFileSystem;
        var normalizedDefaultDirectory = MinecraftDirectoryPath.Normalize(defaultDirectory);
        var invalidDirectory = MinecraftDirectoryPath.Normalize(settings.MinecraftDirectory);
        if (fileSystem.DirectoryIsAccessible(invalidDirectory))
            return null;

        managementService.EnsureCurrentDirectoryRegistered(settings);
        invalidDirectory = settings.MinecraftDirectory;

        // 设计如此：首次运行由 MinecraftDirectoryStartupInitializationService 直接选定启动器默认目录，
        // 而这里是"当前目录已失效"的恢复路径，按列表顺序依次尝试（可能落到自动发现的官方目录），
        // 优先给用户一个立刻能用的目录。两条路径结果不同是刻意的，不要改成默认目录优先。
        foreach (var directory in settings.MinecraftDirectories)
        {
            if (MinecraftDirectoryPath.Equals(directory, invalidDirectory)
                || !fileSystem.DirectoryIsAccessible(directory))
            {
                continue;
            }

            managementService.SelectDirectory(settings, directory);
            return new MinecraftDirectoryStartupRecoveryResult(
                invalidDirectory,
                settings.MinecraftDirectory,
                UsedDefaultDirectory: false,
                CreatedDefaultDirectory: false);
        }

        var defaultDirectoryExisted = fileSystem.DirectoryExists(normalizedDefaultDirectory);
        var ensuredDefaultDirectory = MinecraftDirectoryStartupPreparation.EnsureAccessibleDirectory(
            fileSystem,
            normalizedDefaultDirectory,
            "default");

        managementService.AddAndSelectDirectory(settings, ensuredDefaultDirectory);

        // 补建出用户原本就选中的目录不算"恢复"：前后是同一个目录，提示切换只会让人困惑。
        // 设置仍由调用方落盘，这里只是不上报需要向用户展示的恢复结果。
        if (MinecraftDirectoryPath.Equals(invalidDirectory, settings.MinecraftDirectory))
            return null;

        return new MinecraftDirectoryStartupRecoveryResult(
            invalidDirectory,
            settings.MinecraftDirectory,
            UsedDefaultDirectory: true,
            CreatedDefaultDirectory: !defaultDirectoryExisted);
    }
}
