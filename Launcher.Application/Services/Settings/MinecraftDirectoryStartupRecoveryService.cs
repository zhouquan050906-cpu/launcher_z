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
    public MinecraftDirectoryStartupRecoveryResult? Recover(
        LauncherSettings settings,
        string defaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDefaultDirectory = MinecraftDirectoryPath.Normalize(defaultDirectory);
        var invalidDirectory = MinecraftDirectoryPath.Normalize(settings.MinecraftDirectory);
        if (fileSystem.DirectoryIsAccessible(invalidDirectory))
            return null;

        managementService.EnsureCurrentDirectoryRegistered(settings);
        invalidDirectory = settings.MinecraftDirectory;

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
        string ensuredDefaultDirectory;
        try
        {
            ensuredDefaultDirectory = fileSystem.EnsureDirectoryExists(normalizedDefaultDirectory);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                normalizedDefaultDirectory,
                "The default Minecraft directory could not be created.",
                exception);
        }

        if (!fileSystem.DirectoryIsAccessible(ensuredDefaultDirectory))
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                normalizedDefaultDirectory,
                "The default Minecraft directory is not accessible after creation.");
        }

        managementService.AddAndSelectDirectory(settings, ensuredDefaultDirectory);
        return new MinecraftDirectoryStartupRecoveryResult(
            invalidDirectory,
            settings.MinecraftDirectory,
            UsedDefaultDirectory: true,
            CreatedDefaultDirectory: !defaultDirectoryExisted);
    }
}
