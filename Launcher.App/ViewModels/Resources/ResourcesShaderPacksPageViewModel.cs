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

public sealed class ResourcesShaderPacksPageViewModel : ResourcesModPageViewModel
{
    public ResourcesShaderPacksPageViewModel(
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
            CreateShaderPackOptions(),
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

    private static ResourcesOnlineProjectPageOptions CreateShaderPackOptions()
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.ShaderPack,
            Strings.Resources_SectionShaderPacks,
            "instance_setting_page/shader",
            ShowsLoaderFilters: false,
            Strings.Resources_ModFilterAllVersions,
            Strings.Resources_ShaderPackFilterAllLoaders,
            Strings.Resources_ShaderPackProjectsLoading,
            Strings.Resources_ShaderPackProjectsEmpty,
            Strings.Resources_ShaderPackProjectsLoadError,
            Strings.Resources_ShaderPackProjectsLoadingMore,
            Strings.Resources_ShaderPackProjectsNoMore,
            Strings.Resources_ShaderPackProjectsLoadMoreError,
            Strings.Resources_ShaderPackCurseForgeMissingApiKey,
            Strings.Resources_ShaderPackDetailsInfoSection,
            Strings.Resources_ShaderPackInstallTargetSection,
            Strings.Resources_ShaderPackInstallTargetLocal,
            Strings.Resources_ShaderPackInstallTargetsLoading,
            Strings.Resources_ShaderPackInstallTargetsLoadError,
            Strings.Resources_ShaderPackVersionsLoading,
            Strings.Resources_ShaderPackVersionsEmpty,
            Strings.Resources_ShaderPackVersionsEmptyLocal,
            Strings.Resources_ShaderPackVersionsFilterEmpty,
            Strings.Resources_ShaderPackVersionsLoadError,
            Strings.Resources_ShaderPackVersionsLoadingMore,
            Strings.Resources_ShaderPackVersionsNoMore,
            Strings.Resources_ShaderPackVersionsLoadMoreError,
            Strings.Resources_ShaderPackVersionsAllTitle,
            Strings.FilePicker_ShaderPackDownloadDirectoryTitle,
            Strings.Status_ShaderPackDownloading,
            Strings.Status_ShaderPackDownloadingFormat,
            Strings.Status_ShaderPackDownloadedFormat,
            Strings.Status_ShaderPackDownloadFailed,
            Strings.Status_ShaderPackInstalledFormat,
            Strings.Status_ShaderPackInstallFailed,
            Strings.Resources_ShaderPackDownloadFileExistsMessageFormat,
            [
                new("cartoon", Strings.Resources_ShaderPackFilterTypeCartoon, ResourceProjectCategory.Cartoon),
                new("cursed", Strings.Resources_ShaderPackFilterTypeCursed, ResourceProjectCategory.Cursed),
                new("fantasy", Strings.Resources_ShaderPackFilterTypeFantasy, ResourceProjectCategory.Fantasy),
                new("realistic", Strings.Resources_ShaderPackFilterTypeRealistic, ResourceProjectCategory.Realistic),
                new("semi-realistic", Strings.Resources_ShaderPackFilterTypeSemiRealistic, ResourceProjectCategory.SemiRealistic),
                new("vanilla-like", Strings.Resources_ShaderPackFilterTypeVanillaLike, ResourceProjectCategory.VanillaLike)
            ]);
    }
}
