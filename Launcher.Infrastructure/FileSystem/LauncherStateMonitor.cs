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
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LauncherStateMonitor : ILauncherStateMonitor
{
    private readonly object watcherLock = new();
    private FileSystemWatcher? minecraftParentWatcher;
    private FileSystemWatcher? minecraftRootWatcher;
    private FileSystemWatcher? instanceDirectoryWatcher;
    private FileSystemWatcher? instanceMetadataWatcher;
    private string? watchedVersionsDirectory;

    public event EventHandler? StateChanged;

    public void Watch(LauncherSettings settings)
    {
        lock (watcherLock)
        {
            StopCore();

            var minecraftDirectory = Path.GetFullPath(settings.MinecraftDirectory);
            var minecraftParent = Path.GetDirectoryName(minecraftDirectory);
            var minecraftFolderName = Path.GetFileName(minecraftDirectory);
            if (!string.IsNullOrWhiteSpace(minecraftParent)
                && !string.IsNullOrWhiteSpace(minecraftFolderName))
            {
                minecraftParentWatcher = TryCreateStructureWatcher(
                    minecraftParent,
                    minecraftFolderName,
                    includeSubdirectories: false);
            }

            minecraftRootWatcher = TryCreateStructureWatcher(
                minecraftDirectory,
                "versions",
                includeSubdirectories: false);

            watchedVersionsDirectory = Path.Combine(minecraftDirectory, "versions");
            instanceDirectoryWatcher = TryCreateStructureWatcher(
                watchedVersionsDirectory,
                "*",
                includeSubdirectories: false);
            instanceMetadataWatcher = TryCreateMetadataWatcher(watchedVersionsDirectory);
        }
    }

    public void Stop()
    {
        lock (watcherLock)
            StopCore();
    }

    private void StopCore()
    {
        minecraftParentWatcher?.Dispose();
        minecraftRootWatcher?.Dispose();
        instanceDirectoryWatcher?.Dispose();
        instanceMetadataWatcher?.Dispose();
        minecraftParentWatcher = null;
        minecraftRootWatcher = null;
        instanceDirectoryWatcher = null;
        instanceMetadataWatcher = null;
        watchedVersionsDirectory = null;
    }

    public void Dispose()
    {
        Stop();
    }

    internal static bool IsRelevantMetadataPath(string versionsDirectory, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(versionsDirectory) || string.IsNullOrWhiteSpace(fullPath))
            return false;

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(
                Path.GetFullPath(versionsDirectory),
                Path.GetFullPath(fullPath));
        }
        catch (Exception exception) when (exception is ArgumentException
                                         or NotSupportedException
                                         or PathTooLongException)
        {
            return false;
        }

        if (relativePath is "." or ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2)
            return string.Equals(Path.GetExtension(segments[1]), ".json", StringComparison.OrdinalIgnoreCase);

        return segments.Length == 3
            && string.Equals(
                segments[1],
                LauncherApplicationIdentity.StorageDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[2], "instance-settings.json", StringComparison.OrdinalIgnoreCase);
    }

    private FileSystemWatcher? TryCreateStructureWatcher(
        string path,
        string filter,
        bool includeSubdirectories)
    {
        var watcher = TryCreateWatcher(
            path,
            filter,
            includeSubdirectories,
            NotifyFilters.DirectoryName);
        if (watcher is null)
            return null;

        watcher.Created += WatcherStateChanged;
        watcher.Deleted += WatcherStateChanged;
        watcher.Renamed += WatcherStateRenamed;
        watcher.Error += WatcherError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private FileSystemWatcher? TryCreateMetadataWatcher(string versionsDirectory)
    {
        var watcher = TryCreateWatcher(
            versionsDirectory,
            "*.json",
            includeSubdirectories: true,
            NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size);
        if (watcher is null)
            return null;

        watcher.Changed += MetadataWatcherStateChanged;
        watcher.Created += MetadataWatcherStateChanged;
        watcher.Deleted += MetadataWatcherStateChanged;
        watcher.Renamed += MetadataWatcherStateRenamed;
        watcher.Error += WatcherError;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static FileSystemWatcher? TryCreateWatcher(
        string path,
        string filter,
        bool includeSubdirectories,
        NotifyFilters notifyFilter)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            return new FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = notifyFilter
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void WatcherStateChanged(object sender, FileSystemEventArgs e)
    {
        if (IsCurrentWatcher(sender))
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WatcherStateRenamed(object sender, RenamedEventArgs e)
    {
        if (IsCurrentWatcher(sender))
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MetadataWatcherStateChanged(object sender, FileSystemEventArgs e)
    {
        if (ReferenceEquals(sender, instanceMetadataWatcher)
            && watchedVersionsDirectory is not null
            && IsRelevantMetadataPath(watchedVersionsDirectory, e.FullPath))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MetadataWatcherStateRenamed(object sender, RenamedEventArgs e)
    {
        if (!ReferenceEquals(sender, instanceMetadataWatcher)
            || watchedVersionsDirectory is null)
        {
            return;
        }

        if (IsRelevantMetadataPath(watchedVersionsDirectory, e.FullPath)
            || IsRelevantMetadataPath(watchedVersionsDirectory, e.OldFullPath))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WatcherError(object sender, ErrorEventArgs e)
    {
        if (IsCurrentWatcher(sender))
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsCurrentWatcher(object sender)
    {
        return ReferenceEquals(sender, minecraftParentWatcher)
            || ReferenceEquals(sender, minecraftRootWatcher)
            || ReferenceEquals(sender, instanceDirectoryWatcher)
            || ReferenceEquals(sender, instanceMetadataWatcher);
    }
}
