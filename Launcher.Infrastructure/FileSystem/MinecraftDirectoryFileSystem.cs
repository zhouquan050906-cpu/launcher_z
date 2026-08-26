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

using System.IO;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.FileSystem;

public sealed class MinecraftDirectoryFileSystem : IMinecraftDirectoryFileSystem
{
    public bool DirectoryExists(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            return Directory.Exists(MinecraftDirectoryPath.Normalize(directoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return false;
        }
    }

    public bool DirectoryIsAccessible(string directoryPath)
    {
        if (!DirectoryExists(directoryPath))
            return false;

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(
                    MinecraftDirectoryPath.Normalize(directoryPath))
                .GetEnumerator();
            _ = entries.MoveNext();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            return false;
        }
    }

    public string EnsureDirectoryExists(string directoryPath)
    {
        var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
        Directory.CreateDirectory(normalizedDirectory);
        return normalizedDirectory;
    }
}
