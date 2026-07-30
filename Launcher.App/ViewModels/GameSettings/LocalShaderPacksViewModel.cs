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

public sealed class LocalShaderPacksViewModel : IDisposable
{
    private readonly ILocalShaderPackService service;
    private readonly IStatusService statusService;
    private readonly ILogger<LocalShaderPacksViewModel> logger;
    private readonly LocalContentRefreshCoordinator<LocalShaderPack> refreshCoordinator;
    private readonly LocalResourceCategoryEnrichmentCoordinator<LocalShaderPack> categoryEnrichmentCoordinator;

    public LocalShaderPacksViewModel(
        ILocalShaderPackService localShaderPackService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILogger<LocalShaderPacksViewModel>? logger = null,
        ILocalResourceCategoryEnrichmentService? categoryEnrichmentService = null)
    {
        service = localShaderPackService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<LocalShaderPacksViewModel>.Instance;
        var dispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        refreshCoordinator = new LocalContentRefreshCoordinator<LocalShaderPack>(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.ShaderPacks,
            localShaderPackService.GetShaderPacksAsync,
            Apply,
            Clear,
            _ => ReportStatus(Strings.Status_LoadLocalShaderPacksFailed),
            dispatcher,
            this.logger);
        categoryEnrichmentCoordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalShaderPack>(
            categoryEnrichmentService,
            ResourceProjectKind.ShaderPack,
            shaderPack => shaderPack.FullPath,
            shaderPack => shaderPack.Categories,
            static (shaderPack, categories) => shaderPack.Categories = categories,
            () => CurrentShaderPacks,
            () => ShaderPacksChanged?.Invoke(this, EventArgs.Empty),
            dispatcher,
            this.logger,
            iconSourceSelector: shaderPack => shaderPack.IconSource,
            iconSourceSetter: static (shaderPack, iconSource) => shaderPack.IconSource = iconSource,
            projectReferenceSelector: shaderPack => shaderPack.ProjectReference,
            projectReferenceSetter: static (shaderPack, reference) => shaderPack.ProjectReference = reference,
            iconChanged: (shaderPack, iconSource) =>
                IconChanged?.Invoke(
                    this,
                    new LocalContentIconChangedEventArgs(shaderPack.FullPath, iconSource)));
    }

    public event EventHandler? ShaderPacksChanged;

    public event EventHandler<LocalContentIconChangedEventArgs>? IconChanged;

    public ObservableCollection<LocalShaderPack> ShaderPacks { get; } = [];

    public IReadOnlyList<LocalShaderPack> CurrentShaderPacks => refreshCoordinator.CurrentItems;

    public long Revision => refreshCoordinator.Revision;

    public void SetSelectedInstance(GameInstance? instance)
    {
        categoryEnrichmentCoordinator.Cancel();
        refreshCoordinator.SetInstance(instance);
    }

    public void SetSectionActive(bool active) => refreshCoordinator.SetSectionActive(active);

    public async Task<bool> RefreshIfInvalidatedAsync()
    {
        var previous = CurrentShaderPacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var refreshed = await refreshCoordinator.RefreshIfInvalidatedAsync();
        if (refreshed)
        {
            categoryEnrichmentCoordinator.Queue(
                CurrentShaderPacks.Where(item => !previous.Contains(item)).ToArray());
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

    public async Task<bool> RefreshShaderPacksAsync()
    {
        var previous = CurrentShaderPacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var refreshed = await refreshCoordinator.RefreshAsync();
        if (refreshed)
        {
            categoryEnrichmentCoordinator.Queue(
                CurrentShaderPacks.Where(item => !previous.Contains(item)).ToArray());
        }
        return refreshed;
    }

    public async Task<int> DeleteShaderPacksAsync(IEnumerable<LocalShaderPack> shaderPacks)
    {
        return await refreshCoordinator.ExecuteInternalOperationAsync(
            () => LocalContentBatchExecutor.ExecuteAsync(
                shaderPacks,
                item => item.FullPath,
                item => service.DeleteAsync(item),
                (item, exception) => logger.LogWarning(exception, "Failed to delete local shader pack. Path={Path}", item.FullPath)));
    }

    public async Task<LocalShaderPackImportResult> ImportShaderPackAsync(string archivePath, bool reportStatus = true)
    {
        var instance = refreshCoordinator.SelectedInstance;
        if (instance is null || string.IsNullOrWhiteSpace(archivePath))
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        var previous = CurrentShaderPacks.ToHashSet(ReferenceEqualityComparer.Instance);
        var result = await refreshCoordinator.ExecuteInternalOperationAsync(
            () => service.ImportAsync(instance, archivePath),
            static value => value.IsSuccess);
        if (!result.IsSuccess)
        {
            if (reportStatus)
            {
                ReportStatus(result.FailureReason is LocalShaderPackImportFailureReason.FileNotFound
                    ? Strings.Status_LocalShaderPackImportFileNotFound
                    : Strings.Status_LocalShaderPackImportFailed);
            }
            return result;
        }
        categoryEnrichmentCoordinator.Queue(
            CurrentShaderPacks.Where(item => !previous.Contains(item)).ToArray());
        if (reportStatus)
            ReportStatus(Strings.Status_LocalShaderPackImported);
        return result;
    }

    public void Dispose()
    {
        categoryEnrichmentCoordinator.Dispose();
        refreshCoordinator.Dispose();
    }

    private void Apply(IReadOnlyList<LocalShaderPack> items)
    {
        if (ShaderPacks.SynchronizeByKey(
                items,
                static item => item.FullPath,
                StringComparer.OrdinalIgnoreCase))
        {
            ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Clear()
    {
        if (ShaderPacks.Count == 0)
            return;
        ShaderPacks.Clear();
        ShaderPacksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReportStatus(string message) => statusService.Report(message);
}
