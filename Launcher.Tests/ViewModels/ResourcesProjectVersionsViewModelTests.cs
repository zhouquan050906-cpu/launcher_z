/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels;

public sealed class ResourcesProjectVersionsViewModelTests
{
    [Theory]
    [InlineData(ResourceProjectKind.Mod, false)]
    [InlineData(ResourceProjectKind.ResourcePack, true)]
    [InlineData(ResourceProjectKind.ShaderPack, true)]
    [InlineData(ResourceProjectKind.World, true)]
    public async Task ExistingInstanceTargetsExcludeVanillaOnlyForMods(
        ResourceProjectKind kind,
        bool includesVanilla)
    {
        IReadOnlyList<GameInstance> instances =
        [
            CreateInstance("vanilla", LoaderKind.Vanilla),
            CreateInstance("fabric", LoaderKind.Fabric)
        ];
        var snapshotReadCount = 0;
        using var viewModel = new ResourcesProjectVersionsViewModel(
            CreateOptions(kind),
            resourceCatalogService: null,
            () =>
            {
                snapshotReadCount++;
                return instances;
            },
            ImmediateUiDispatcher.Instance,
            logger: null);

        viewModel.SetProject(new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = kind,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "project",
            Title = "Project"
        }));
        await WaitUntilAsync(() => !viewModel.IsLoadingTargets);

        var targetIds = viewModel.InstallTargets
            .Where(target => target.Instance is not null)
            .Select(target => target.Instance!.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var expectedIds = includesVanilla
            ? new[] { "fabric", "vanilla" }
            : ["fabric"];
        Assert.Equal(expectedIds, targetIds);
        Assert.True(viewModel.InstallTargets[^1].IsLocalDownload);
        Assert.Equal(1, snapshotReadCount);
    }

    private static GameInstance CreateInstance(string id, LoaderKind loader) => new()
    {
        Id = id,
        Name = id,
        MinecraftVersion = "1.21.5",
        VersionName = id,
        Loader = loader
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static ResourcesOnlineProjectPageOptions CreateOptions(ResourceProjectKind kind) => new(
        Kind: kind,
        Title: "Resources",
        FallbackIconKey: "resource",
        ShowsLoaderFilters: kind is ResourceProjectKind.Mod,
        AllVersionsText: "All versions",
        AllLoadersText: "All loaders",
        ProjectsLoadingText: "Loading",
        ProjectsEmptyText: "Empty",
        ProjectsLoadErrorText: "Error",
        ProjectsLoadingMoreText: "Loading more",
        ProjectsNoMoreText: "No more",
        ProjectsLoadMoreErrorText: "Load more error",
        CurseForgeMissingApiKeyText: "Missing key",
        DetailsInfoSectionText: "Details",
        InstallTargetSectionText: "Target",
        InstallTargetLocalText: "Local",
        InstallTargetsLoadingText: "Loading targets",
        InstallTargetsLoadErrorText: "Target error",
        VersionsLoadingText: "Loading versions",
        VersionsEmptyText: "No versions",
        VersionsEmptyLocalText: "No local versions",
        VersionsFilterEmptyText: "No filtered versions",
        VersionsLoadErrorText: "Version error",
        VersionsLoadingMoreText: "Loading more versions",
        VersionsNoMoreText: "No more versions",
        VersionsLoadMoreErrorText: "Version load more error",
        VersionsAllTitleText: "All versions",
        DownloadDirectoryPickerTitle: "Download",
        DownloadingText: "Downloading",
        DownloadingFormat: "Downloading {0}",
        DownloadedFormat: "Downloaded {0}",
        DownloadFailedText: "Download failed",
        InstalledFormat: "Installed {0}",
        InstallFailedText: "Install failed",
        FileExistsMessageFormat: "Exists {0}",
        TypeOptions: []);
}
