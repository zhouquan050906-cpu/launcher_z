/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;

namespace Launcher.Tests.Application.Settings;

/// <summary>
/// 探测上限直接决定"失效目录会不会把启动拖住"，因此覆盖：卡住的探测会超时放行、
/// 一个失效目录不拖累其余目录、快照在补建目录后不会给出过期答案。
/// </summary>
public sealed class MinecraftDirectoryStartupProbeTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task ReportsHungProbesAsUnavailableInsteadOfWaitingForThem()
    {
        // 断连的网络路径就是这个形状：调用不返回，也没法取消。
        using var release = new ManualResetEventSlim(false);
        var fileSystem = new FakeMinecraftDirectoryFileSystem(
            accessibleDirectories: [@"\\dead-host\mc"],
            blockedDirectories: [@"\\dead-host\mc"],
            release);

        var isAccessible = await MinecraftDirectoryStartupProbe.IsAccessibleAsync(
            fileSystem,
            @"\\dead-host\mc",
            ProbeTimeout);

        Assert.False(isAccessible);
        release.Set();
    }

    [Fact]
    public async Task ProbesDirectoriesInParallelSoOneDeadPathDoesNotHideTheOthers()
    {
        using var release = new ManualResetEventSlim(false);
        var fileSystem = new FakeMinecraftDirectoryFileSystem(
            accessibleDirectories: [@"C:\mc", @"D:\mc"],
            blockedDirectories: [@"\\dead-host\mc"],
            release);

        var availability = await MinecraftDirectoryStartupProbe.ProbeAsync(
            fileSystem,
            [@"\\dead-host\mc", @"C:\mc", @"D:\mc"],
            ProbeTimeout);

        Assert.False(availability.DirectoryIsAccessible(@"\\dead-host\mc"));
        Assert.True(availability.DirectoryIsAccessible(@"C:\mc"));
        Assert.True(availability.DirectoryIsAccessible(@"D:\mc"));
        release.Set();
    }

    [Fact]
    public async Task SnapshotFallsBackToTheFileSystemForDirectoriesItNeverProbed()
    {
        var fileSystem = new FakeMinecraftDirectoryFileSystem(accessibleDirectories: [@"C:\mc", @"D:\mc"]);

        var availability = await MinecraftDirectoryStartupProbe.ProbeAsync(fileSystem, [@"C:\mc"], ProbeTimeout);

        Assert.True(availability.DirectoryIsAccessible(@"D:\mc"));
    }

    [Fact]
    public async Task SnapshotStopsAnsweringForADirectoryOnceItHasBeenRecreated()
    {
        // 探测时默认目录还不存在，恢复流程随后把它补建出来。若继续读快照，
        // 刚建好的目录会被判成不可用，启动会误判为"恢复失败"并直接退出。
        var fileSystem = new FakeMinecraftDirectoryFileSystem(accessibleDirectories: []);
        var availability = await MinecraftDirectoryStartupProbe.ProbeAsync(
            fileSystem,
            [@"C:\mc"],
            ProbeTimeout);
        Assert.False(availability.DirectoryIsAccessible(@"C:\mc"));

        availability.EnsureDirectoryExists(@"C:\mc");

        Assert.True(availability.DirectoryIsAccessible(@"C:\mc"));
    }

    private sealed class FakeMinecraftDirectoryFileSystem(
        IEnumerable<string> accessibleDirectories,
        IEnumerable<string>? blockedDirectories = null,
        ManualResetEventSlim? release = null) : IMinecraftDirectoryFileSystem
    {
        private readonly HashSet<string> accessibleDirectories = new(
            accessibleDirectories.Select(MinecraftDirectoryPath.Normalize),
            MinecraftDirectoryPath.Comparer);

        private readonly HashSet<string> blockedDirectories = new(
            (blockedDirectories ?? []).Select(MinecraftDirectoryPath.Normalize),
            MinecraftDirectoryPath.Comparer);

        public bool DirectoryExists(string directoryPath) => IsAccessible(directoryPath);

        public bool DirectoryIsAccessible(string directoryPath) => IsAccessible(directoryPath);

        public string EnsureDirectoryExists(string directoryPath)
        {
            var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
            accessibleDirectories.Add(normalizedDirectory);
            return normalizedDirectory;
        }

        private bool IsAccessible(string directoryPath)
        {
            var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
            if (blockedDirectories.Contains(normalizedDirectory))
            {
                // 测试结束前调用方会放行；这里的上限只是兜底，避免测试挂死。
                release?.Wait(TimeSpan.FromSeconds(30));
            }

            return accessibleDirectories.Contains(normalizedDirectory);
        }
    }
}
