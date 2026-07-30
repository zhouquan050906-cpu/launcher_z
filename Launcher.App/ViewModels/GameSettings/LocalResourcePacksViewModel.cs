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

using System.Collections.ObjectModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalResourcePacksViewModel : IDisposable
{
    private readonly ILocalResourcePackService service;
    private readonly IStatusService statusService;
    private readonly ILogger<LocalResourcePacksViewModel> logger;
    private readonly LocalContentRefreshCoordinator<LocalResourcePack> refreshCoordinator;
    private readonly LocalResourceCategoryEnrichmentCoordinator<LocalResourcePack> categoryEnrichmentCoordinator;

    public LocalResourcePacksViewModel(
        ILocalResourcePackService localResourcePackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalResourcePacksViewModel>? logger = null,
        ILocalResourceCategoryEnrichmentService? categoryEnrichmentService = null)
    {
        service = localResourcePackService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<LocalResourcePacksViewModel>.Instance;
        var dispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        refreshCoordinator = new LocalContentRefreshCoordinator<LocalResourcePack>(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ResourcePacks,
            localResourcePackService.GetResourcePacksAsync,
            Apply,
            Clear,
            _ => ReportStatus(Strings.Status_LoadLocalResourcePacksFailed),
            dispatcher,
            this.logger);
        categoryEnrichmentCoordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalResourcePack>(
            categoryEnrichmentService,
            ResourceProjectKind.ResourcePack,
            resourcePack => resourcePack.FullPath,
            resourcePack => resourcePack.Categories,
            static (resourcePack, categories) => resourcePack.Categories = categories,
            () => CurrentResourcePacks,
            () => ResourcePacksChanged?.Invoke(this, EventArgs.Empty),
            dispatcher,
            this.logger,
            iconSourceSelector: resourcePack => resourcePack.IconSource,
            iconSourceSetter: static (resourcePack, iconSource) => resourcePack.IconSource = iconSource,
            preferResolvedIconSource: true,
            projectReferenceSelector: resourcePack => resourcePack.ProjectReference,
            projectReferenceSetter: static (resourcePack, reference) => resourcePack.ProjectReference = reference,
            iconChanged: (resourcePack, iconSource) =>
                IconChanged?.Invoke(
                    this,
                    new LocalContentIconChangedEventArgs(resourcePack.FullPath, iconSource)));
    }

    public event EventHandler? ResourcePacksChanged;

    public event EventHandler<LocalContentIconChangedEventArgs>? IconChanged;

    public ObservableCollection<LocalResourcePack> ResourcePacks { get; } = [];

    public IReadOnlyList<LocalResourcePack> CurrentResourcePacks => refreshCoordinator.CurrentItems;

    public long Revision => refreshCoordinator.Revision;

    public void SetSelectedInstance(GameInstance? instance)
    {
        categoryEnrichmentCoordinator.Cancel();
        refreshCoordinator.SetInstance(instance);
    }

    public void SetSectionActive(bool active) => refreshCoordinator.SetSectionActive(active);

    public async Task<bool> RefreshIfInvalidatedAsync()
    {
        var previous = CurrentResourcePacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var refreshed = await refreshCoordinator.RefreshIfInvalidatedAsync();
        if (refreshed)
        {
            categoryEnrichmentCoordinator.Queue(
                CurrentResourcePacks.Where(item => !previous.Contains(item)).ToArray());
        }
        return refreshed;
    }

    public void InvalidateSnapshot() => refreshCoordinator.InvalidateSnapshot();

    public void ReleaseObservation()
    {
        categoryEnrichmentCoordinator.Cancel();
        refreshCoordinator.ReleaseObservation();
    }

    public void SuspendWatcherForInstanceRename() => refreshCoordinator.SuspendForRename();

    public void ResumeWatcherAfterInstanceRename(bool restart = true) => refreshCoordinator.ResumeAfterRename(restart);

    public async Task<bool> RefreshResourcePacksAsync()
    {
        var previous = CurrentResourcePacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var refreshed = await refreshCoordinator.RefreshAsync();
        if (refreshed)
        {
            categoryEnrichmentCoordinator.Queue(
                CurrentResourcePacks.Where(item => !previous.Contains(item)).ToArray());
        }
        return refreshed;
    }

    public async Task<int> DeleteResourcePacksAsync(IEnumerable<LocalResourcePack> resourcePacks)
    {
        return await refreshCoordinator.ExecuteInternalOperationAsync(
            () => LocalContentBatchExecutor.ExecuteAsync(
                resourcePacks,
                item => item.FullPath,
                item => service.DeleteAsync(item),
                (item, exception) => logger.LogWarning(exception, "Failed to delete local resource pack. Path={Path}", item.FullPath)));
    }

    public async Task<LocalResourcePackImportResult> ImportResourcePackAsync(string archivePath, bool reportStatus = true)
    {
        var instance = refreshCoordinator.SelectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalResourcePackImportResult.Failure(LocalResourcePackImportFailureReason.UnexpectedError);
        var previous = CurrentResourcePacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var result = await refreshCoordinator.ExecuteInternalOperationAsync(
            () => service.ImportAsync(instance, archivePath),
            static value => value.IsSuccess);
        if (!result.IsSuccess)
        {
            if (reportStatus)
            {
                ReportStatus(result.FailureReason is LocalResourcePackImportFailureReason.FileNotFound
                    ? Strings.Status_LocalResourcePackImportFileNotFound
                    : Strings.Status_LocalResourcePackImportFailed);
            }
            return result;
        }
        categoryEnrichmentCoordinator.Queue(
            CurrentResourcePacks.Where(item => !previous.Contains(item)).ToArray());
        if (reportStatus)
            ReportStatus(Strings.Status_LocalResourcePackImported);
        return result;
    }

    public void Dispose()
    {
        categoryEnrichmentCoordinator.Dispose();
        refreshCoordinator.Dispose();
    }

    private void Apply(IReadOnlyList<LocalResourcePack> items)
    {
        if (ResourcePacks.SynchronizeByKey(
                items,
                static item => item.FullPath,
                StringComparer.OrdinalIgnoreCase))
        {
            ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Clear()
    {
        if (ResourcePacks.Count == 0)
            return;
        ResourcePacks.Clear();
        ResourcePacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportStatus(string message) => statusService.Report(message);
}
