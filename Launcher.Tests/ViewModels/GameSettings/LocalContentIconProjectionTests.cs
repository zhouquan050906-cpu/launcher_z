/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class LocalContentIconProjectionTests
{
    [Fact]
    public async Task ModIconProgressUpdatesMatchingItemBeforeEnrichmentCompletes()
    {
        var mods = Enumerable.Range(0, 2)
            .Select(index => new LocalMod
            {
                Name = $"Mod {index}",
                FileName = $"mod-{index}.jar",
                FullPath = Path.Combine("instance", "mods", $"mod-{index}.jar"),
                IsEnabled = true
            })
            .ToArray();
        var enrichment = new ControlledModIconEnrichmentService();
        using var localMods = new LocalModsViewModel(
            new FixedModService(mods),
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance,
            enrichment);
        var management = new InstanceModManagementSettingsViewModel(
            null!,
            localMods,
            new NullStatusService(),
            null!,
            null!,
            null!,
            new NullFloatingMessageService(),
            ImmediateUiDispatcher.Instance);
        management.OnSelectedInstanceChanged(CreateInstance());
        await management.OnSectionActivatedAsync();
        await enrichment.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstItem = Assert.Single(management.Mods.Where(item => item.FullPath == mods[0].FullPath));
        var secondItem = Assert.Single(management.Mods.Where(item => item.FullPath == mods[1].FullPath));

        enrichment.ReleaseFirst.TrySetResult();
        await enrichment.FirstReported.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(firstItem, management.Mods.Single(item => item.FullPath == mods[0].FullPath));
        Assert.Equal("file:///cache/mod-0.png", firstItem.IconSource);
        Assert.Null(secondItem.IconSource);
        Assert.False(enrichment.Completed.Task.IsCompleted);

        enrichment.ReleaseSecond.TrySetResult();
        await enrichment.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ResourcePackIconProgressUpdatesMatchingItemBeforeMetadataCompletes()
    {
        var resourcePacks = Enumerable.Range(0, 2)
            .Select(index => new LocalResourcePack
            {
                Name = $"Pack {index}",
                FileName = $"pack-{index}.zip",
                FullPath = Path.Combine("instance", "resourcepacks", $"pack-{index}.zip"),
                IconSource = index == 0 ? "file:///cache/local-pack.png" : null
            })
            .ToArray();
        var enrichment = new ControlledResourceMetadataService();
        using var localResourcePacks = new LocalResourcePacksViewModel(
            new FixedLocalContentService(resourcePacks, []),
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance,
            categoryEnrichmentService: enrichment);
        var management = new InstanceResourcePackManagementSettingsViewModel(
            null!,
            localResourcePacks,
            new NullStatusService(),
            null!,
            null!,
            null!,
            ImmediateUiDispatcher.Instance);
        management.OnSelectedInstanceChanged(CreateInstance());
        await management.OnSectionActivatedAsync();
        await enrichment.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstItem = Assert.Single(management.ResourcePacks.Where(item => item.FullPath == resourcePacks[0].FullPath));
        var secondItem = Assert.Single(management.ResourcePacks.Where(item => item.FullPath == resourcePacks[1].FullPath));

        enrichment.ReleaseFirst.TrySetResult();
        await enrichment.FirstReported.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(firstItem, management.ResourcePacks.Single(item => item.FullPath == resourcePacks[0].FullPath));
        Assert.Equal("file:///cache/remote-0.png", firstItem.IconSource);
        Assert.Null(secondItem.IconSource);
        Assert.False(enrichment.Completed.Task.IsCompleted);

        enrichment.ReleaseSecond.TrySetResult();
        await enrichment.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ShaderPackIconProgressUpdatesMatchingItemBeforeMetadataCompletes()
    {
        var shaderPacks = Enumerable.Range(0, 2)
            .Select(index => new LocalShaderPack
            {
                Name = $"Shader {index}",
                FileName = $"shader-{index}.zip",
                FullPath = Path.Combine("instance", "shaderpacks", $"shader-{index}.zip")
            })
            .ToArray();
        var enrichment = new ControlledResourceMetadataService();
        using var localShaderPacks = new LocalShaderPacksViewModel(
            new FixedLocalContentService([], shaderPacks),
            new NullStatusService(),
            new NoOpDirectoryMonitor(),
            ImmediateUiDispatcher.Instance,
            categoryEnrichmentService: enrichment);
        var management = new InstanceShaderPackManagementSettingsViewModel(
            null!,
            localShaderPacks,
            new NullStatusService(),
            null!,
            null!,
            null!,
            ImmediateUiDispatcher.Instance);
        management.OnSelectedInstanceChanged(CreateInstance());
        await management.OnSectionActivatedAsync();
        await enrichment.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstItem = Assert.Single(management.ShaderPacks.Where(item => item.FullPath == shaderPacks[0].FullPath));
        var secondItem = Assert.Single(management.ShaderPacks.Where(item => item.FullPath == shaderPacks[1].FullPath));

        enrichment.ReleaseFirst.TrySetResult();
        await enrichment.FirstReported.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Same(firstItem, management.ShaderPacks.Single(item => item.FullPath == shaderPacks[0].FullPath));
        Assert.Equal("file:///cache/remote-0.png", firstItem.IconSource);
        Assert.Null(secondItem.IconSource);
        Assert.False(enrichment.Completed.Task.IsCompleted);

        enrichment.ReleaseSecond.TrySetResult();
        await enrichment.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static GameInstance CreateInstance() => new()
    {
        Id = "instance",
        Name = "Instance",
        InstanceDirectory = "instance",
        MinecraftVersion = "1.21.1",
        Loader = LoaderKind.Fabric
    };

    private sealed class ControlledModIconEnrichmentService : ILocalModIconEnrichmentService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstReported { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public async Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
            IReadOnlyList<LocalMod> mods,
            CancellationToken cancellationToken = default,
            IProgress<LocalContentIconResolution>? progress = null)
        {
            Started.TrySetResult();
            await ReleaseFirst.Task.WaitAsync(cancellationToken);
            progress?.Report(new LocalContentIconResolution(mods[0].FullPath, "file:///cache/mod-0.png"));
            FirstReported.TrySetResult();
            await ReleaseSecond.Task.WaitAsync(cancellationToken);
            progress?.Report(new LocalContentIconResolution(mods[1].FullPath, "file:///cache/mod-1.png"));
            Completed.TrySetResult();
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [mods[0].FullPath] = "file:///cache/mod-0.png",
                [mods[1].FullPath] = "file:///cache/mod-1.png"
            };
        }
    }

    private sealed class ControlledResourceMetadataService : ILocalResourceCategoryEnrichmentService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstReported { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecond { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveCachedMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default,
            IProgress<LocalContentIconResolution>? iconProgress = null) =>
            Task.FromResult<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>>(
                new Dictionary<string, LocalResourceEnrichmentResult>(StringComparer.OrdinalIgnoreCase));

        public async Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default,
            IProgress<LocalContentIconResolution>? iconProgress = null)
        {
            Started.TrySetResult();
            await ReleaseFirst.Task.WaitAsync(cancellationToken);
            iconProgress?.Report(new LocalContentIconResolution(resources[0].FullPath, "file:///cache/remote-0.png"));
            FirstReported.TrySetResult();
            await ReleaseSecond.Task.WaitAsync(cancellationToken);
            iconProgress?.Report(new LocalContentIconResolution(resources[1].FullPath, "file:///cache/remote-1.png"));
            Completed.TrySetResult();
            return resources.ToDictionary(
                resource => resource.FullPath,
                resource => new LocalResourceEnrichmentResult(
                    [],
                    resource == resources[0]
                        ? "file:///cache/remote-0.png"
                        : "file:///cache/remote-1.png"),
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCachedCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class FixedModService(IReadOnlyList<LocalMod> mods) : IModService
    {
        public Task<IReadOnlyList<LocalMod>> GetModsAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(mods);

        public Task<LocalMod> ImportAsync(
            GameInstance instance,
            string sourceJarPath,
            bool overwriteExisting = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetEnabledAsync(
            LocalMod mod,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedLocalContentService(
        IReadOnlyList<LocalResourcePack> resourcePacks,
        IReadOnlyList<LocalShaderPack> shaderPacks) :
        ILocalResourcePackService,
        ILocalShaderPackService
    {
        public Task<IReadOnlyList<LocalResourcePack>> GetResourcePacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(resourcePacks);

        public Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(shaderPacks);

        public Task<LocalResourcePackImportResult> ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        Task<LocalShaderPackImportResult> ILocalShaderPackService.ImportAsync(
            GameInstance instance,
            string archivePath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            LocalResourcePack resourcePack,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            IEnumerable<LocalResourcePack> resourcePacks,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            LocalShaderPack shaderPack,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            IEnumerable<LocalShaderPack> shaderPacks,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

    private sealed class NullFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested
        {
            add { }
            remove { }
        }

        public void Show(string message)
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
