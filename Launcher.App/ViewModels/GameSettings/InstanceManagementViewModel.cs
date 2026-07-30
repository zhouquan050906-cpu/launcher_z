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
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceManagementViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameInstanceService instanceService;
    private readonly IInstanceBackupService? backupService;
    private readonly IStatusService statusService;
    private readonly ILogger<InstanceManagementViewModel> logger;
    private readonly object refreshInstancesSync = new();
    private readonly SemaphoreSlim refreshInstancesGate = new(1, 1);
    private LauncherSettings settings = new();
    private long refreshRequestGeneration;
    private long appliedRefreshGeneration;
    private string? lastRefreshedMinecraftDirectory;
    private bool hasLoadedInstances;
    private IReadOnlyList<InstanceCatalogEntrySnapshot> catalogSnapshot = [];
    private long catalogRevision;

    [ObservableProperty]
    private GameInstance? selectedInstance;

    [ObservableProperty]
    private string newInstanceName = string.Empty;

    public InstanceManagementViewModel(
        ISettingsService settingsService,
        IGameInstanceService instanceService,
        IStatusService statusService,
        IInstanceBackupService? backupService = null,
        ILogger<InstanceManagementViewModel>? logger = null)
    {
        this.settingsService = settingsService;
        this.instanceService = instanceService;
        this.backupService = backupService;
        this.statusService = statusService;
        this.logger = logger ?? NullLogger<InstanceManagementViewModel>.Instance;
    }

    public ObservableCollection<GameInstance> Instances { get; } = [];

    public bool HasLoadedInstances => hasLoadedInstances;

    public long CatalogRevision => Interlocked.Read(ref catalogRevision);

    public async Task PrimeInstancesAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        var loadedInstances = await instanceService.GetStoredInstancesAsync(launcherSettings);
        ApplyInstanceSnapshot(
            launcherSettings.MinecraftDirectory,
            loadedInstances,
            markAsFullyLoaded: false);
        logger.LogDebug(
            "Game management instances primed. Count={InstanceCount} SelectedInstanceId={SelectedInstanceId} CatalogRevision={CatalogRevision}",
            Instances.Count,
            SelectedInstance?.Id,
            CatalogRevision);
    }

    public async Task InitializeAsync(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        await EnsureInstancesLoadedAsync();
    }

    public async Task EnsureInstancesLoadedAsync()
    {
        if (hasLoadedInstances)
            return;

        await RefreshInstancesAsync();
    }

    public async Task RefreshInstancesAsync()
    {
        long requestedGeneration;
        lock (refreshInstancesSync)
            requestedGeneration = ++refreshRequestGeneration;

        await refreshInstancesGate.WaitAsync();
        try
        {
            lock (refreshInstancesSync)
            {
                if (appliedRefreshGeneration >= requestedGeneration
                    && lastRefreshedMinecraftDirectory is not null
                    && PathsEqual(lastRefreshedMinecraftDirectory, settings.MinecraftDirectory))
                {
                    return;
                }
            }

            while (true)
            {
                long generation;
                string requestedMinecraftDirectory;
                lock (refreshInstancesSync)
                {
                    generation = refreshRequestGeneration;
                    requestedMinecraftDirectory = settings.MinecraftDirectory;
                }

                var loadedInstances = await LoadInstanceSnapshotAsync(requestedMinecraftDirectory);
                lock (refreshInstancesSync)
                {
                    if (generation != refreshRequestGeneration
                        || !PathsEqual(requestedMinecraftDirectory, settings.MinecraftDirectory))
                    {
                        logger.LogDebug(
                            "Discarded stale instance refresh. RefreshGeneration={RefreshGeneration} CurrentGeneration={CurrentGeneration} RequestedDirectory={RequestedDirectory} CurrentDirectory={CurrentDirectory}",
                            generation,
                            refreshRequestGeneration,
                            requestedMinecraftDirectory,
                            settings.MinecraftDirectory);
                        continue;
                    }

                    ApplyInstanceSnapshot(
                        requestedMinecraftDirectory,
                        loadedInstances,
                        markAsFullyLoaded: true);
                    appliedRefreshGeneration = generation;
                    return;
                }
            }
        }
        finally
        {
            refreshInstancesGate.Release();
        }
    }

    public async Task<GameInstance?> CreateInstanceAsync(
        MinecraftVersionInfo? minecraftVersion,
        LoaderKind loader,
        LoaderVersionInfo? loaderVersion,
        IProgress<LauncherProgress>? progress)
    {
        if (minecraftVersion is null)
        {
            ReportStatus(Strings.Status_SelectMinecraftVersionFirst);
            return null;
        }

        var resolvedLoaderVersion = loader is LoaderKind.Vanilla ? null : loaderVersion?.Version;
        GameInstance instance;
        try
        {
            instance = await instanceService.CreateInstanceAsync(
                minecraftVersion.Name,
                loader,
                resolvedLoaderVersion,
                NewInstanceName,
                progress,
                downloadSourcePreference: settings.DownloadSourcePreference,
                downloadSpeedLimitMbPerSecond: settings.DownloadSpeedLimitMbPerSecond);
        }
        catch (DuplicateGameInstanceNameException)
        {
            ReportStatus(Strings.Status_DuplicateInstanceName);
            return null;
        }

        await refreshInstancesGate.WaitAsync();
        try
        {
            // The creation service receives only the version id. Retain the catalog type already
            // selected by this ViewModel so filtered instance projections update without a disk scan.
            instance.VersionType = minecraftVersion.Type;
            Instances.Add(instance);
            SelectedInstance = instance;
            UpdateCatalogSnapshotAndRevision();
        }
        finally
        {
            refreshInstancesGate.Release();
        }
        ReportStatus(string.Format(Strings.Status_InstanceCreatedFormat, instance.Name));
        return instance;
    }

    public async Task SaveSettingsAsync()
    {
        var minecraftDirectory = settings.MinecraftDirectory;
        if (lastRefreshedMinecraftDirectory is null
            || !PathsEqual(lastRefreshedMinecraftDirectory, minecraftDirectory))
        {
            logger.LogWarning(
                "Skipped saving instance defaults because the visible list belongs to a different Minecraft directory. CurrentDirectory={CurrentDirectory} RefreshedDirectory={RefreshedDirectory}",
                minecraftDirectory,
                lastRefreshedMinecraftDirectory);
            return;
        }

        var defaultInstanceId = settings.DefaultInstanceId;
        await settingsService.UpdateAsync(
            latest =>
            {
                if (PathsEqual(latest.MinecraftDirectory, minecraftDirectory))
                    latest.DefaultInstanceId = defaultInstanceId;
            });
        ReportStatus(Strings.Status_SettingsSaved);
    }

    public async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        ReportStatus(Strings.Status_InstanceSettingsSaved);
    }

    public async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        var saved = await SelectLaunchInstanceAsync(SelectedInstance);
        ReportStatus(saved
            ? string.Format(Strings.Status_DefaultInstanceSetFormat, SelectedInstance.Name)
            : Strings.Status_LaunchInstanceSelectionFailed);
    }

    public async Task<bool> SelectLaunchInstanceAsync(GameInstance instance)
    {
        var previousSelected = SelectedInstance;
        var selected = Instances.FirstOrDefault(existing =>
            string.Equals(existing.Id, instance.Id, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
            return false;

        SelectedInstance = selected;

        try
        {
            var saved = await instanceService.SetDefaultInstanceAsync(selected.Id);
            if (!saved)
            {
                SelectedInstance = previousSelected;
                return false;
            }
        }
        catch (Exception exception)
        {
            SelectedInstance = previousSelected;
            logger.LogWarning(
                exception,
                "Default game instance selection failed. InstanceId={InstanceId}",
                selected.Id);
            return false;
        }

        settings.DefaultInstanceId = selected.Id;
        return true;
    }

    public async Task ApplyUpdatedInstanceAsync(GameInstance instance)
    {
        await refreshInstancesGate.WaitAsync();
        try
        {
            ApplyUpdatedInstanceCore(instance);
        }
        finally
        {
            refreshInstancesGate.Release();
        }
    }

    private void ApplyUpdatedInstanceCore(GameInstance instance)
    {
        var index = FindInstanceIndex(instance.Id);
        var wasSelected = string.Equals(SelectedInstance?.Id, instance.Id, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            var existing = Instances[index];
            if (!ReferenceEquals(existing, instance))
                GameInstanceStateCopier.Copy(instance, existing);
            instance = existing;
        }
        else
            Instances.Add(instance);

        if (wasSelected || SelectedInstance is null)
            SelectedInstance = instance;

        hasLoadedInstances = true;
        UpdateCatalogSnapshotAndRevision();
        logger.LogDebug(
            "Game management instance updated locally. InstanceId={InstanceId} Count={InstanceCount} SelectedInstanceId={SelectedInstanceId} CatalogRevision={CatalogRevision}",
            instance.Id,
            Instances.Count,
            SelectedInstance?.Id,
            CatalogRevision);
    }

    private async Task<IReadOnlyList<GameInstance>> LoadInstanceSnapshotAsync(string requestedMinecraftDirectory)
    {
        if (backupService is not null)
        {
            await backupService.RecoverPendingRestoresAsync(requestedMinecraftDirectory);
        }

        return await instanceService.GetInstancesAsync();
    }

    private void ApplyInstanceSnapshot(
        string requestedMinecraftDirectory,
        IReadOnlyList<GameInstance> loadedInstances,
        bool markAsFullyLoaded)
    {
        var previousSelectedId = SelectedInstance?.Id;
        var rootChanged = lastRefreshedMinecraftDirectory is null
            || !PathsEqual(lastRefreshedMinecraftDirectory, requestedMinecraftDirectory);
        ReconcileInstances(loadedInstances);
        lastRefreshedMinecraftDirectory = requestedMinecraftDirectory;
        SelectedInstance = ResolveSelectedInstance(settings.DefaultInstanceId, previousSelectedId);
        if (!string.IsNullOrWhiteSpace(settings.DefaultInstanceId)
            && Instances.All(instance => !string.Equals(
                instance.Id,
                settings.DefaultInstanceId,
                StringComparison.OrdinalIgnoreCase)))
        {
            settings.DefaultInstanceId = SelectedInstance?.Id ?? string.Empty;
        }
        hasLoadedInstances = markAsFullyLoaded;
        UpdateCatalogSnapshotAndRevision(rootChanged);
        logger.LogDebug(
            "Game management instances refreshed. Count={InstanceCount} SelectedInstanceId={SelectedInstanceId} CatalogRevision={CatalogRevision}",
            Instances.Count,
            SelectedInstance?.Id,
            CatalogRevision);
    }

    public async Task<bool> RemoveInstanceAsync(string instanceId)
    {
        await refreshInstancesGate.WaitAsync();
        try
        {
            return RemoveInstanceCore(instanceId);
        }
        finally
        {
            refreshInstancesGate.Release();
        }
    }

    private bool RemoveInstanceCore(string instanceId)
    {
        var index = FindInstanceIndex(instanceId);
        if (index < 0)
            return false;

        var wasSelected = string.Equals(
            SelectedInstance?.Id,
            instanceId,
            StringComparison.OrdinalIgnoreCase);
        Instances.RemoveAt(index);
        if (wasSelected)
        {
            SelectedInstance = ResolveSelectedInstance(settings.DefaultInstanceId, previousSelectedId: null);
            settings.DefaultInstanceId = SelectedInstance?.Id ?? string.Empty;
        }
        UpdateCatalogSnapshotAndRevision();
        logger.LogDebug(
            "Game management instance removed locally. InstanceId={InstanceId} Count={InstanceCount} SelectedInstanceId={SelectedInstanceId} CatalogRevision={CatalogRevision}",
            instanceId,
            Instances.Count,
            SelectedInstance?.Id,
            CatalogRevision);
        return true;
    }

    private void ReconcileInstances(IReadOnlyList<GameInstance> loadedInstances)
    {
        var existingById = Instances
            .Where(instance => !string.IsNullOrWhiteSpace(instance.Id))
            .GroupBy(instance => instance.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var next = new List<GameInstance>(loadedInstances.Count);

        foreach (var loadedInstance in loadedInstances)
        {
            if (!string.IsNullOrWhiteSpace(loadedInstance.Id)
                && existingById.TryGetValue(loadedInstance.Id, out var existing))
            {
                if (!ReferenceEquals(existing, loadedInstance))
                    GameInstanceStateCopier.Copy(loadedInstance, existing);
                next.Add(existing);
            }
            else
            {
                next.Add(loadedInstance);
            }
        }

        for (var index = 0; index < next.Count; index++)
        {
            var instance = next[index];
            if (index < Instances.Count && ReferenceEquals(Instances[index], instance))
                continue;

            var existingIndex = -1;
            for (var candidate = index + 1; candidate < Instances.Count; candidate++)
            {
                if (!ReferenceEquals(Instances[candidate], instance))
                    continue;
                existingIndex = candidate;
                break;
            }

            if (existingIndex >= 0)
                Instances.Move(existingIndex, index);
            else
                Instances.Insert(index, instance);
        }

        while (Instances.Count > next.Count)
            Instances.RemoveAt(Instances.Count - 1);
    }

    private void UpdateCatalogSnapshotAndRevision(bool forceChanged = false)
    {
        var nextSnapshot = Instances
            .Select(InstanceCatalogEntrySnapshot.Create)
            .ToArray();
        if (!forceChanged && catalogSnapshot.SequenceEqual(nextSnapshot))
            return;

        catalogSnapshot = nextSnapshot;
        Interlocked.Increment(ref catalogRevision);
    }

    partial void OnSelectedInstanceChanged(GameInstance? oldValue, GameInstance? newValue)
    {
        if (string.Equals(oldValue?.Id, newValue?.Id, StringComparison.OrdinalIgnoreCase))
            return;

        Interlocked.Increment(ref catalogRevision);
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);

    private GameInstance? ResolveSelectedInstance(string? defaultInstanceId, string? previousSelectedId)
    {
        var selected = !string.IsNullOrWhiteSpace(defaultInstanceId)
            ? Instances.FirstOrDefault(instance => string.Equals(instance.Id, defaultInstanceId, StringComparison.OrdinalIgnoreCase))
            : null;
        selected ??= !string.IsNullOrWhiteSpace(previousSelectedId)
            ? Instances.FirstOrDefault(instance => string.Equals(instance.Id, previousSelectedId, StringComparison.OrdinalIgnoreCase))
            : null;
        selected ??= Instances.FirstOrDefault();
        return selected;
    }

    private int FindInstanceIndex(string instanceId)
    {
        for (var index = 0; index < Instances.Count; index++)
        {
            if (string.Equals(Instances[index].Id, instanceId, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}

