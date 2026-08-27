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

public sealed class MinecraftDirectoryStartupInitializationService(
    IMinecraftDirectoryFileSystem fileSystem,
    MinecraftDirectoryManagementService managementService)
{
    public string InitializeDefaultDirectory(
        LauncherSettings settings,
        string defaultDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDefaultDirectory = MinecraftDirectoryPath.Normalize(defaultDirectory);
        var ensuredDefaultDirectory = MinecraftDirectoryStartupPreparation.EnsureAccessibleDirectory(
            fileSystem,
            normalizedDefaultDirectory,
            "initial");

        managementService.AddAndSelectDirectory(settings, ensuredDefaultDirectory);
        return settings.MinecraftDirectory;
    }
}
