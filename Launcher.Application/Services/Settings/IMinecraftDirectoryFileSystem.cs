/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public interface IMinecraftDirectoryFileSystem
{
    bool DirectoryExists(string directoryPath);

    bool DirectoryIsAccessible(string directoryPath);

    string EnsureDirectoryExists(string directoryPath);
}
