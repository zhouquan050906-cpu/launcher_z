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
/// 首次运行初始化与失效恢复都要"建出目录并确认真的能用"。两者对失败的处理必须一致，
/// 否则其中一条路径会因为异常没被归类而绕过启动器的目录错误提示，直接静默退出。
/// </summary>
internal static class MinecraftDirectoryStartupPreparation
{
    /// <param name="directoryDescription">
    /// 用于异常消息，区分是哪条启动路径失败（诊断用，不面向用户）。
    /// </param>
    public static string EnsureAccessibleDirectory(
        IMinecraftDirectoryFileSystem fileSystem,
        string normalizedDirectory,
        string directoryDescription)
    {
        string ensuredDirectory;
        try
        {
            ensuredDirectory = fileSystem.EnsureDirectoryExists(normalizedDirectory);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                normalizedDirectory,
                $"The {directoryDescription} Minecraft directory could not be created.",
                exception);
        }

        // 创建成功不等于可用：目录可能落在只读卷上，或被其他进程独占。
        if (!fileSystem.DirectoryIsAccessible(ensuredDirectory))
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                normalizedDirectory,
                $"The {directoryDescription} Minecraft directory is not accessible after creation.");
        }

        return ensuredDirectory;
    }
}
