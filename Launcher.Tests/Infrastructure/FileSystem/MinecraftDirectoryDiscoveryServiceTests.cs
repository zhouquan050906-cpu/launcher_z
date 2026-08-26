/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class MinecraftDirectoryDiscoveryServiceTests : TestTempDirectory
{
    [Fact]
    public void DiscoveryOnlyReturnsExistingOfficialAndLauncherMinecraftDirectories()
    {
        var launcherDirectory = Path.Combine(TempRoot, "launcher");
        var roamingDirectory = Path.Combine(TempRoot, "roaming");
        var launcherMinecraftDirectory = Path.Combine(launcherDirectory, ".minecraft");
        var officialMinecraftDirectory = Path.Combine(roamingDirectory, ".minecraft");
        Directory.CreateDirectory(launcherMinecraftDirectory);
        Directory.CreateDirectory(officialMinecraftDirectory);
        Directory.CreateDirectory(Path.Combine(launcherDirectory, "other", ".minecraft"));
        Directory.CreateDirectory(Path.Combine(launcherDirectory, "versions"));
        var service = new MinecraftDirectoryDiscoveryService(
            new LauncherPathProvider(launcherDirectory, roamingDirectory),
            new MinecraftDirectoryFileSystem());

        var result = service.DiscoverExistingDirectories();

        Assert.Equal(
            [
                MinecraftDirectoryPath.Normalize(officialMinecraftDirectory),
                MinecraftDirectoryPath.Normalize(launcherMinecraftDirectory)
            ],
            result);
    }

    [Fact]
    public void ExistingEmptyDirectoryIsDiscoveredWithoutVersionsFolder()
    {
        var launcherDirectory = Path.Combine(TempRoot, "launcher");
        var roamingDirectory = Path.Combine(TempRoot, "roaming");
        var launcherMinecraftDirectory = Path.Combine(launcherDirectory, ".minecraft");
        Directory.CreateDirectory(launcherMinecraftDirectory);
        var service = new MinecraftDirectoryDiscoveryService(
            new LauncherPathProvider(launcherDirectory, roamingDirectory),
            new MinecraftDirectoryFileSystem());

        var result = service.DiscoverExistingDirectories();

        Assert.Equal(MinecraftDirectoryPath.Normalize(launcherMinecraftDirectory), Assert.Single(result));
    }

    [Fact]
    public void FileSystemCreatesAnAccessibleEmptyDirectory()
    {
        var directory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new MinecraftDirectoryFileSystem();

        var result = fileSystem.EnsureDirectoryExists(directory);

        Assert.Equal(MinecraftDirectoryPath.Normalize(directory), result);
        Assert.True(fileSystem.DirectoryExists(directory));
        Assert.True(fileSystem.DirectoryIsAccessible(directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }
}
