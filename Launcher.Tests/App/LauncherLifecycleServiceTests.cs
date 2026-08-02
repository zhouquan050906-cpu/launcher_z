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

using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.App;

public sealed class LauncherLifecycleServiceTests
{
    [Fact]
    public async Task StateSyncDebouncesChangesAndRearmsMonitor()
    {
        var monitor = new TestLauncherStateMonitor();
        using var service = new LauncherStateSyncService(
            monitor,
            ImmediateUiDispatcher.Instance,
            debounceDelay: TimeSpan.FromMilliseconds(10));
        var settings = new LauncherSettings();
        var synchronizationCount = 0;

        service.Start(() => settings, () =>
        {
            synchronizationCount++;
            return Task.CompletedTask;
        });

        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        await service.WaitForPendingSyncAsync();

        Assert.Equal(1, synchronizationCount);
        Assert.Equal(2, monitor.WatchCount);
        Assert.Same(settings, monitor.LastSettings);
    }

    [Fact]
    public async Task StoppingStateSyncCancelsPendingRefresh()
    {
        var monitor = new TestLauncherStateMonitor();
        using var service = new LauncherStateSyncService(
            monitor,
            ImmediateUiDispatcher.Instance,
            debounceDelay: TimeSpan.FromMilliseconds(50));
        var synchronizationCount = 0;
        service.Start(() => new LauncherSettings(), () =>
        {
            synchronizationCount++;
            return Task.CompletedTask;
        });

        monitor.RaiseStateChanged();
        service.Stop();
        await service.WaitForPendingSyncAsync();

        Assert.Equal(0, synchronizationCount);
        Assert.Equal(1, monitor.StopCount);
    }

    [Fact]
    public async Task ChangesDuringSynchronizationProduceOneTrailingSynchronization()
    {
        var monitor = new TestLauncherStateMonitor();
        using var service = new LauncherStateSyncService(
            monitor,
            ImmediateUiDispatcher.Instance,
            debounceDelay: TimeSpan.FromMilliseconds(10));
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var synchronizationCount = 0;
        service.Start(() => new LauncherSettings(), async () =>
        {
            var call = Interlocked.Increment(ref synchronizationCount);
            if (call != 1)
                return;

            firstStarted.TrySetResult();
            await releaseFirst.Task;
        });

        monitor.RaiseStateChanged();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        monitor.RaiseStateChanged();
        releaseFirst.TrySetResult();
        await service.WaitForPendingSyncAsync();

        Assert.Equal(2, synchronizationCount);
        Assert.Equal(3, monitor.WatchCount);
    }

    private sealed class TestLauncherStateMonitor : ILauncherStateMonitor
    {
        public event EventHandler? StateChanged;

        public int WatchCount { get; private set; }

        public int StopCount { get; private set; }

        public LauncherSettings? LastSettings { get; private set; }

        public void Watch(LauncherSettings settings)
        {
            WatchCount++;
            LastSettings = settings;
        }

        public void Stop()
        {
            StopCount++;
        }

        public void Dispose()
        {
        }

        public void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestInstallCleanupService : IInstanceInstallCleanupService
    {
        public int CallCount { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public Task CleanupPendingAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedCancellation = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorkspaceCleanupService : IModpackWorkspaceCleanupService
    {
        public int CallCount { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public async Task CleanupAllAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed class TestSandboxCleanupService(Task? waitTask = null) : IModpackSandboxCleanupService
    {
        public int WaitCallCount { get; private set; }

        public IModpackSandboxSession CreateSession(ModpackSandboxKind kind) =>
            throw new NotSupportedException();

        public Task CleanupStaleAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task WaitForPendingCleanupAsync(CancellationToken cancellationToken = default)
        {
            WaitCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (waitTask is not null)
                await waitTask.WaitAsync(cancellationToken);
        }
    }

}
