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
    private void CancelDownloadCloseConfirmation()
    {
        logger.LogInformation("Launcher close canceled because downloads are running.");
        IsDownloadCloseConfirmationDialogOpen = false;
    }

    [RelayCommand]
    private void ConfirmDownloadClose()
    {
        logger.LogInformation(
            "Launcher close confirmed while downloads are running. RunningTaskCount={RunningTaskCount}",
            DownloadTasksPage.RunningTaskCount);
        isCloseConfirmed = true;
        IsDownloadCloseConfirmationDialogOpen = false;
        DownloadTasksPage.CancelAllRunningTasks();
        windowService.Close();
    }

    [RelayCommand]
    private void CloseJavaRequirementDialog()
    {
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;
    }

    [RelayCommand]
    private Task OpenJavaSettingsFromRequirementDialogAsync()
    {
        // 先关闭要求弹窗再导航，避免模态遮罩留在 Java 设置页上方。
        var targetInstance = pendingJavaRequirementInstance;
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;

        if (targetInstance?.JavaSettingsMode is LaunchSettingsMode.PerInstance)
        {
            GameSettingsPage.ShowInstanceDetails(targetInstance, "java");
            CurrentPage = NavigationCatalog.GameSettingsPage;
        }
        else
        {
            SettingsPage.ShowJavaSection();
            CurrentPage = NavigationCatalog.SettingsPage;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ForceLaunchFromJavaRequirementDialogAsync()
    {
        // 强制启动只绕过 Java 要求，仍由首页执行完整账户、实例和并发校验。
        var targetInstance = pendingJavaRequirementInstance;
        IsJavaRequirementDialogOpen = false;
        IsJavaRequirementForceLaunchAvailable = false;
        pendingJavaRequirementInstance = null;

        if (targetInstance is null)
            return;

        await HomePage.ForceLaunchIgnoringJavaRequirementAsync(targetInstance);
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
        ObserveShellTask(ActivateCurrentPageAsync(), "activate current page");
    }
}
