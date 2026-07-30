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

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModpacksPageViewModel : ResourcesModPageViewModel
{
    public ResourcesModpacksPageViewModel(
        ResourcesPageViewModel parent,
        IResourceCatalogService? resourceCatalogService = null,
        ILogger? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null,
        Func<IReadOnlyList<GameInstance>>? getInstanceCatalogSnapshot = null,
        IStatusService? statusService = null,
        IFilePickerService? filePickerService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null,
        IResourceProjectInstallationService? resourceProjectInstallationService = null,
        IResourceDependencyPlanningService? resourceDependencyPlanningService = null)
        : base(
            parent,
            CreateModpackOptions(),
            resourceCatalogService,
            logger,
            uiDispatcher,
            gameVersionService,
            getInstanceCatalogSnapshot,
            statusService,
            filePickerService,
            floatingMessageService,
            downloadTasksPage,
            resourceProjectInstallationService: resourceProjectInstallationService,
            resourceDependencyPlanningService: resourceDependencyPlanningService)
    {
    }

    private static ResourcesOnlineProjectPageOptions CreateModpackOptions()
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.Modpack,
            Strings.Resources_SectionModpacks,
            "general/general_extention",
            ShowsLoaderFilters: true,
            Strings.Resources_ModFilterAllVersions,
            Strings.Resources_ModpackFilterAllLoaders,
            Strings.Resources_ModpackProjectsLoading,
            Strings.Resources_ModpackProjectsEmpty,
            Strings.Resources_ModpackProjectsLoadError,
            Strings.Resources_ModpackProjectsLoadingMore,
            Strings.Resources_ModpackProjectsNoMore,
            Strings.Resources_ModpackProjectsLoadMoreError,
            Strings.Resources_ModpackCurseForgeMissingApiKey,
            Strings.Resources_ModpackDetailsInfoSection,
            Strings.Resources_ModpackInstallTargetSection,
            Strings.Resources_ModpackInstallTargetLocal,
            Strings.Resources_ModpackInstallTargetsLoading,
            Strings.Resources_ModpackInstallTargetsLoadError,
            Strings.Resources_ModpackVersionsLoading,
            Strings.Resources_ModpackVersionsEmpty,
            Strings.Resources_ModpackVersionsEmptyLocal,
            Strings.Resources_ModpackVersionsFilterEmpty,
            Strings.Resources_ModpackVersionsLoadError,
            Strings.Resources_ModpackVersionsLoadingMore,
            Strings.Resources_ModpackVersionsNoMore,
            Strings.Resources_ModpackVersionsLoadMoreError,
            Strings.Resources_ModpackVersionsAllTitle,
            Strings.FilePicker_ModpackDownloadDirectoryTitle,
            Strings.Status_ModpackDownloading,
            Strings.Status_ModpackDownloadingFormat,
            Strings.Status_ModpackDownloadedFormat,
            Strings.Status_ModpackDownloadFailed,
            Strings.Status_ModpackImportedFormat,
            Strings.Status_ModpackImportFailed,
            Strings.Resources_ModpackDownloadFileExistsMessageFormat,
            [
                new("adventure", Strings.Resources_ModpackFilterTypeAdventure, ResourceProjectCategory.Adventure),
                new("technology", Strings.Resources_ModpackFilterTypeTechnology, ResourceProjectCategory.Technology),
                new("magic", Strings.Resources_ModpackFilterTypeMagic, ResourceProjectCategory.Magic),
                new("optimization", Strings.Resources_ModpackFilterTypeOptimization, ResourceProjectCategory.Optimization),
                new("quests", Strings.Resources_ModpackFilterTypeQuests, ResourceProjectCategory.Quests),
                new("kitchen-sink", Strings.Resources_ModpackFilterTypeKitchenSink, ResourceProjectCategory.KitchenSink),
                new("lightweight", Strings.Resources_ModpackFilterTypeLightweight, ResourceProjectCategory.Lightweight),
                new("multiplayer", Strings.Resources_ModpackFilterTypeMultiplayer, ResourceProjectCategory.Multiplayer),
                new("exploration", Strings.Resources_ModpackFilterTypeExploration, ResourceProjectCategory.Exploration)
            ],
            InstallTargetMode: ResourcesOnlineProjectInstallTargetMode.NewInstance,
            InstallTargetNewInstanceText: Strings.Resources_ModpackInstallTargetNewInstance,
            InstallTargetServerText: Strings.Resources_ModpackInstallTargetServer);
    }
}
