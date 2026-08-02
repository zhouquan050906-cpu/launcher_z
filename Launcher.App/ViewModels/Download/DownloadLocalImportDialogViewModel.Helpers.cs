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

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Domain.Models;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadLocalImportDialogViewModel
{
private bool TryResolveSingleFile(IReadOnlyList<string> paths, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (paths.Count != 1)
            return false;

        return existingFilePathValidator.TryNormalize(paths[0], out resolvedPath);
    }

    private static IProgress<LauncherProgress> CreateProgressReporter(DownloadTaskItem importTask)
    {
        return importTask.CreateProgress(progress =>
        {
            importTask.Report(progress with { Message = LauncherProgressTextFormatter.Format(progress) });
        });
    }

    private static string MapFailureMessage(ModpackImportFailureReason failureReason)
    {
        return failureReason switch
        {
            ModpackImportFailureReason.FileNotFound
                or ModpackImportFailureReason.UnsupportedArchive
                or ModpackImportFailureReason.InvalidManifest
                => Strings.Status_ModpackInvalidArchive,
            ModpackImportFailureReason.UnsupportedLoader
                => Strings.Status_ModpackUnsupportedLoader,
            ModpackImportFailureReason.MissingCurseForgeApiKey
                => Strings.Status_ModpackMissingCurseForgeApiKey,
            ModpackImportFailureReason.HashMismatch
                => Strings.Status_ModpackHashMismatch,
            ModpackImportFailureReason.JavaRuntimeUnavailable
                => Strings.Status_JavaSelectionFailed,
            ModpackImportFailureReason.ArchiveTooLarge
                => Strings.Status_ModpackArchiveTooLarge,
            ModpackImportFailureReason.InsufficientDiskSpace
                => Strings.Status_ModpackInsufficientDiskSpace,
            _ => Strings.Status_ModpackImportFailed
        };
    }
}
