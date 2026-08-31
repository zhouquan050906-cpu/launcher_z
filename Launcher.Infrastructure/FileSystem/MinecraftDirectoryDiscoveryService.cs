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
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.FileSystem;

public sealed class MinecraftDirectoryDiscoveryService(
    LauncherPathProvider pathProvider,
    IMinecraftDirectoryFileSystem fileSystem,
    ILogger<MinecraftDirectoryDiscoveryService>? logger = null,
    TimeSpan? probeTimeout = null)
    : IMinecraftDirectoryDiscoveryService
{
    public async Task<IReadOnlyList<MinecraftDirectoryDiscovery>> DiscoverExistingDirectoriesAsync(
        CancellationToken cancellationToken = default)
    {
        // 两个候选目录并行探测，且各自带上限：总耗时取决于最慢的一个，
        // 而不是"官方目录卡住几十秒 + 自带目录几毫秒"的累加。
        var candidates = new[]
        {
            (Path: pathProvider.OfficialMinecraftDirectory, Kind: MinecraftDirectoryKind.Official),
            (Path: pathProvider.DefaultMinecraftDirectory, Kind: MinecraftDirectoryKind.LauncherDefault)
        };
        var probes = await Task.WhenAll(candidates.Select(async candidate =>
        {
            var normalizedCandidate = MinecraftDirectoryPath.Normalize(candidate.Path);
            var exists = await MinecraftDirectoryStartupProbe.ExistsAsync(
                    fileSystem,
                    normalizedCandidate,
                    probeTimeout,
                    logger,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return (DirectoryPath: normalizedCandidate, candidate.Kind, Exists: exists);
        })).ConfigureAwait(false);

        var discovered = new List<MinecraftDirectoryDiscovery>(probes.Length);
        var knownDirectories = new HashSet<string>(MinecraftDirectoryPath.Comparer);
        foreach (var probe in probes)
        {
            // 顺序按候选顺序固定：官方目录在前，登记时的默认显示名依赖这个顺序。
            if (probe.Exists && knownDirectories.Add(probe.DirectoryPath))
                discovered.Add(new MinecraftDirectoryDiscovery(probe.DirectoryPath, probe.Kind));
        }

        return discovered;
    }
}
