/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.ViewModels.GameSettings;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class InstanceContentRefreshWatcherTests
{
    [Fact]
    public async Task RapidDirectoryChangesAreDebouncedIntoSingleRefresh()
    {
        var monitor = new RecordingDirectoryMonitor();
        var refreshCount = 0;
        var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () =>
            {
                Interlocked.Increment(ref refreshCount);
                refreshed.TrySetResult(true);
                return Task.CompletedTask;
            },
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);

        monitor.Current.Raise("Created", "first.jar");
        monitor.Current.Raise("Changed", "first.jar");

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void IdempotentConfigurationDoesNotReplaceActiveWatch()
    {
        var monitor = new RecordingDirectoryMonitor();
        var instance = CreateInstance();
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () => Task.CompletedTask,
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(instance);
        watcher.SetEnabled(true);
        var firstWatch = monitor.Current;

        watcher.SetInstance(new GameInstance
        {
            Id = instance.Id,
            InstanceDirectory = instance.InstanceDirectory
        });
        watcher.SetEnabled(true);

        Assert.False(firstWatch.IsDisposed);
        Assert.Equal(1, monitor.WatchCount);
    }

    [Fact]
    public async Task ChangeDuringRefreshProducesOneSequentialTrailingRefresh()
    {
        var monitor = new RecordingDirectoryMonitor();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        var activeCount = 0;
        var maximumActiveCount = 0;
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            async () =>
            {
                var active = Interlocked.Increment(ref activeCount);
                maximumActiveCount = Math.Max(maximumActiveCount, active);
                var current = Interlocked.Increment(ref refreshCount);
                if (current == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                }
                else
                {
                    secondFinished.TrySetResult(true);
                }
                Interlocked.Decrement(ref activeCount);
            },
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);

        monitor.Current.Raise("Created", "first.jar");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        monitor.Current.Raise("Changed", "first.jar");
        monitor.Current.Raise("Changed", "first.jar");
        releaseFirst.TrySetResult(true);
        await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, refreshCount);
        Assert.Equal(1, maximumActiveCount);
    }

    [Fact]
    public async Task WatcherErrorRefreshesAndRebuildsWatch()
    {
        var monitor = new RecordingDirectoryMonitor();
        var refreshed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new InstanceContentRefreshWatcher(
            monitor,
            InstanceDirectoryKind.Mods,
            () =>
            {
                refreshed.TrySetResult(true);
                return Task.CompletedTask;
            },
            _ => { },
            NullLogger.Instance);
        watcher.SetInstance(CreateInstance());
        watcher.SetEnabled(true);

        monitor.Current.Raise("Error", "mods");
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => monitor.WatchCount == 2);

        Assert.Equal(2, monitor.WatchCount);
    }

    private static GameInstance CreateInstance()
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceDirectory = "instance"
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
            await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class RecordingDirectoryMonitor : IInstanceDirectoryMonitor
    {
        public int WatchCount { get; private set; }
        public RecordingDirectoryWatch Current { get; private set; } = new();

        public IInstanceDirectoryWatch Watch(GameInstance instance, InstanceDirectoryKind directoryKind)
        {
            WatchCount++;
            Current = new RecordingDirectoryWatch();
            return Current;
        }
    }

    private sealed class RecordingDirectoryWatch : IInstanceDirectoryWatch
    {
        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed;

        public bool IsDisposed { get; private set; }

        public void Raise(string changeType, string path)
        {
            Changed?.Invoke(this, new InstanceDirectoryChangedEventArgs(changeType, path));
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
