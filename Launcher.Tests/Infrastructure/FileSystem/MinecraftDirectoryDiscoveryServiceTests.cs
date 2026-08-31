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
    public async Task DiscoveryOnlyReturnsExistingOfficialAndLauncherMinecraftDirectories()
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

        var result = await service.DiscoverExistingDirectoriesAsync();

        // 官方目录排在前面，且两者的来源被分别标注，登记时才能给出不同的默认显示名。
        Assert.Equal(
            [
                Official(MinecraftDirectoryPath.Normalize(officialMinecraftDirectory)),
                LauncherDefault(MinecraftDirectoryPath.Normalize(launcherMinecraftDirectory))
            ],
            result);
    }

    [Fact]
    public async Task ExistingEmptyDirectoryIsDiscoveredWithoutVersionsFolder()
    {
        var launcherDirectory = Path.Combine(TempRoot, "launcher");
        var roamingDirectory = Path.Combine(TempRoot, "roaming");
        var launcherMinecraftDirectory = Path.Combine(launcherDirectory, ".minecraft");
        Directory.CreateDirectory(launcherMinecraftDirectory);
        var service = new MinecraftDirectoryDiscoveryService(
            new LauncherPathProvider(launcherDirectory, roamingDirectory),
            new MinecraftDirectoryFileSystem());

        var result = await service.DiscoverExistingDirectoriesAsync();

        Assert.Equal(
            LauncherDefault(MinecraftDirectoryPath.Normalize(launcherMinecraftDirectory)),
            Assert.Single(result));
    }

    /// <summary>
    /// 官方目录在 %APPDATA% 下，漫游配置文件会把它重定向到网络路径。断连时这次探测会挂住，
    /// 而它发生在主窗口出现之前——必须有上限，而且不能连累另一个候选目录。
    /// </summary>
    [Fact]
    public async Task HangingOfficialDirectoryProbeDoesNotStallDiscovery()
    {
        var launcherDirectory = Path.Combine(TempRoot, "launcher");
        var roamingDirectory = Path.Combine(TempRoot, "roaming");
        var pathProvider = new LauncherPathProvider(launcherDirectory, roamingDirectory);
        using var release = new ManualResetEventSlim(false);
        var fileSystem = new HangingMinecraftDirectoryFileSystem(
            MinecraftDirectoryPath.Normalize(pathProvider.OfficialMinecraftDirectory),
            release);
        var service = new MinecraftDirectoryDiscoveryService(
            pathProvider,
            fileSystem,
            probeTimeout: TimeSpan.FromMilliseconds(150));

        var result = await service.DiscoverExistingDirectoriesAsync();

        // 卡住的官方目录按"不存在"处理，自带目录照常被发现。
        Assert.Equal(
            LauncherDefault(MinecraftDirectoryPath.Normalize(pathProvider.DefaultMinecraftDirectory)),
            Assert.Single(result));
        release.Set();
    }

    private sealed class HangingMinecraftDirectoryFileSystem(
        string hangingDirectory,
        ManualResetEventSlim release) : IMinecraftDirectoryFileSystem
    {
        public bool DirectoryExists(string directoryPath)
        {
            if (MinecraftDirectoryPath.Equals(directoryPath, hangingDirectory))
            {
                // 测试结束前调用方会放行；这里的上限只是兜底，避免测试挂死。
                release.Wait(TimeSpan.FromSeconds(30));
                return true;
            }

            return true;
        }

        public bool DirectoryIsAccessible(string directoryPath) => DirectoryExists(directoryPath);

        public string EnsureDirectoryExists(string directoryPath) =>
            MinecraftDirectoryPath.Normalize(directoryPath);
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
