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
using Launcher.App.ViewModels.Resources;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Shell;

public sealed partial class MainViewModel
{
[RelayCommand]
    private async Task NavigateAsync(NavigationItem item)
    {
        if (NavigationCatalog.IsPage(item.Page, NavigationCatalog.MultiplayerPage)
            && !await TerracottaAgreementDialog.EnsureReadyAsync())
        {
            return;
        }

        SelectNavigationItem(item);
    }

    public void SelectNavigationItem(NavigationItem item)
    {
        // 主、次导航项共享一个选中事实，切换后同步两组稳定对象的选中标记。
        var targetPage = item.Loader is LoaderKind
            ? NavigationCatalog.DownloadPage
            : item.Page;

        if (item.Loader is LoaderKind loader)
        {
            GameManagement.SelectLoader(loader);
            CurrentPage = targetPage;
        }
        else
        {
            CurrentPage = targetPage;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    [RelayCommand]
    private async Task ToggleMenuAsync()
    {
        // 折叠状态只是 UI 偏好，先乐观翻转，保存失败再回滚，
        // 写盘异常绝不能顺着 AsyncRelayCommand 冒泡到 Dispatcher 终止进程。
        var previousValue = IsMenuExpanded;
        var nextValue = !previousValue;
        IsMenuExpanded = nextValue;
        Settings.IsMenuExpanded = nextValue;

        try
        {
            await settingsService.UpdateAsync(latest => latest.IsMenuExpanded = nextValue);
        }
        catch (Exception exception)
        {
            IsMenuExpanded = previousValue;
            Settings.IsMenuExpanded = previousValue;
            logger.LogWarning(
                exception,
                "Failed to save menu expansion preference. IsExpanded={IsExpanded}",
                nextValue);
            statusService.Report(Strings.Status_SettingsSaveFailed);
        }
    }

    [RelayCommand]
    private void MinimizeWindow()
    {
        windowService.Minimize();
    }

    [RelayCommand]
    private void CloseWindow()
    {
        if (CanCloseWindow())
            windowService.Close();
    }

    public bool CanCloseWindow()
    {
        // 活动下载存在时显示确认对话框并取消本次关闭，避免窗口退出中断写入。
        if (isCloseConfirmed || !DownloadTasksPage.HasRunningTasks)
            return true;

        logger.LogInformation(
            "Launcher close requested while downloads are running. RunningTaskCount={RunningTaskCount}",
            DownloadTasksPage.RunningTaskCount);
        IsDownloadCloseConfirmationDialogOpen = true;
        return false;
    }
}
