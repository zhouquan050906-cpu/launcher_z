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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectInstallViewModel
{
private void BeginUserFeedback(ResourcesModVersionItemViewModel item)
    {
        floatingMessageService?.Show(options.DownloadingText);
        var message = string.Format(options.DownloadingFormat, item.Title);
        reportStatus(message);
    }

    private void BeginSession(InstallOperationContext context, string subtitle)
    {
        // 会话开始统一清空上一轮错误、进度和弹窗状态，避免迟到反馈与新安装混合。
        context.Session = ResourceInstallTaskSession.Begin(
            downloadTasksPage,
            context.Item.Title,
            subtitle,
            options.DownloadingText);
    }

    private void PresentFailure(InstallOperationContext context, string message)
    {
        // 页面只展示稳定、本地化的失败原因，底层异常细节由业务服务写入日志。
        floatingMessageService?.Show(message);
        reportStatus(message);
        context.Session?.Fail(message);
    }

    private string MapModpackImportFailureMessage(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound
                or ModpackImportFailureReason.UnsupportedArchive
                or ModpackImportFailureReason.InvalidManifest => Strings.Status_ModpackInvalidArchive,
            ModpackImportFailureReason.UnsupportedLoader => Strings.Status_ModpackUnsupportedLoader,
            ModpackImportFailureReason.MissingCurseForgeApiKey => Strings.Status_ModpackMissingCurseForgeApiKey,
            ModpackImportFailureReason.HashMismatch => Strings.Status_ModpackHashMismatch,
            ModpackImportFailureReason.JavaRuntimeUnavailable => Strings.Status_JavaSelectionFailed,
            ModpackImportFailureReason.ArchiveTooLarge => Strings.Status_ModpackArchiveTooLarge,
            ModpackImportFailureReason.InsufficientDiskSpace => Strings.Status_ModpackInsufficientDiskSpace,
            _ => options.InstallFailedText
        };
    }

    private static string ResolveFileName(ResourcesModVersionItemViewModel item)
    {
        return string.IsNullOrWhiteSpace(item.Version.FileName) ? item.Title : item.Version.FileName;
    }

    private sealed class InstallOperationContext(
        ResourcesModVersionItemViewModel item,
        ResourcesModInstallTargetItemViewModel target,
        ResourcesModProjectItemViewModel? project)
    {
        public ResourcesModVersionItemViewModel Item { get; } = item;
        public ResourcesModInstallTargetItemViewModel Target { get; } = target;
        public ResourcesModProjectItemViewModel? Project { get; } = project;
        public string? ActiveInstallKey { get; set; }
        public ResourceInstallTaskSession? Session { get; set; }
    }
}
