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

using Launcher.Application.Services;

namespace Launcher.Infrastructure.FileSystem;

public sealed class MinecraftDirectoryDiscoveryService(
    LauncherPathProvider pathProvider,
    IMinecraftDirectoryFileSystem fileSystem)
    : IMinecraftDirectoryDiscoveryService
{
    public IReadOnlyList<string> DiscoverExistingDirectories()
    {
        var discovered = new List<string>(2);
        var knownDirectories = new HashSet<string>(MinecraftDirectoryPath.Comparer);
        AddIfExisting(pathProvider.OfficialMinecraftDirectory, discovered, knownDirectories);
        AddIfExisting(pathProvider.DefaultMinecraftDirectory, discovered, knownDirectories);
        return discovered;
    }

    private void AddIfExisting(
        string candidate,
        ICollection<string> discovered,
        ISet<string> knownDirectories)
    {
        var normalizedCandidate = MinecraftDirectoryPath.Normalize(candidate);
        if (fileSystem.DirectoryExists(normalizedCandidate) && knownDirectories.Add(normalizedCandidate))
            discovered.Add(normalizedCandidate);
    }
}
