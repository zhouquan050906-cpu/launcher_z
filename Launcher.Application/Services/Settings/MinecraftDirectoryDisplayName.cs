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

public static class MinecraftDirectoryDisplayName
{
    public const int MaximumLength = 64;

    public static string Normalize(string? displayName)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > MaximumLength)
            throw new ArgumentException("The Minecraft directory display name must contain 1 to 64 characters.", nameof(displayName));

        return normalized;
    }

    public static string NormalizeOrDefault(string? displayName, string directoryPath)
    {
        try
        {
            return Normalize(displayName);
        }
        catch (ArgumentException)
        {
            return GetDefault(directoryPath);
        }
    }

    public static string GetDefault(string directoryPath)
    {
        var normalizedPath = MinecraftDirectoryPath.Normalize(directoryPath);
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath));
        return string.IsNullOrWhiteSpace(directoryName) ? normalizedPath : directoryName;
    }
}
