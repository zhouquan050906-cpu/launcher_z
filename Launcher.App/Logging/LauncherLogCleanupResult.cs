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

namespace Launcher.App.Logging;

/// <summary>Outcome of a manual launcher log cleanup.</summary>
/// <param name="DeletedFileCount">Log files removed from the directory.</param>
/// <param name="RetainedFileCount">Log files that could not be removed, such as the log the running launcher holds open.</param>
internal readonly record struct LauncherLogCleanupResult(int DeletedFileCount, int RetainedFileCount)
{
    public static LauncherLogCleanupResult Empty => new(0, 0);
}
