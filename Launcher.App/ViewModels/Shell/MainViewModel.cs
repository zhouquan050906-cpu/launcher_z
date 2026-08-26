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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;
using Launcher.App.ViewModels.Resources;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Shell;

/// <summary>
/// 管理主窗口导航、全局对话框和浮动消息，并通过会话协调器连接各功能页面。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    // Shell 只拥有跨页面 UI 状态，实例、下载、账户和设置业务仍由各自 ViewModel/Service 负责。
    private static readonly TimeSpan FloatingMessageDuration = TimeSpan.FromSeconds(2.2);

    private readonly ISettingsService settingsService;
    private readonly LauncherSessionCoordinator sessionCoordinator;
    private readonly IWindowService windowService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<MainViewModel> logger;
    private bool hasPrimedSettings;
    private bool hasInitialized;
    private bool isCloseConfirmed;
    private CancellationTokenSource? floatingMessageHideCancellation;
    private GameInstance? pendingJavaRequirementInstance;

    [ObservableProperty]
    private LauncherSettings settings = new();

    [ObservableProperty]
    private string currentPage = NavigationCatalog.HomePage;

    [ObservableProperty]
    private bool isMenuExpanded;

    [ObservableProperty]
    private string statusMessage = Strings.Status_Ready;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string floatingMessage = string.Empty;

    [ObservableProperty]
    private bool isFloatingMessageOpen;

    [ObservableProperty]
    private bool isJavaRequirementDialogOpen;

    [ObservableProperty]
    private string javaRequirementDialogTitle = Strings.Dialog_JavaRequirementNotMetTitle;

    [ObservableProperty]
    private string javaRequirementDialogMessage = string.Empty;

    [ObservableProperty]
    private bool isJavaRequirementForceLaunchAvailable;

    [ObservableProperty]
    private bool isDownloadCloseConfirmationDialogOpen;

    public MainViewModel(
        ISettingsService settingsService,
        LauncherSessionCoordinator sessionCoordinator,
        AccountPageViewModel accountPage,
        DownloadPageViewModel downloadPage,
        DownloadTasksPageViewModel downloadTasksPage,
        GameSettingsPageViewModel gameSettingsPage,
        MultiplayerPageViewModel multiplayerPage,
        ResourcesPageViewModel resourcesPage,
        SettingsPageViewModel settingsPage,
        GameManagementViewModel gameManagement,
        IWindowService windowService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IUiDispatcher uiDispatcher,
        IHomePageViewModelFactory homePageFactory,
        LaunchStatusDialogViewModel launchStatusDialog,
        UserAgreementDialogViewModel userAgreementDialog,
        MinecraftDirectoryStartupRecoveryDialogViewModel minecraftDirectoryStartupRecoveryDialog,
        TerracottaAgreementDialogViewModel terracottaAgreementDialog,
        LauncherBackgroundViewModel launcherBackground,
        ILogger<MainViewModel>? logger = null)
    {
        this.settingsService = settingsService;
        this.sessionCoordinator = sessionCoordinator;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<MainViewModel>.Instance;
        AccountPage = accountPage;
        DownloadPage = downloadPage;
        DownloadTasksPage = downloadTasksPage;
        GameSettingsPage = gameSettingsPage;
        MultiplayerPage = multiplayerPage;
        ResourcesPage = resourcesPage;
        SettingsPage = settingsPage;
        GameManagement = gameManagement;
        LaunchStatusDialog = launchStatusDialog;
        UserAgreementDialog = userAgreementDialog;
        MinecraftDirectoryStartupRecoveryDialog = minecraftDirectoryStartupRecoveryDialog;
        TerracottaAgreementDialog = terracottaAgreementDialog;
        LauncherBackground = launcherBackground;
        HomePage = homePageFactory.Create(
            AccountPage,
            percent => ProgressPercent = percent,
            sessionCoordinator.SelectLaunchInstanceAsync,
            sessionCoordinator.SetHomeLaunchMenuPinnedAsync,
            OpenGameSettingsForInstanceAsync);
        sessionCoordinator.Attach(HomePage);
        sessionCoordinator.NavigationRequested += SessionCoordinator_NavigationRequested;
        sessionCoordinator.ProgressChanged += progress => ProgressPercent = progress;
        HomePage.JavaRequirementNotMet += HomePage_JavaRequirementNotMet;
        HomePage.LaunchFailureReported += HomePage_LaunchFailureReported;
        GameSettingsPage.LocalImportRequested += GameSettingsPage_LocalImportRequested;
        GameSettingsPage.ResourceProjectDetailsRequested += GameSettingsPage_ResourceProjectDetailsRequested;
        GameSettingsPage.MinecraftDirectorySwitchRequested += GameSettingsPage_MinecraftDirectorySwitchRequested;

        statusService.MessageReported += message => StatusMessage = message;
        floatingMessageService.MessageRequested += ShowFloatingMessage;
        AccountPage.PropertyChanged += AccountPage_PropertyChanged;

        UpdateNavigationSelection();
    }

    public AccountPageViewModel AccountPage { get; }

    public HomePageViewModel HomePage { get; }

    public DownloadPageViewModel DownloadPage { get; }

    public DownloadTasksPageViewModel DownloadTasksPage { get; }

    public GameSettingsPageViewModel GameSettingsPage { get; }

    public MultiplayerPageViewModel MultiplayerPage { get; }

    public ResourcesPageViewModel ResourcesPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public GameManagementViewModel GameManagement { get; }

    public LaunchStatusDialogViewModel LaunchStatusDialog { get; }

    public UserAgreementDialogViewModel UserAgreementDialog { get; }

    public MinecraftDirectoryStartupRecoveryDialogViewModel MinecraftDirectoryStartupRecoveryDialog { get; }

    public TerracottaAgreementDialogViewModel TerracottaAgreementDialog { get; }

    public LauncherBackgroundViewModel LauncherBackground { get; }

    public NavigationItem DownloadTasksNavigationItem { get; } = NavigationCatalog.CreateDownloadTasksItem();

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new(NavigationCatalog.CreatePrimaryItems());

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];

    public async Task PrimeAsync(
        LauncherSettings? initialSettings = null,
        MinecraftDirectoryStartupRecoveryResult? minecraftDirectoryStartupRecovery = null)
    {
        // Prime 使用已加载设置建立首屏，完整异步初始化随后执行，缩短窗口首次可见时间。
        if (hasPrimedSettings)
            return;

        Settings = initialSettings ?? await settingsService.LoadAsync();
        MinecraftDirectoryStartupRecoveryDialog.Prime(minecraftDirectoryStartupRecovery);
        LauncherBackground.ApplyEffect(Settings.LauncherBackgroundEffect, reportFailure: false);
        UserAgreementDialog.Prime(Settings);
        IsMenuExpanded = Settings.IsMenuExpanded;
        AccountPage.PrimeFromSettings(Settings);
        await sessionCoordinator.PrimeAsync(Settings);
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        hasPrimedSettings = true;
    }

    public Task<bool> WaitForUserAgreementDecisionAsync() => UserAgreementDialog.WaitForDecisionAsync();

    [RelayCommand]
    public async Task InitializeAsync()
    {
        // SessionCoordinator 统一初始化共享状态，Shell 不自行重复加载实例或版本目录。
        await PrimeAsync();
        MinecraftDirectoryStartupRecoveryDialog.ShowPending();
        await AccountPage.InitializeAsync(Settings);
        await sessionCoordinator.InitializeAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
        UpdateAccountNavigationAvatar();
        hasInitialized = true;
    }

}
