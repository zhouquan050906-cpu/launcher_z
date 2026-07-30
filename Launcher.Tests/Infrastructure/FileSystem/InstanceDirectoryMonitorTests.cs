/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.Concurrent;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class InstanceDirectoryMonitorTests : TestTempDirectory
{
    [Theory]
    [InlineData("mods", InstanceDirectoryKind.Mods)]
    [InlineData("shaderpacks", InstanceDirectoryKind.ShaderPacks)]
    public void WatchDoesNotCreateMissingContentDirectory(string directoryName, InstanceDirectoryKind kind)
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var monitor = new InstanceDirectoryMonitor();

        using var watch = monitor.Watch(CreateInstance(instanceDirectory), kind);

        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, directoryName)));
    }

    [Fact]
    public async Task WatchObservesTargetDirectoryCreatedLaterAndIgnoresOutsideChanges()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var monitor = new InstanceDirectoryMonitor();
        using var watch = monitor.Watch(CreateInstance(instanceDirectory), InstanceDirectoryKind.Mods);
        var changes = new ConcurrentQueue<InstanceDirectoryChangedEventArgs>();
        var targetCreated = new TaskCompletionSource<InstanceDirectoryChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var outsidePath = Path.Combine(instanceDirectory, "outside.txt");
        var modsDirectory = Path.Combine(instanceDirectory, "mods");
        watch.Changed += (_, args) =>
        {
            changes.Enqueue(args);
            if (string.Equals(args.FullPath, modsDirectory, StringComparison.OrdinalIgnoreCase))
                targetCreated.TrySetResult(args);
        };

        await File.WriteAllTextAsync(outsidePath, "outside");
        Directory.CreateDirectory(modsDirectory);

        var change = await targetCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(modsDirectory, change.FullPath, ignoreCase: true);
        Assert.DoesNotContain(
            changes,
            item => string.Equals(item.FullPath, outsidePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SavesWatchIgnoresWorldDataAndObservesWorldIcon()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instance")).FullName;
        var saveDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves", "world", "region")).FullName;
        var monitor = new InstanceDirectoryMonitor();
        using var watch = monitor.Watch(CreateInstance(instanceDirectory), InstanceDirectoryKind.Saves);
        var regionPath = Path.Combine(saveDirectory, "r.0.0.mca");
        var iconPath = Path.Combine(instanceDirectory, "saves", "world", "icon.png");
        var changes = new ConcurrentQueue<InstanceDirectoryChangedEventArgs>();
        var changed = new TaskCompletionSource<InstanceDirectoryChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        watch.Changed += (_, args) =>
        {
            changes.Enqueue(args);
            if (string.Equals(args.FullPath, iconPath, StringComparison.OrdinalIgnoreCase))
                changed.TrySetResult(args);
        };

        await File.WriteAllTextAsync(regionPath, "region");
        await Task.Delay(300);
        await File.WriteAllTextAsync(iconPath, "icon");

        var change = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(iconPath, change.FullPath, ignoreCase: true);
        Assert.DoesNotContain(
            changes,
            item => string.Equals(item.FullPath, regionPath, StringComparison.OrdinalIgnoreCase));
    }

    private static GameInstance CreateInstance(string directory) => new()
    {
        Id = "instance",
        InstanceDirectory = directory
    };
}
