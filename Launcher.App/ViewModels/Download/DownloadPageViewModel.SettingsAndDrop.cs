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

public sealed partial class DownloadPageViewModel
{
public void PrimeFromSettings(LauncherSettings settings)
    {
        // 下载源和限速同时影响在线安装与本地整合包依赖下载，必须传播给两个入口。
        ApplyDownloadSourcePreference(settings.DownloadSourcePreference);
        ApplyDownloadSpeedLimit(settings.DownloadSpeedLimitMbPerSecond);
        ApplyMinecraftDirectory(settings.MinecraftDirectory);
    }

    public void ApplyMinecraftDirectory(string? minecraftDirectory)
    {
        InstanceOptions.ApplyMinecraftDirectory(minecraftDirectory);
    }

    public void ApplyDownloadSourcePreference(DownloadSourcePreference preference)
    {
        LocalImportDialog.ApplyDownloadSourcePreference(preference);
        VersionList.ApplyDownloadSourcePreference(preference);
        InstanceOptions.ApplyDownloadSourcePreference(preference);
        if (IsInstanceOptionsStep && VersionList.SelectedMinecraftVersion is null)
            BackToVersionList();
    }

    public void ApplyDownloadSpeedLimit(int downloadSpeedLimitMbPerSecond)
    {
        var normalized = Math.Max(downloadSpeedLimitMbPerSecond, 0);
        LocalImportDialog.ApplyDownloadSpeedLimit(normalized);
        VersionList.ApplyDownloadSpeedLimit(normalized);
        InstanceOptions.ApplyDownloadSpeedLimit(normalized);
    }

    public bool CanHandleLocalImportDrop(IReadOnlyList<string> paths)
    {
        return CanHandleLocalImportDropCore(paths);
    }

    public bool UpdateLocalImportDropState(IReadOnlyList<string> paths)
    {
        // DragOver 只做轻量格式判断和提示，不在高频事件中打开压缩包或执行识别。
        var canAccept = CanHandleLocalImportDropCore(paths);
        floatingMessageService.ShowDragHint(this, canAccept
            ? Strings.GameSettings_DropReleaseToImportMessage
            : Strings.GameSettings_DropUnsupportedFileMessage);
        return canAccept;
    }

    public void ClearLocalImportDropState()
    {
        floatingMessageService.ClearDragHint(this);
    }

    public async Task<bool> HandleLocalImportDropAsync(IReadOnlyList<string> paths)
    {
        // Drop 后才进入实际识别；返回值表示是否接管文件，便于外层清除拖放视觉状态。
        if (!CanHandleLocalImportDropCore(paths))
            return false;
        try
        {
            return await LocalImportDialog.ImportDroppedFilesAsync(paths);
        }
        finally
        {
            ClearLocalImportDropState();
        }
    }

    public Task EnsureVersionsLoadedAsync(CancellationToken cancellationToken = default)
    {
        return VersionList.EnsureVersionsLoadedAsync(cancellationToken);
    }

    public void Dispose()
    {
        CancelOptionsNavigation();
        VersionList.VersionSelected -= VersionList_VersionSelected;
        VersionList.LocalImportRequested -= VersionList_LocalImportRequested;
        VersionList.CategoryContentRefreshRequested -= VersionList_CategoryContentRefreshRequested;
        VersionList.PropertyChanged -= VersionList_PropertyChanged;
        InstanceOptions.InstallAvailabilityChanged -= InstanceOptions_InstallAvailabilityChanged;
        InstallState.InstanceInstalled -= InstallState_InstanceInstalled;
        InstallState.NameAvailabilityChanged -= InstallState_NameAvailabilityChanged;
        LocalImportDialog.ModpackImported -= LocalImportDialog_ModpackImported;
        VersionList.Dispose();
        InstanceOptions.Dispose();
    }
}
