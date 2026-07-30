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

using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed record DownloadInstallRequest(
    string MinecraftVersion,
    string MinecraftVersionType,
    string InstanceName,
    LoaderKind Loader,
    string? LoaderVersion,
    string? FabricApiVersionId,
    string? QuiltStandardLibraryVersionId,
    string LoaderDisplayName,
    DownloadSourcePreference DownloadSourcePreference,
    int DownloadSpeedLimitMbPerSecond);
