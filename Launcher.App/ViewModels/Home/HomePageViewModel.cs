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
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Home;

/// <summary>
/// 管理首页启动入口、启动进度和启动失败交互；实例列表与固定菜单状态委托给子 ViewModel。
/// </summary>
public sealed partial class HomePageViewModel : ObservableObject
{
    // LaunchService 负责启动业务，本类只把领域进度和异常映射为 UI 状态、弹窗与窗口行为。
    private readonly ILaunchService launchService;
    private readonly AccountPageViewModel accountPage;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IWindowService windowService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly IAccountDialogService? accountDialogService;
    private readonly Action<double> reportProgressPercent;
    private readonly Func<GameInstance?, Task> openGameSettingsForInstance;
    private readonly ILogger<HomePageViewModel> logger;
    private LauncherSettings settings = new();
    // 一个首页只允许一个活动启动会话；CTS 的存在也作为取消按钮所需的会话身份。
    private CancellationTokenSource? launchCancellationTokenSource;
    private IDisposable? launchSpeedMeterLifetime;
    private IProgress<LauncherProgress>? launchProgress;

    [ObservableProperty]
    private bool isLaunching;

    [ObservableProperty]
    private string launchStatusMessage = string.Empty;

    [ObservableProperty]
    private double launchProgressPercent;

    [ObservableProperty]
    private string launchDownloadSpeedText = string.Empty;

    public HomePageViewModel(
        ILaunchService launchService,
        AccountPageViewModel accountPage,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService,
        IUiDispatcher uiDispatcher,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<bool, Task<bool>> setLaunchMenuPinned,
        Func<GameInstance?, Task> openGameSettingsForInstance,
        ILogger<HomePageViewModel>? logger = null,
        IAccountDialogService? accountDialogService = null)
    {
        this.launchService = launchService;
        this.accountPage = accountPage;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
        this.accountDialogService = accountDialogService;
        this.reportProgressPercent = reportProgressPercent;
        this.openGameSettingsForInstance = openGameSettingsForInstance;
        this.logger = logger ?? NullLogger<HomePageViewModel>.Instance;

        LaunchGames = new HomeLaunchGameListViewModel(
            statusService,
            selectLaunchInstance,
            setLaunchMenuPinned);
        LaunchGames.PropertyChanged += LaunchGames_PropertyChanged;

        accountPage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.SelectedAccount))
                NotifyAccountStateChanged();
        };
    }

    public HomeLaunchGameListViewModel LaunchGames { get; }

    public bool HasSelectedAccount => accountPage.SelectedAccount is not null;

    public GameInstance? SelectedInstance => LaunchGames.SelectedInstance;

    public bool CanLaunchSelectedGame => HasSelectedAccount && SelectedInstance is not null && !IsLaunching;

    public bool HasLaunchProgress => IsLaunching && !string.IsNullOrWhiteSpace(LaunchStatusMessage);

    public bool HasLaunchDownloadSpeedText => IsLaunching && !string.IsNullOrWhiteSpace(LaunchDownloadSpeedText);

    public ObservableCollection<HomeLaunchInstanceItem> LaunchInstances => LaunchGames.LaunchInstances;

    public bool HasLaunchInstances => LaunchGames.HasLaunchInstances;

    public bool HasNoLaunchInstances => LaunchGames.HasNoLaunchInstances;

    public HomeLaunchInstanceItem? SelectedLaunchInstanceItem => LaunchGames.SelectedLaunchInstanceItem;

    public bool HasSelectedLaunchInstance => LaunchGames.HasSelectedLaunchInstance;

    public bool IsLaunchMenuPinned => LaunchGames.IsLaunchMenuPinned;

    public IAsyncRelayCommand SelectLaunchInstanceCommand => LaunchGames.SelectLaunchInstanceCommand;

    public IAsyncRelayCommand ToggleLaunchMenuPinnedCommand => LaunchGames.ToggleLaunchMenuPinnedCommand;

    public bool CanOpenSelectedInstanceSettings => SelectedInstance is not null && !IsLaunching;

    public event EventHandler<JavaRequirementNotMetEventArgs>? JavaRequirementNotMet;

    public event EventHandler<LaunchFailureReport>? LaunchFailureReported;

    /// <summary>
    /// 启动准备阶段的开始与结束。游戏进程拉起后即视为结束，其生命周期由 GameLaunchSession 独立持有。
    /// </summary>
    public event EventHandler? LaunchActivityChanged;

    public string? HomeAvatarUrl
    {
        get
        {
            var account = accountPage.SelectedAccount;
            if (account is null)
                return null;

            if (!string.IsNullOrWhiteSpace(account.AvatarSource))
                return account.AvatarSource;

            if (!account.IsMicrosoft || string.IsNullOrWhiteSpace(account.Uuid))
                return "https://minotar.net/avatar/Steve/576.png";

            return $"https://crafatar.com/avatars/{account.Uuid}?size=576&overlay";
        }
    }

    public string HomeAccountDisplayName => accountPage.SelectedAccount?.DisplayName ?? Strings.Home_NoAccountSelected;

    public string HomeVersionDisplayName
    {
        get
        {
            if (SelectedInstance is null)
                return Strings.Home_NoVersionSelected;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.Name))
                return SelectedInstance.Name;

            if (!string.IsNullOrWhiteSpace(SelectedInstance.VersionName))
                return SelectedInstance.VersionName;

            return string.IsNullOrWhiteSpace(SelectedInstance.MinecraftVersion)
                ? Strings.Home_NoVersionSelected
                : SelectedInstance.MinecraftVersion;
        }
    }

    public void Initialize(LauncherSettings launcherSettings, GameInstance? instance)
    {
        // 初始化顺序先设置列表策略再选实例，使子 ViewModel 能按最新固定菜单偏好构造可见项。
        settings = launcherSettings;
        LaunchGames.SetLaunchMenuPinned(launcherSettings.IsHomeLaunchMenuPinned);
        SetSelectedInstance(instance);
        NotifyAccountStateChanged();
        NotifyInstanceStateChanged();
    }

    public void SetSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        LaunchGames.SetLaunchMenuPinned(launcherSettings.IsHomeLaunchMenuPinned);
    }

    public void SetSelectedInstance(GameInstance? instance)
    {
        LaunchGames.SetSelectedInstance(instance);
    }

    public void SetLaunchInstances(IEnumerable<GameInstance> instances)
    {
        LaunchGames.SetLaunchInstances(instances);
    }

    public bool ApplyInstanceCatalog(IEnumerable<GameInstance> instances, long catalogRevision)
    {
        return LaunchGames.ApplyInstanceCatalog(instances, catalogRevision);
    }

}
