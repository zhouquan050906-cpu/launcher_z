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

using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Mods;

public sealed class ModServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ModServiceImportsDisablesAndEnablesJar()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "modded");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "example.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(sourceJar, "fake jar");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();

        Assert.False(disabled.IsEnabled);
        Assert.Equal("example.jar.disabled", disabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar.disabled")));

        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetEnabledAsyncRejectsExistingTargetWithoutChangingEitherFile(bool enabled)
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "conflict", "mods")).FullName;
        var enabledPath = Path.Combine(modsDirectory, "example.jar");
        var disabledPath = Path.Combine(modsDirectory, "example.jar.disabled");
        await File.WriteAllTextAsync(enabledPath, "enabled-content");
        await File.WriteAllTextAsync(disabledPath, "disabled-content");
        var sourcePath = enabled ? disabledPath : enabledPath;
        var targetPath = enabled ? enabledPath : disabledPath;
        var mod = CreateLocalMod(sourcePath, isEnabled: !enabled);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModEnabledStateConflictException>(
            () => service.SetEnabledAsync(mod, enabled));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("enabled-content", await File.ReadAllTextAsync(enabledPath));
        Assert.Equal("disabled-content", await File.ReadAllTextAsync(disabledPath));
    }

    [Fact]
    public async Task SetEnabledAsyncRejectsExistingTargetDirectoryWithoutChangingSource()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "directory-conflict", "mods")).FullName;
        var sourcePath = Path.Combine(modsDirectory, "example.jar.disabled");
        var targetPath = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(sourcePath, "source-content");
        Directory.CreateDirectory(targetPath);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ModEnabledStateConflictException>(
            () => service.SetEnabledAsync(CreateLocalMod(sourcePath, isEnabled: false), enabled: true));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("source-content", await File.ReadAllTextAsync(sourcePath));
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task MoveFileWithoutOverwriteMapsTargetCreatedAfterPrecheckToConflict()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "race-conflict", "mods")).FullName;
        var sourcePath = Path.Combine(modsDirectory, "example.jar.disabled");
        var targetPath = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(sourcePath, "source-content");
        await File.WriteAllTextAsync(targetPath, "target-content");

        var exception = Assert.Throws<ModEnabledStateConflictException>(
            () => ModService.MoveFileWithoutOverwrite(sourcePath, targetPath));

        Assert.Equal(targetPath, exception.TargetPath);
        Assert.Equal("source-content", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal("target-content", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task ModServiceImportAsyncOverwritesExistingJarWhenRequested()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "overwrite");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "replace-me.jar");
        await File.WriteAllTextAsync(sourceJar, "first");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        await service.ImportAsync(instance, sourceJar);
        await File.WriteAllTextAsync(sourceJar, "second");

        await service.ImportAsync(instance, sourceJar, overwriteExisting: true);

        var importedPath = Path.Combine(instanceDirectory, "mods", "replace-me.jar");
        Assert.Equal("second", await File.ReadAllTextAsync(importedPath));
        Assert.Single(await service.GetModsAsync(instance));
    }

    [Fact]
    public async Task GetModsAsyncDoesNotCreateMissingDirectory()
    {
        var instanceDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "missing-mods")).FullName;
        var service = CreateService();

        var mods = await service.GetModsAsync(new GameInstance { InstanceDirectory = instanceDirectory });

        Assert.Empty(mods);
        Assert.False(Directory.Exists(Path.Combine(instanceDirectory, "mods")));
    }

    [Fact]
    public async Task GetModsAsyncReusesUnchangedItemsAndReplacesOnlyChangedItem()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "snapshot", "mods")).FullName;
        var firstPath = Path.Combine(modsDirectory, "first.jar");
        var secondPath = Path.Combine(modsDirectory, "second.jar");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var instance = new GameInstance { InstanceDirectory = Directory.GetParent(modsDirectory)!.FullName };
        var service = CreateService();

        var initial = await service.GetModsAsync(instance);
        var unchanged = await service.GetModsAsync(instance);
        await File.AppendAllTextAsync(firstPath, "-changed");
        var changed = await service.GetModsAsync(instance);

        Assert.Same(initial[0], unchanged[0]);
        Assert.Same(initial[1], unchanged[1]);
        Assert.NotSame(initial.Single(item => item.FileName == "first.jar"), changed.Single(item => item.FileName == "first.jar"));
        Assert.Same(initial.Single(item => item.FileName == "second.jar"), changed.Single(item => item.FileName == "second.jar"));
    }

    [Fact]
    public async Task SetEnabledAsyncMovesExistingSnapshotWithoutReplacingItem()
    {
        var modsDirectory = Directory.CreateDirectory(
            Path.Combine(TempRoot, "instances", "toggle-snapshot", "mods")).FullName;
        var enabledPath = Path.Combine(modsDirectory, "example.jar");
        await File.WriteAllTextAsync(enabledPath, "content");
        var instance = new GameInstance
        {
            InstanceDirectory = Directory.GetParent(modsDirectory)!.FullName
        };
        var service = CreateService();
        var loaded = Assert.Single(await service.GetModsAsync(instance));

        await service.SetEnabledAsync(loaded, enabled: false);
        var afterToggle = Assert.Single(await service.GetModsAsync(instance));

        Assert.Same(loaded, afterToggle);
        Assert.False(afterToggle.IsEnabled);
        Assert.Equal("example.jar.disabled", afterToggle.FileName);
        Assert.False(File.Exists(enabledPath));
        Assert.True(File.Exists(Path.Combine(modsDirectory, "example.jar.disabled")));
    }

    [Fact]
    public async Task SetEnabledAsyncRemainsConsistentAfterSnapshotEviction()
    {
        var instances = new List<GameInstance>();
        for (var index = 0; index < 5; index++)
        {
            var instanceDirectory = Directory.CreateDirectory(
                Path.Combine(TempRoot, "instances", "toggle-after-eviction", $"instance-{index}")).FullName;
            var modsDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "mods")).FullName;
            var jarPath = Path.Combine(modsDirectory, $"mod-{index}.jar");
            using (ZipFile.Open(jarPath, ZipArchiveMode.Create))
            {
            }
            instances.Add(new GameInstance { Id = $"instance-{index}", InstanceDirectory = instanceDirectory });
        }

        var service = CreateService();
        var first = Assert.Single(await service.GetModsAsync(instances[0]));
        for (var index = 1; index < instances.Count; index++)
            Assert.Single(await service.GetModsAsync(instances[index]));

        await service.SetEnabledAsync(first, enabled: false);
        var reloaded = Assert.Single(await service.GetModsAsync(instances[0]));

        Assert.NotSame(first, reloaded);
        Assert.False(first.IsEnabled);
        Assert.False(reloaded.IsEnabled);
        Assert.Equal("mod-0.jar.disabled", reloaded.FileName);
        Assert.True(File.Exists(Path.Combine(instances[0].InstanceDirectory, "mods", "mod-0.jar.disabled")));
    }

    [Fact]
    public async Task ConcurrentInstanceLoadsRemainConsistentAcrossEviction()
    {
        var instances = new List<GameInstance>();
        for (var index = 0; index < 8; index++)
        {
            var instanceDirectory = Directory.CreateDirectory(
                Path.Combine(TempRoot, "instances", "concurrent-lru", $"instance-{index}")).FullName;
            var modsDirectory = Directory.CreateDirectory(Path.Combine(instanceDirectory, "mods")).FullName;
            using (ZipFile.Open(Path.Combine(modsDirectory, $"mod-{index}.jar"), ZipArchiveMode.Create))
            {
            }
            instances.Add(new GameInstance { Id = $"instance-{index}", InstanceDirectory = instanceDirectory });
        }

        var service = CreateService();
        var results = await Task.WhenAll(instances.Select(instance => service.GetModsAsync(instance)));

        Assert.All(results, result => Assert.Single(result));
        for (var index = 0; index < instances.Count; index++)
        {
            var reloaded = Assert.Single(await service.GetModsAsync(instances[index]));
            Assert.Equal($"mod-{index}.jar", reloaded.FileName);
        }
    }

    [Fact]
    public async Task RemoteIconFileAliasIsStableAcrossEnabledStateRename()
    {
        var modsDirectory = Directory.CreateDirectory(
            Path.Combine(TempRoot, "instances", "icon-alias", "mods")).FullName;
        var enabledPath = Path.Combine(modsDirectory, "example.jar");
        var disabledPath = enabledPath + ".disabled";
        await File.WriteAllTextAsync(enabledPath, "content");
        var originalWriteTime = File.GetLastWriteTimeUtc(enabledPath);
        var enabledAlias = LocalModIconEnrichmentService.TryCreateFileAlias(enabledPath);

        File.Move(enabledPath, disabledPath);
        File.SetLastWriteTimeUtc(disabledPath, originalWriteTime);
        var disabledAlias = LocalModIconEnrichmentService.TryCreateFileAlias(disabledPath);

        Assert.NotNull(enabledAlias);
        Assert.Equal(enabledAlias, disabledAlias);
    }

    [Fact]
    public async Task MetadataCacheIsReusedAcrossServiceInstances()
    {
        var modsDirectory = Directory.CreateDirectory(Path.Combine(TempRoot, "instances", "persistent-cache", "mods")).FullName;
        var jarPath = Path.Combine(modsDirectory, "cached.jar");
        using (var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("fabric.mod.json");
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"id":"cached_mod","version":"1.0.0","name":"Cached Display Name"}"""));
        }

        var instance = new GameInstance { InstanceDirectory = Directory.GetParent(modsDirectory)!.FullName };
        var firstService = CreateService();
        var first = Assert.Single(await firstService.GetModsAsync(instance));
        var originalLength = new FileInfo(jarPath).Length;
        var originalLastWrite = File.GetLastWriteTimeUtc(jarPath);
        await File.WriteAllBytesAsync(jarPath, new byte[checked((int)originalLength)]);
        File.SetLastWriteTimeUtc(jarPath, originalLastWrite);

        var second = Assert.Single(await CreateService().GetModsAsync(instance));

        Assert.Equal("Cached Display Name", first.Name);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.ModId, second.ModId);
        Assert.Equal(first.Version, second.Version);
    }

    private ModService CreateService()
    {
        return new ModService(new LauncherPathProvider(TempRoot));
    }

    private static LocalMod CreateLocalMod(string fullPath, bool isEnabled) => new()
    {
        Name = "Example",
        FileName = Path.GetFileName(fullPath),
        FullPath = fullPath,
        IsEnabled = isEnabled
    };

    private static (string EntryName, byte[] Content) TextEntry(string entryName, string content)
    {
        return (entryName, Encoding.UTF8.GetBytes(content));
    }

}
