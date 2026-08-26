/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;

namespace Launcher.Infrastructure.Persistence;

internal static class InstanceInstallNameOccupancy
{
    public static bool IsOccupied(
        string versionsDirectory,
        string finalDirectory,
        string logicalVersionName) =>
        Directory.Exists(finalDirectory)
        || File.Exists(finalDirectory)
        || PendingInstanceInstallDirectory.IsLogicalNameReserved(
            versionsDirectory,
            logicalVersionName);
}
