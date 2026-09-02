/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

/// <summary>
/// Checks whether the launcher's data directory supports the write-and-replace
/// operation used by its persistent stores.
/// </summary>
public interface ILauncherDataDirectoryProbe
{
    Task<bool> IsWritableAsync(
        string? directoryPath,
        CancellationToken cancellationToken = default);
}
