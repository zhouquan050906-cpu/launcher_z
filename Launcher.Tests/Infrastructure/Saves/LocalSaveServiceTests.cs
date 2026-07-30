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
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using System.Formats.Tar;
using System.IO.Compression;

namespace Launcher.Tests.Infrastructure.Saves;

public sealed class LocalSaveServiceTests
{
    [Fact]
    public async Task GetSavesAsyncLoadsTopLevelSaveDirectoriesAndReadsIcons()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var savesDirectory = Path.Combine(instanceDirectory, "saves");
            var withIconDirectory = Path.Combine(savesDirectory, "Cherry Grove");
            var withoutIconDirectory = Path.Combine(savesDirectory, "Starter Base");
            var nestedDirectory = Path.Combine(withIconDirectory, "region");
            Directory.CreateDirectory(withIconDirectory);
            Directory.CreateDirectory(withoutIconDirectory);
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllText(Path.Combine(withIconDirectory, "icon.png"), "not-a-real-png");

            var createdAt = new DateTimeOffset(2026, 1, 3, 10, 20, 30, TimeSpan.Zero);
            Directory.SetCreationTimeUtc(withIconDirectory, createdAt.UtcDateTime);

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Equal(2, saves.Count);
            Assert.DoesNotContain(saves, save => string.Equals(save.DirectoryName, "region", StringComparison.OrdinalIgnoreCase));

            var cherry = saves.Single(save => save.DirectoryName == "Cherry Grove");
            Assert.Equal("Cherry Grove", cherry.Name);
            Assert.Equal(withIconDirectory, cherry.FullPath);
            Assert.NotNull(cherry.IconSource);
            Assert.NotEqual(Path.Combine(withIconDirectory, "icon.png"), cherry.IconSource);
            Assert.True(File.Exists(new Uri(cherry.IconSource!, UriKind.Absolute).LocalPath));
            Assert.Equal(createdAt, cherry.CreatedAt);

            var starter = saves.Single(save => save.DirectoryName == "Starter Base");
            Assert.Null(starter.IconSource);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task GetSavesAsyncReturnsEmptyWhenSavesDirectoryIsMissing()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));

            var saves = await service.GetSavesAsync(CreateInstance(instanceDirectory));

            Assert.Empty(saves);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task GetSavesAsyncIgnoresWorldDataChangesAndReusesSnapshot()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var worldDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves", "World")).FullName;
            var regionDirectory = Directory.CreateDirectory(Path.Combine(worldDirectory, "region")).FullName;
            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var instance = CreateInstance(instanceDirectory);

            var first = Assert.Single(await service.GetSavesAsync(instance));
            await File.WriteAllTextAsync(Path.Combine(regionDirectory, "r.0.0.mca"), "changed");
            var second = Assert.Single(await service.GetSavesAsync(instance));

            Assert.Same(first, second);
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task DeleteAsyncRemovesSingleSaveDirectory()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var saveDirectory = Path.Combine(instanceDirectory, "saves", "Alpha Base");
            Directory.CreateDirectory(saveDirectory);

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            await service.DeleteAsync(new LocalSave
            {
                Name = "Alpha Base",
                DirectoryName = "Alpha Base",
                FullPath = saveDirectory,
                CreatedAt = DateTimeOffset.UtcNow
            });

            Assert.False(Directory.Exists(saveDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncReturnsInvalidWhenArchiveDoesNotContainLevelDat()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(instanceDirectory, "Broken Save.zip");
            CreateZipArchive(
                archivePath,
                rootDirectoryName: "Broken Save",
                ("Broken Save/region/r.0.0.mca", "region-data"));

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(LocalSaveImportFailureReason.InvalidMinecraftSaveArchive, result.FailureReason);
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "saves", "Broken Save")));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    [Fact]
    public async Task ImportFromArchiveAsyncBlocksPathTraversalEntries()
    {
        var instanceDirectory = CreateTempDirectory();
        var appDataDirectory = CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(instanceDirectory, "Unsafe.zip");
            CreateZipArchive(
                archivePath,
                rootDirectoryName: null,
                ("level.dat", "level"),
                ("../escape.txt", "escape"));
            var escapedPath = Path.Combine(instanceDirectory, "escape.txt");

            var service = new LocalSaveService(new LauncherPathProvider(appDataDirectory));
            var result = await service.ImportFromArchiveAsync(CreateInstance(instanceDirectory), archivePath);

            Assert.False(result.IsSuccess);
            Assert.Equal(LocalSaveImportFailureReason.UnexpectedError, result.FailureReason);
            Assert.False(File.Exists(escapedPath));
            Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "saves", "Unsafe")));
        }
        finally
        {
            DeleteDirectoryIfExists(instanceDirectory);
            DeleteDirectoryIfExists(appDataDirectory);
        }
    }

    private static GameInstance CreateInstance(string instanceDirectory)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Test Instance",
            MinecraftVersion = "1.21.4",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = instanceDirectory
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void CreateZipArchive(
        string archivePath,
        string? rootDirectoryName,
        params (string Path, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var entryPath = rootDirectoryName is null || entry.Path.StartsWith(rootDirectoryName, StringComparison.Ordinal)
                ? entry.Path
                : rootDirectoryName + "/" + entry.Path.TrimStart('/');
            var archiveEntry = archive.CreateEntry(entryPath);
            using var stream = archiveEntry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write(entry.Content);
        }
    }

}
