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

using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

/// <summary>
/// 协调版本列表、实例安装选项、安装状态和本地整合包导入四个下载页子流程。
/// </summary>
public sealed partial class DownloadPageViewModel : ObservableObject, IDisposable
{
    // 子 ViewModel 各自拥有业务状态，本类只维护页面步骤与跨子流程事件转发。
    private readonly IFloatingMessageService floatingMessageService;
    private readonly DownloadTasksPageViewModel downloadTasksPage;
    private readonly ILogger<DownloadPageViewModel> logger;
    private CancellationTokenSource? optionsNavigationCancellation;
    private string lastLocalImportDropHintMessage = string.Empty;

    [ObservableProperty]
    private DownloadPageStep currentStep = DownloadPageStep.VersionList;

    [ObservableProperty]
    private int contentRefreshToken;

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders)
        : this(
            gameVersionService,
            instanceService,
            downloadTasksPage,
            loaderProviders,
            ImmediateUiDispatcher.Instance,
            NullFloatingMessageService.Instance,
            NullInstanceFolderService.Instance,
            NullFilePickerService.Instance,
            NullLocalModpackImportService.Instance,
            RejectingExistingFilePathValidator.Instance)
    {
    }

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders,
        IUiDispatcher uiDispatcher)
        : this(
            gameVersionService,
            instanceService,
            downloadTasksPage,
            loaderProviders,
            uiDispatcher,
            NullFloatingMessageService.Instance,
            NullInstanceFolderService.Instance,
            NullFilePickerService.Instance,
            NullLocalModpackImportService.Instance,
            RejectingExistingFilePathValidator.Instance)
    {
    }

    public DownloadPageViewModel(
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        DownloadTasksPageViewModel downloadTasksPage,
        IEnumerable<ILoaderProvider> loaderProviders,
        IUiDispatcher uiDispatcher,
        IFloatingMessageService floatingMessageService,
        IInstanceFolderService instanceFolderService,
        IFilePickerService filePickerService,
        ILocalModpackImportService localModpackImportService,
        IExistingFilePathValidator existingFilePathValidator,
        IInstanceInstallNameAvailabilityService? instanceInstallNameAvailabilityService = null,
        IModrinthService? modrinthService = null,
        ILogger<DownloadLocalImportDialogViewModel>? localImportLogger = null,
        ILogger<DownloadInstallViewModel>? installLogger = null,
        ILogger<DownloadPageViewModel>? logger = null)
    {
        this.floatingMessageService = floatingMessageService;
        this.downloadTasksPage = downloadTasksPage;
        this.logger = logger ?? NullLogger<DownloadPageViewModel>.Instance;
        var instanceNameTracker = new DownloadInstanceNameTracker();
        VersionList = new DownloadVersionListViewModel(gameVersionService, uiDispatcher);
        InstanceOptions = new DownloadInstanceOptionsViewModel(
            instanceService,
            loaderProviders,
            instanceNameTracker,
            instanceInstallNameAvailabilityService,
            modrinthService,
            this.logger);
        InstallState = new DownloadInstallViewModel(
            instanceService,
            downloadTasksPage,
            instanceNameTracker,
            uiDispatcher,
            floatingMessageService,
            installLogger);
        ModpackManualDownloadsDialog = new DownloadModpackManualDownloadsDialogViewModel(
            instanceFolderService,
            floatingMessageService);
        LocalImportDialog = new DownloadLocalImportDialogViewModel(
            filePickerService,
            localModpackImportService,
            downloadTasksPage,
            uiDispatcher,
            floatingMessageService,
            ModpackManualDownloadsDialog,
            existingFilePathValidator,
            localImportLogger);

        VersionList.VersionSelected += VersionList_VersionSelected;
        VersionList.LocalImportRequested += VersionList_LocalImportRequested;
        VersionList.CategoryContentRefreshRequested += VersionList_CategoryContentRefreshRequested;
        VersionList.PropertyChanged += VersionList_PropertyChanged;
        InstanceOptions.InstallAvailabilityChanged += InstanceOptions_InstallAvailabilityChanged;
        InstallState.InstanceInstalled += InstallState_InstanceInstalled;
        InstallState.NameAvailabilityChanged += InstallState_NameAvailabilityChanged;
        LocalImportDialog.ModpackImported += LocalImportDialog_ModpackImported;
    }

    public event EventHandler<GameInstance>? InstanceInstalled;

    public DownloadVersionListViewModel VersionList { get; }

    public DownloadInstanceOptionsViewModel InstanceOptions { get; }

    public DownloadInstallViewModel InstallState { get; }

    public DownloadModpackManualDownloadsDialogViewModel ModpackManualDownloadsDialog { get; }

    public DownloadLocalImportDialogViewModel LocalImportDialog { get; }

    public bool IsVersionListStep => CurrentStep is DownloadPageStep.VersionList;

    public bool IsInstanceOptionsStep => CurrentStep is DownloadPageStep.InstanceOptions;

    public bool IsDownloadContentVisible => IsInstanceOptionsStep || VersionList.HasVisibleVersions;

    public bool CanInstallSelectedVersion => InstanceOptions.CanInstall;

    public string InstallButtonText => Strings.Download_InstallButton;

    public string PageTitle => IsInstanceOptionsStep
        ? VersionList.SelectedMinecraftVersion?.Name ?? string.Empty
        : VersionList.SelectedVersionCategory?.Title ?? string.Empty;

    public string? PageTitleIconSource => IsInstanceOptionsStep
        ? VersionList.SelectedMinecraftVersion?.IconSource
        : null;
}
