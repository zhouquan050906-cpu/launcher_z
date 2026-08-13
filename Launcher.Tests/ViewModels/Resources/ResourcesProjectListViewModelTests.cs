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

public sealed class ResourcesProjectListViewModelTests
{
    [Fact]
    public async Task FailedProjectListCanBeRefreshed()
    {
        var retryResult = new TaskCompletionSource<ResourceCatalogSearchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new SequencedResourceCatalogService(
            () => Task.FromException<ResourceCatalogSearchResult>(new HttpRequestException("offline")),
            () => retryResult.Task);
        using var page = new ResourcesPageViewModel(service).ModPage;
        var viewModel = page.ProjectList;

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.CanShowLoadErrorState);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CallCount);
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.CanShowLoadErrorState);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));

        retryResult.SetResult(CreateSearchResult());
        await refresh;

        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.CanShowLoadErrorState);
        Assert.Equal("sodium", Assert.Single(viewModel.VisibleProjects).Project.ProjectId);
    }

    [Fact]
    public async Task FailedRefreshRemainsRetryable()
    {
        var service = new SequencedResourceCatalogService(
            () => Task.FromException<ResourceCatalogSearchResult>(new HttpRequestException("offline")),
            () => Task.FromException<ResourceCatalogSearchResult>(new HttpRequestException("still offline")));
        using var page = new ResourcesPageViewModel(service).ModPage;
        var viewModel = page.ProjectList;

        await viewModel.RefreshAsync();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, service.CallCount);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.CanShowLoadErrorState);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task PageReentryRetriesFailedProjectListLoad()
    {
        var service = new SequencedResourceCatalogService(
            () => Task.FromException<ResourceCatalogSearchResult>(new HttpRequestException("offline")),
            () => Task.FromResult(CreateSearchResult()));
        using var page = new ResourcesPageViewModel(service).ModPage;
        var viewModel = page.ProjectList;

        await viewModel.RefreshAsync();
        viewModel.BeginEnsureLoaded();

        Assert.Equal(2, service.CallCount);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.CanShowLoadErrorState);
        Assert.Equal("sodium", Assert.Single(viewModel.VisibleProjects).Project.ProjectId);
    }

    private static ResourceCatalogSearchResult CreateSearchResult()
    {
        return new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Kind = ResourceProjectKind.Mod,
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "sodium",
                    Title = "Sodium"
                }
            ]
        };
    }

    private sealed class SequencedResourceCatalogService(
        params Func<Task<ResourceCatalogSearchResult>>[] responses) : IResourceCatalogService
    {
        private readonly Queue<Func<Task<ResourceCatalogSearchResult>>> responses = new(responses);

        public int CallCount { get; private set; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return responses.Dequeue()();
        }

        public Task<ResourceProjectVersionsResult> GetProjectVersionsAsync(
            ResourceProjectVersionsRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
