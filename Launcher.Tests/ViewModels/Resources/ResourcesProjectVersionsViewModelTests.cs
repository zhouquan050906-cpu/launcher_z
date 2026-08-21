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

using Launcher.App.ViewModels.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Resources;

public sealed class ResourcesProjectVersionsViewModelTests
{
    [Fact]
    public async Task FailedVersionListCanBeRefreshed()
    {
        var retryResult = new TaskCompletionSource<ResourceProjectVersionsResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SequencedResourceCatalogService(
            () => Task.FromException<ResourceProjectVersionsResult>(new HttpRequestException("offline")),
            () => retryResult.Task);
        using var page = new ResourcesPageViewModel(service).ModPage;
        var viewModel = page.Versions;
        PrepareLocalDownloadTarget(viewModel);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.CanShowLoadErrorState);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CallCount);
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.CanShowLoadErrorState);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));

        retryResult.SetResult(CreateVersionsResult());
        await refresh;

        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.CanShowLoadErrorState);
        Assert.Equal("sodium-1", Assert.Single(viewModel.SourceVersions).VersionId);
    }

    [Fact]
    public async Task FailedVersionRefreshRemainsRetryable()
    {
        var service = new SequencedResourceCatalogService(
            () => Task.FromException<ResourceProjectVersionsResult>(new HttpRequestException("offline")),
            () => Task.FromException<ResourceProjectVersionsResult>(new HttpRequestException("still offline")));
        using var page = new ResourcesPageViewModel(service).ModPage;
        var viewModel = page.Versions;
        PrepareLocalDownloadTarget(viewModel);

        await viewModel.RefreshAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CallCount);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.CanShowLoadErrorState);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    private static void PrepareLocalDownloadTarget(ResourcesProjectVersionsViewModel viewModel)
    {
        viewModel.SetProject(new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Kind = ResourceProjectKind.Mod,
            Source = ResourceProjectSource.Modrinth,
            ProjectId = "sodium",
            Title = "Sodium"
        }));
        viewModel.SelectedTarget = Assert.Single(viewModel.InstallTargets, target => target.IsLocalDownload);
    }

    private static ResourceProjectVersionsResult CreateVersionsResult()
    {
        return new ResourceProjectVersionsResult
        {
            Versions =
            [
                new ResourceProjectVersion
                {
                    Kind = ResourceProjectKind.Mod,
                    VersionId = "sodium-1",
                    Name = "Sodium 1.0",
                    VersionNumber = "1.0",
                    FileName = "sodium.jar"
                }
            ]
        };
    }

    private sealed class SequencedResourceCatalogService(
        params Func<Task<ResourceProjectVersionsResult>>[] responses) : IResourceCatalogService
    {
        private readonly Queue<Func<Task<ResourceProjectVersionsResult>>> responses = new(responses);

        public int CallCount { get; private set; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return responses.Dequeue()();
        }

        public Task<string> InstallProjectVersionAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> DownloadProjectVersionAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ProjectVersionDownloadExistsAsync(
            ResourceProjectVersion version,
            string targetDirectory,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ProjectVersionInstallExistsAsync(
            ResourceProjectVersion version,
            GameInstance instance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
