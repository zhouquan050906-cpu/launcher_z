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

public sealed class ResourcesResourcePacksPageViewModel : ResourcesModPageViewModel
{
    public ResourcesResourcePacksPageViewModel(
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
            CreateResourcePackOptions(),
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

    private static ResourcesOnlineProjectPageOptions CreateResourcePackOptions()
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.ResourcePack,
            Strings.Resources_SectionResourcePacks,
            "main_menu_library",
            ShowsLoaderFilters: false,
            Strings.Resources_ModFilterAllVersions,
            Strings.Resources_ResourcePackFilterAllLoaders,
            Strings.Resources_ResourcePackProjectsLoading,
            Strings.Resources_ResourcePackProjectsEmpty,
            Strings.Resources_ResourcePackProjectsLoadError,
            Strings.Resources_ResourcePackProjectsLoadingMore,
            Strings.Resources_ResourcePackProjectsNoMore,
            Strings.Resources_ResourcePackProjectsLoadMoreError,
            Strings.Resources_ResourcePackCurseForgeMissingApiKey,
            Strings.Resources_ResourcePackDetailsInfoSection,
            Strings.Resources_ResourcePackInstallTargetSection,
            Strings.Resources_ResourcePackInstallTargetLocal,
            Strings.Resources_ResourcePackInstallTargetsLoading,
            Strings.Resources_ResourcePackInstallTargetsLoadError,
            Strings.Resources_ResourcePackVersionsLoading,
            Strings.Resources_ResourcePackVersionsEmpty,
            Strings.Resources_ResourcePackVersionsEmptyLocal,
            Strings.Resources_ResourcePackVersionsFilterEmpty,
            Strings.Resources_ResourcePackVersionsLoadError,
            Strings.Resources_ResourcePackVersionsLoadingMore,
            Strings.Resources_ResourcePackVersionsNoMore,
            Strings.Resources_ResourcePackVersionsLoadMoreError,
            Strings.Resources_ResourcePackVersionsAllTitle,
            Strings.FilePicker_ResourcePackDownloadDirectoryTitle,
            Strings.Status_ResourcePackDownloading,
            Strings.Status_ResourcePackDownloadingFormat,
            Strings.Status_ResourcePackDownloadedFormat,
            Strings.Status_ResourcePackDownloadFailed,
            Strings.Status_ResourcePackInstalledFormat,
            Strings.Status_ResourcePackInstallFailed,
            Strings.Resources_ResourcePackDownloadFileExistsMessageFormat,
            [
                new("simplistic", Strings.Resources_ResourcePackFilterTypeSimplistic, ResourceProjectCategory.Simplistic),
                new("themed", Strings.Resources_ResourcePackFilterTypeThemed, ResourceProjectCategory.Themed),
                new("realistic", Strings.Resources_ResourcePackFilterTypeRealistic, ResourceProjectCategory.Realistic),
                new("vanilla-like", Strings.Resources_ResourcePackFilterTypeVanillaLike, ResourceProjectCategory.VanillaLike),
                new("audio", Strings.Resources_ResourcePackFilterTypeAudio, ResourceProjectCategory.Audio)
            ]);
    }
}
