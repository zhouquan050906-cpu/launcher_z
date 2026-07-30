/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LocalResourceSnapshotTests : TestTempDirectory
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task ShaderPackSnapshotReusesUnchangedItems()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "shader-instance")).FullName;
        var shaderDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "shaderpacks")).FullName;
        var firstPath = Path.Combine(shaderDirectory, "first.zip");
        var secondPath = Path.Combine(shaderDirectory, "second.zip");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var service = new LocalShaderPackService();
        var instance = CreateInstance(instanceDirectory);

        var first = await service.GetShaderPacksAsync(instance);
        var unchanged = await service.GetShaderPacksAsync(instance);
        await File.AppendAllTextAsync(firstPath, "-changed");
        var changed = await service.GetShaderPacksAsync(instance);

        Assert.Same(first[0], unchanged[0]);
        Assert.Same(first[1], unchanged[1]);
        Assert.NotSame(first.Single(item => item.FileName == "first.zip"), changed.Single(item => item.FileName == "first.zip"));
        Assert.Same(first.Single(item => item.FileName == "second.zip"), changed.Single(item => item.FileName == "second.zip"));
    }

    [Fact]
    public async Task ResourcePackIconCacheIsReusedAcrossServiceInstances()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "resource-instance")).FullName;
        var resourceDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "resourcepacks")).FullName;
        var archivePath = Path.Combine(resourceDirectory, "cached.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var icon = archive.CreateEntry("pack.png");
            await using var stream = icon.Open();
            await stream.WriteAsync(OnePixelPng);
        }

        var paths = new LauncherPathProvider(TempRoot);
        var instance = CreateInstance(instanceDirectory);
        var first = Assert.Single(await new LocalResourcePackService(paths).GetResourcePacksAsync(instance));
        var originalLength = new FileInfo(archivePath).Length;
        var originalLastWrite = File.GetLastWriteTimeUtc(archivePath);
        await File.WriteAllBytesAsync(archivePath, new byte[checked((int)originalLength)]);
        File.SetLastWriteTimeUtc(archivePath, originalLastWrite);

        var second = Assert.Single(await new LocalResourcePackService(paths).GetResourcePacksAsync(instance));

        Assert.False(string.IsNullOrWhiteSpace(first.IconSource));
        Assert.Equal(first.IconSource, second.IconSource);
        Assert.True(File.Exists(new Uri(second.IconSource!, UriKind.Absolute).LocalPath));
    }

    [Fact]
    public async Task ModSnapshotsRetainOnlyFourMostRecentlyUsedInstances()
    {
        var instances = CreateArchiveBackedInstances("mod-lru", "mods", ".jar");
        var service = new ModService(new LauncherPathProvider(TempRoot));

        await AssertFourEntryLruAsync(instances, service.GetModsAsync);
    }

    [Fact]
    public async Task SaveSnapshotsRetainOnlyFourMostRecentlyUsedInstances()
    {
        var instances = Enumerable.Range(0, 5)
            .Select(index =>
            {
                var instanceDirectory = Directory.CreateDirectory(
                    Path.Combine(TempRoot, "save-lru", $"instance-{index}")).FullName;
                Directory.CreateDirectory(Path.Combine(instanceDirectory, "saves", $"world-{index}"));
                return CreateInstance(instanceDirectory);
            })
            .ToArray();
        var service = new LocalSaveService(new LauncherPathProvider(TempRoot));

        await AssertFourEntryLruAsync(instances, service.GetSavesAsync);
    }

    [Fact]
    public async Task ResourcePackSnapshotsRetainOnlyFourMostRecentlyUsedInstances()
    {
        var instances = CreateArchiveBackedInstances("resource-pack-lru", "resourcepacks", ".zip");
        var service = new LocalResourcePackService(new LauncherPathProvider(TempRoot));

        await AssertFourEntryLruAsync(instances, service.GetResourcePacksAsync);
    }

    [Fact]
    public async Task ShaderPackSnapshotsRetainOnlyFourMostRecentlyUsedInstances()
    {
        var instances = CreateArchiveBackedInstances("shader-pack-lru", "shaderpacks", ".zip");
        var service = new LocalShaderPackService();

        await AssertFourEntryLruAsync(instances, service.GetShaderPacksAsync);
    }

    [Fact]
    public async Task RemovingShaderPackDirectoryReleasesSnapshot()
    {
        var instanceDirectory = Directory.CreateDirectory(
            Path.Combine(TempRoot, "shader-directory-removal")).FullName;
        var shaderDirectory = Directory.CreateDirectory(
            Path.Combine(instanceDirectory, "shaderpacks")).FullName;
        var archivePath = Path.Combine(shaderDirectory, "same.zip");
        CreateEmptyArchive(archivePath);
        var originalWriteTime = File.GetLastWriteTimeUtc(archivePath);
        var service = new LocalShaderPackService();
        var instance = CreateInstance(instanceDirectory);
        var first = Assert.Single(await service.GetShaderPacksAsync(instance));

        Directory.Delete(shaderDirectory, recursive: true);
        Assert.Empty(await service.GetShaderPacksAsync(instance));
        Directory.CreateDirectory(shaderDirectory);
        CreateEmptyArchive(archivePath);
        File.SetLastWriteTimeUtc(archivePath, originalWriteTime);
        var recreated = Assert.Single(await service.GetShaderPacksAsync(instance));

        Assert.NotSame(first, recreated);
    }

    private IReadOnlyList<GameInstance> CreateArchiveBackedInstances(
        string rootName,
        string contentDirectoryName,
        string extension)
    {
        return Enumerable.Range(0, 5)
            .Select(index =>
            {
                var instanceDirectory = Directory.CreateDirectory(
                    Path.Combine(TempRoot, rootName, $"instance-{index}")).FullName;
                var contentDirectory = Directory.CreateDirectory(
                    Path.Combine(instanceDirectory, contentDirectoryName)).FullName;
                CreateEmptyArchive(Path.Combine(contentDirectory, $"item-{index}{extension}"));
                return CreateInstance(instanceDirectory);
            })
            .ToArray();
    }

    private static async Task AssertFourEntryLruAsync<T>(
        IReadOnlyList<GameInstance> instances,
        Func<GameInstance, CancellationToken, Task<IReadOnlyList<T>>> load)
        where T : class
    {
        var initial = new T[instances.Count];
        for (var index = 0; index < instances.Count; index++)
            initial[index] = Assert.Single(await load(instances[index], CancellationToken.None));

        for (var index = 1; index < instances.Count; index++)
        {
            var retained = Assert.Single(await load(instances[index], CancellationToken.None));
            Assert.Same(initial[index], retained);
        }

        var evicted = Assert.Single(await load(instances[0], CancellationToken.None));
        Assert.NotSame(initial[0], evicted);
    }

    private static void CreateEmptyArchive(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    }

    private static GameInstance CreateInstance(string directory) => new()
    {
        Id = Path.GetFileName(directory),
        InstanceDirectory = directory
    };
}
