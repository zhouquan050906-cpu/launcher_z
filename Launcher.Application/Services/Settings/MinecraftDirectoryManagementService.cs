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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed class MinecraftDirectoryManagementService
{
    /// <param name="resolveDisplayName">
    /// 按来源解析登记时使用的显示名；返回 null 或无效名称时回退到目录名。
    /// 本地化文案位于表现层，因此由调用方注入。
    /// </param>
    public bool RegisterDiscoveredDirectories(
        LauncherSettings settings,
        IEnumerable<MinecraftDirectoryDiscovery> discoveredDirectories,
        Func<MinecraftDirectoryKind, string?>? resolveDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(discoveredDirectories);

        var changed = EnsureCurrentDirectoryRegistered(settings);
        var knownDirectories = new HashSet<string>(settings.MinecraftDirectories, MinecraftDirectoryPath.Comparer);
        var excludedDirectories = new HashSet<string>(
            settings.ExcludedMinecraftDirectories,
            MinecraftDirectoryPath.Comparer);
        foreach (var discovery in discoveredDirectories)
        {
            string normalizedDirectory;
            try
            {
                normalizedDirectory = MinecraftDirectoryPath.Normalize(discovery.DirectoryPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (excludedDirectories.Contains(normalizedDirectory)
                || !knownDirectories.Add(normalizedDirectory))
                continue;

            settings.MinecraftDirectories.Add(normalizedDirectory);
            SetDisplayName(
                settings,
                normalizedDirectory,
                MinecraftDirectoryDisplayName.NormalizeOrDefault(
                    resolveDisplayName?.Invoke(discovery.Kind),
                    normalizedDirectory));
            changed = true;
        }

        return changed;
    }

    public string AddAndSelectDirectory(
        LauncherSettings settings,
        string directoryPath,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
        EnsureCurrentDirectoryRegistered(settings);

        if (!settings.MinecraftDirectories.Contains(normalizedDirectory, MinecraftDirectoryPath.Comparer))
            settings.MinecraftDirectories.Add(normalizedDirectory);
        if (displayName is not null)
        {
            SetDisplayName(
                settings,
                normalizedDirectory,
                MinecraftDirectoryDisplayName.Normalize(displayName));
        }
        else if (!TryGetDisplayName(settings, normalizedDirectory, out var existingDisplayName)
                 || !IsValidDisplayName(existingDisplayName))
        {
            SetDisplayName(
                settings,
                normalizedDirectory,
                MinecraftDirectoryDisplayName.GetDefault(normalizedDirectory));
        }
        settings.ExcludedMinecraftDirectories.RemoveAll(directory =>
            MinecraftDirectoryPath.Equals(directory, normalizedDirectory));

        settings.MinecraftDirectory = normalizedDirectory;
        return normalizedDirectory;
    }

    public string RenameDirectory(
        LauncherSettings settings,
        string directoryPath,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
        EnsureCurrentDirectoryRegistered(settings);
        if (!settings.MinecraftDirectories.Contains(normalizedDirectory, MinecraftDirectoryPath.Comparer))
            throw new InvalidOperationException("The Minecraft directory is not registered.");

        SetDisplayName(settings, normalizedDirectory, MinecraftDirectoryDisplayName.Normalize(displayName));
        return normalizedDirectory;
    }

    public string SelectDirectory(LauncherSettings settings, string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
        EnsureCurrentDirectoryRegistered(settings);
        if (!settings.MinecraftDirectories.Contains(normalizedDirectory, MinecraftDirectoryPath.Comparer))
            throw new InvalidOperationException("The Minecraft directory is not registered.");

        settings.MinecraftDirectory = normalizedDirectory;
        return normalizedDirectory;
    }

    public bool RemoveDirectoryFromList(LauncherSettings settings, string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
        EnsureCurrentDirectoryRegistered(settings);
        if (MinecraftDirectoryPath.Equals(normalizedDirectory, settings.MinecraftDirectory))
            throw new InvalidOperationException("The current Minecraft directory cannot be removed from the list.");

        var removed = settings.MinecraftDirectories.RemoveAll(directory =>
            MinecraftDirectoryPath.Equals(directory, normalizedDirectory)) > 0;
        if (removed)
            RemoveDisplayName(settings, normalizedDirectory);
        if (removed
            && !settings.ExcludedMinecraftDirectories.Contains(
                normalizedDirectory,
                MinecraftDirectoryPath.Comparer))
        {
            settings.ExcludedMinecraftDirectories.Add(normalizedDirectory);
        }

        return removed;
    }

    public bool EnsureCurrentDirectoryRegistered(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedCurrent = MinecraftDirectoryPath.Normalize(settings.MinecraftDirectory);
        settings.MinecraftDirectory = normalizedCurrent;
        settings.MinecraftDirectories ??= [];
        settings.MinecraftDirectoryDisplayNames ??= [];
        settings.ExcludedMinecraftDirectories ??= [];
        settings.ExcludedMinecraftDirectories.RemoveAll(directory =>
            MinecraftDirectoryPath.Equals(directory, normalizedCurrent));
        var changed = false;
        if (!settings.MinecraftDirectories.Contains(normalizedCurrent, MinecraftDirectoryPath.Comparer))
        {
            settings.MinecraftDirectories.Insert(0, normalizedCurrent);
            changed = true;
        }

        foreach (var directory in settings.MinecraftDirectories)
        {
            if (TryGetDisplayName(settings, directory, out var displayName)
                && IsValidDisplayName(displayName))
                continue;

            SetDisplayName(settings, directory, MinecraftDirectoryDisplayName.GetDefault(directory));
            changed = true;
        }

        return changed;
    }

    private static bool IsValidDisplayName(string? displayName)
    {
        try
        {
            _ = MinecraftDirectoryDisplayName.Normalize(displayName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetDisplayName(
        LauncherSettings settings,
        string directoryPath,
        out string displayName)
    {
        foreach (var pair in settings.MinecraftDirectoryDisplayNames)
        {
            if (!MinecraftDirectoryPath.Equals(pair.Key, directoryPath))
                continue;

            displayName = pair.Value;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    private static void SetDisplayName(
        LauncherSettings settings,
        string directoryPath,
        string displayName)
    {
        RemoveDisplayName(settings, directoryPath);
        settings.MinecraftDirectoryDisplayNames[directoryPath] = displayName;
    }

    private static void RemoveDisplayName(LauncherSettings settings, string directoryPath)
    {
        // 字典的键相等性未必与路径语义一致（默认构造的 LauncherSettings 用的是序数比较器），
        // 因此同一个目录可能以 C:\MC 与 C:\mc 等多种写法并存，必须全部清掉而不是只删第一个。
        var matchingKeys = settings.MinecraftDirectoryDisplayNames.Keys
            .Where(key => MinecraftDirectoryPath.Equals(key, directoryPath))
            .ToList();
        foreach (var matchingKey in matchingKeys)
            settings.MinecraftDirectoryDisplayNames.Remove(matchingKey);
    }
}
