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

namespace Launcher.App.ViewModels.GameSettings;

internal readonly record struct InstanceCatalogEntrySnapshot(
    string Id,
    string Name,
    string MinecraftVersion,
    string VersionName,
    string VersionType,
    LoaderKind Loader,
    string? LoaderVersion,
    string? IconSource,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string InstanceDirectory)
{
    public static InstanceCatalogEntrySnapshot Create(GameInstance instance)
    {
        return new InstanceCatalogEntrySnapshot(
            instance.Id,
            instance.Name,
            instance.MinecraftVersion,
            instance.VersionName,
            instance.VersionType,
            instance.Loader,
            instance.LoaderVersion,
            instance.IconSource,
            instance.CreatedAt,
            instance.UpdatedAt,
            instance.InstanceDirectory);
    }
}

internal static class GameInstanceStateCopier
{
    public static void Copy(GameInstance source, GameInstance destination)
    {
        destination.Id = source.Id;
        destination.Name = source.Name;
        destination.MinecraftVersion = source.MinecraftVersion;
        destination.Loader = source.Loader;
        destination.LoaderVersion = source.LoaderVersion;
        destination.VersionName = source.VersionName;
        destination.VersionType = source.VersionType;
        destination.Description = source.Description;
        destination.IconSource = source.IconSource;
        destination.InstanceDirectory = source.InstanceDirectory;
        destination.BackupDirectory = source.BackupDirectory;
        destination.MemorySettingsMode = source.MemorySettingsMode;
        destination.MemoryMb = source.MemoryMb;
        destination.WindowWidth = source.WindowWidth;
        destination.WindowHeight = source.WindowHeight;
        destination.PreLaunchCommand = source.PreLaunchCommand;
        destination.WaitForPreLaunchCommand = source.WaitForPreLaunchCommand;
        destination.PostExitCommand = source.PostExitCommand;
        destination.JvmArguments = source.JvmArguments;
        destination.GameArguments = source.GameArguments;
        destination.LaunchSettingsMode = source.LaunchSettingsMode;
        destination.JavaSettingsMode = source.JavaSettingsMode;
        destination.JavaSelectionMode = source.JavaSelectionMode;
        destination.SelectedJavaExecutablePath = source.SelectedJavaExecutablePath;
        destination.CheckFilesBeforeLaunch = source.CheckFilesBeforeLaunch;
        destination.AutoRepairMissingFiles = source.AutoRepairMissingFiles;
        destination.MinimizeLauncherAfterLaunch = source.MinimizeLauncherAfterLaunch;
        destination.LaunchFullScreen = source.LaunchFullScreen;
        destination.AutoJoinServerAddress = source.AutoJoinServerAddress;
        destination.CreatedAt = source.CreatedAt;
        destination.UpdatedAt = source.UpdatedAt;
    }
}
