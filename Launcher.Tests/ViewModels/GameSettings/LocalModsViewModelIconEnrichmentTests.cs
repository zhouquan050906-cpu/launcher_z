/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class LocalModsViewModelIconEnrichmentTests
{
    [Fact]
    public async Task UnchangedInventoryRefreshDoesNotCancelRunningIconEnrichment()
    {
        var mod = new LocalMod
        {
            Name = "Example",
            FileName = "example.jar",
            FullPath = Path.Combine("instance", "mods", "example.jar"),
            IsEnabled = true
        };
        var service = new StableModService([mod]);
        var enrichment = new ControlledIconEnrichmentService();
        using var viewModel = new LocalModsViewModel(
            service,
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance,
            enrichment);
        viewModel.SetSelectedInstance(new GameInstance
        {
            Id = "instance",
            InstanceDirectory = "instance"
        });
        var iconApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ModsChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(viewModel.CurrentMods.Single().IconSource))
                iconApplied.TrySetResult(true);
        };

        Assert.True(await viewModel.RefreshModsAsync());
        await enrichment.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await viewModel.RefreshModsAsync());
        enrichment.Release.TrySetResult(true);
        await iconApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, enrichment.ResolveCallCount);
        Assert.Equal("file:///cached/example.png", mod.IconSource);
    }

    [Fact]
    public async Task ToggleModUpdatesSnapshotWithoutReloadingInventory()
    {
        var mod = new LocalMod
        {
            Name = "Example",
            FileName = "example.jar",
            FullPath = Path.Combine("instance", "mods", "example.jar"),
            IsEnabled = true
        };
        var service = new StableModService([mod]);
        using var viewModel = new LocalModsViewModel(
            service,
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance);
        viewModel.SetSelectedInstance(new GameInstance
        {
            Id = "instance",
            InstanceDirectory = "instance"
        });
        Assert.True(await viewModel.RefreshModsAsync());
        var revisionBeforeToggle = viewModel.Revision;

        await viewModel.ToggleModAsync(mod);

        Assert.Equal(1, service.GetModsCallCount);
        Assert.Equal(1, service.SetEnabledCallCount);
        Assert.False(mod.IsEnabled);
        Assert.Equal("example.jar.disabled", mod.FileName);
        Assert.EndsWith(
            Path.Combine("mods", "example.jar.disabled"),
            mod.FullPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(revisionBeforeToggle + 1, viewModel.Revision);
    }

    [Fact]
    public async Task SectionDeactivationDoesNotRestartRunningIconEnrichment()
    {
        var mod = new LocalMod
        {
            Name = "Example",
            FileName = "example.jar",
            FullPath = Path.Combine("instance", "mods", "example.jar"),
            IsEnabled = true
        };
        var service = new StableModService([mod]);
        var enrichment = new ControlledIconEnrichmentService();
        using var viewModel = new LocalModsViewModel(
            service,
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance,
            enrichment);
        viewModel.SetSelectedInstance(new GameInstance
        {
            Id = "instance",
            InstanceDirectory = "instance"
        });
        viewModel.SetSectionActive(true);
        var iconApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ModsChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(viewModel.CurrentMods.Single().IconSource))
                iconApplied.TrySetResult(true);
        };

        Assert.True(await viewModel.RefreshModsAsync());
        await enrichment.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SetSectionActive(false);
        enrichment.Release.TrySetResult(true);
        await iconApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SetSectionActive(true);
        Assert.True(await viewModel.RefreshIfInvalidatedAsync());

        Assert.Equal(1, enrichment.ResolveCallCount);
        Assert.Equal(1, service.GetModsCallCount);
        Assert.Equal("file:///cached/example.png", mod.IconSource);
    }

    private sealed class StableModService(IReadOnlyList<LocalMod> mods) : IModService
    {
        public int GetModsCallCount { get; private set; }

        public int SetEnabledCallCount { get; private set; }

        public Task<IReadOnlyList<LocalMod>> GetModsAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default)
        {
            GetModsCallCount++;
            return Task.FromResult(mods);
        }

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetEnabledAsync(
            LocalMod mod,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            SetEnabledCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledIconEnrichmentService : ILocalModIconEnrichmentService
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ResolveCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public async Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default,
            IProgress<IReadOnlyDictionary<string, string>>? progress = null)
        {
            ResolveCallCount++;
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [mods.Single().FullPath] = "file:///cached/example.png"
            };
        }
    }

    private sealed class NullStatusService : IStatusService
    {
        public event Action<string>? MessageReported
        {
            add { }
            remove { }
        }

        public void Report(string message)
        {
        }
    }

    private sealed class NoOpDirectoryMonitor : IInstanceDirectoryMonitor
    {
        public IInstanceDirectoryWatch Watch(GameInstance instance, InstanceDirectoryKind directoryKind) =>
            new NoOpDirectoryWatch();
    }

    private sealed class NoOpDirectoryWatch : IInstanceDirectoryWatch
    {
        public event EventHandler<InstanceDirectoryChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }
}
