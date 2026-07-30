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
using Launcher.App.Resources;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Download;

public sealed class DownloadViewModelTests
{
    [Fact]
    public async Task VersionListLoadsAndFiltersItsOwnCatalog()
    {
        using var viewModel = new DownloadVersionListViewModel(
            new StubGameVersionService(
            [
                new MinecraftVersionInfo("1.21", "release", false),
                new MinecraftVersionInfo("25w01a", "snapshot", false)
            ]),
            ImmediateUiDispatcher.Instance);

        await viewModel.EnsureVersionsLoadedAsync();

        var release = Assert.Single(viewModel.VisibleVersions);
        Assert.Equal("1.21", release.Name);
        Assert.Equal("release", viewModel.SelectedVersionCategory?.Id);
    }

    [Fact]
    public async Task CancelingDownloadTaskCancelsInstallationWithoutFailureMessage()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeGameInstanceService { WaitBeforeCreate = release.Task };
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromSeconds(1));
        var viewModel = CreateInstallViewModel(service, tasks, new DownloadInstanceNameTracker());
        var installation = viewModel.InstallAsync(CreateInstallRequest("cancel-me"));
        await service.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        tasks.CancelTask(Assert.Single(tasks.Tasks));
        await installation;

        Assert.False(viewModel.IsInstalling);
        Assert.False(viewModel.HasInstallError);
        Assert.Empty(service.CreatedInstances);
    }

    [Fact]
    public async Task CompletedInstallPublishesVersionTypeForInstanceCategoryProjection()
    {
        var service = new FakeGameInstanceService();
        var tasks = new DownloadTasksPageViewModel(TimeSpan.FromSeconds(1));
        var viewModel = CreateInstallViewModel(service, tasks, new DownloadInstanceNameTracker());
        GameInstance? installed = null;
        viewModel.InstanceInstalled += (_, instance) => installed = instance;

        await viewModel.InstallAsync(CreateInstallRequest("new-release"));

        Assert.NotNull(installed);
        Assert.Equal("release", installed.VersionType);
    }

    [Fact]
    public async Task InstallRequestRetainsSelectedMinecraftVersionType()
    {
        var service = new FakeGameInstanceService();
        using var viewModel = new DownloadInstanceOptionsViewModel(
            service,
            [],
            new DownloadInstanceNameTracker());
        await viewModel.PrepareAsync(
            new DownloadMinecraftVersionItem(
                new MinecraftVersionInfo("25w01a", "snapshot", false)));

        var request = viewModel.CreateInstallRequest();

        Assert.NotNull(request);
        Assert.Equal("snapshot", request.MinecraftVersionType);
    }

    private static DownloadInstallViewModel CreateInstallViewModel(
        FakeGameInstanceService service,
        DownloadTasksPageViewModel tasks,
        DownloadInstanceNameTracker tracker)
    {
        return new DownloadInstallViewModel(
            service,
            tasks,
            tracker,
            ImmediateUiDispatcher.Instance,
            new RecordingFloatingMessageService(),
            NullLogger<DownloadInstallViewModel>.Instance);
    }

    private static DownloadInstallRequest CreateInstallRequest(string instanceName)
    {
        return new DownloadInstallRequest(
            "1.20.1",
            "release",
            instanceName,
            LoaderKind.Vanilla,
            null,
            null,
            null,
            "Vanilla",
            LauncherDefaults.DefaultDownloadSourcePreference,
            0);
    }

    private sealed class StubGameVersionService(IReadOnlyList<MinecraftVersionInfo> versions) : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromResult(versions);
        }
    }

    private sealed class RecordingFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public void Show(string message)
        {
            MessageRequested?.Invoke(message);
        }
    }
}
