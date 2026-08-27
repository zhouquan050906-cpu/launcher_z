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
    public IReadOnlyList<MinecraftDirectoryDiscovery> DiscoverExistingDirectories()
    {
        var discovered = new List<MinecraftDirectoryDiscovery>(2);
        var knownDirectories = new HashSet<string>(MinecraftDirectoryPath.Comparer);
        AddIfExisting(
            pathProvider.OfficialMinecraftDirectory,
            MinecraftDirectoryKind.Official,
            discovered,
            knownDirectories);
        AddIfExisting(
            pathProvider.DefaultMinecraftDirectory,
            MinecraftDirectoryKind.LauncherDefault,
            discovered,
            knownDirectories);
        return discovered;
    }

    private void AddIfExisting(
        string candidate,
        MinecraftDirectoryKind kind,
        ICollection<MinecraftDirectoryDiscovery> discovered,
        ISet<string> knownDirectories)
    {
        var normalizedCandidate = MinecraftDirectoryPath.Normalize(candidate);
        if (fileSystem.DirectoryExists(normalizedCandidate) && knownDirectories.Add(normalizedCandidate))
            discovered.Add(new MinecraftDirectoryDiscovery(normalizedCandidate, kind));
    }
}
