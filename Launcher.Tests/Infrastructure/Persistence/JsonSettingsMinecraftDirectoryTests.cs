/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class JsonSettingsMinecraftDirectoryTests : TestTempDirectory
{
    [Fact]
    public async Task LoadingLegacySettingsRegistersTheExistingCurrentDirectory()
    {
        Directory.CreateDirectory(TempRoot);
        var current = Path.Combine(TempRoot, "legacy");
        await File.WriteAllTextAsync(
            Path.Combine(TempRoot, "settings.json"),
            JsonSerializer.Serialize(new { MinecraftDirectory = current }));
        var service = new JsonSettingsService(TempRoot);

        var settings = await service.LoadAsync();

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Equal(settings.MinecraftDirectory, Assert.Single(settings.MinecraftDirectories));
        Assert.Equal("legacy", settings.MinecraftDirectoryDisplayNames[settings.MinecraftDirectory]);
    }

    [Fact]
    public async Task UnchangedDirectoryListDoesNotOverwriteConcurrentListUpdate()
    {
        var service = new JsonSettingsService(TempRoot);
        var first = await service.LoadAsync();
        var second = await service.LoadAsync();
        var additional = Path.Combine(TempRoot, "additional");

        first.MinecraftDirectories.Add(additional);
        await service.SaveAsync(first);
        second.Theme = "Light";
        await service.SaveAsync(second);

        var reloaded = await service.LoadAsync();
        Assert.Contains(
            reloaded.MinecraftDirectories,
            directory => MinecraftDirectoryPath.Equals(directory, additional));
        Assert.Equal("Light", reloaded.Theme);
    }

    [Fact]
    public async Task MissingDirectoriesRemainPersistedAndDuplicatePathsAreCollapsed()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        var missing = Path.Combine(TempRoot, "missing");
        settings.MinecraftDirectories = [
            settings.MinecraftDirectory,
            missing,
            missing + Path.DirectorySeparatorChar
        ];

        await service.SaveAsync(settings);
        var reloaded = await service.LoadAsync();

        Assert.Equal(2, reloaded.MinecraftDirectories.Count);
        Assert.Contains(
            reloaded.MinecraftDirectories,
            directory => MinecraftDirectoryPath.Equals(directory, missing));
    }

    [Fact]
    public async Task ExcludedDirectoriesRemainPersistedNormalizedAndOutsideActiveList()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        var excluded = Path.Combine(TempRoot, "excluded");
        settings.ExcludedMinecraftDirectories = [
            excluded,
            excluded + Path.DirectorySeparatorChar,
            settings.MinecraftDirectory
        ];

        await service.SaveAsync(settings);
        var reloaded = await service.LoadAsync();

        Assert.Equal(
            MinecraftDirectoryPath.Normalize(excluded),
            Assert.Single(reloaded.ExcludedMinecraftDirectories));
        Assert.DoesNotContain(reloaded.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, excluded));
    }

    [Fact]
    public async Task DisplayNamesRoundTripTrimAndDiscardStaleEntries()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        var additional = Path.Combine(TempRoot, "additional");
        var stale = Path.Combine(TempRoot, "stale");
        settings.MinecraftDirectories.Add(additional);
        settings.MinecraftDirectoryDisplayNames = new Dictionary<string, string>
        {
            [settings.MinecraftDirectory] = " Current ",
            [additional + Path.DirectorySeparatorChar] = " Additional ",
            [stale] = "Stale"
        };

        await service.SaveAsync(settings);
        var reloaded = await service.LoadAsync();

        Assert.Equal("Current", reloaded.MinecraftDirectoryDisplayNames[settings.MinecraftDirectory]);
        Assert.Equal("Additional", reloaded.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(additional)]);
        Assert.Equal(2, reloaded.MinecraftDirectoryDisplayNames.Count);
    }

    [Fact]
    public async Task UnchangedDisplayNamesDoNotOverwriteConcurrentRename()
    {
        var service = new JsonSettingsService(TempRoot);
        var first = await service.LoadAsync();
        var second = await service.LoadAsync();

        first.MinecraftDirectoryDisplayNames[first.MinecraftDirectory] = "Renamed";
        await service.SaveAsync(first);
        second.Theme = "Light";
        await service.SaveAsync(second);

        var reloaded = await service.LoadAsync();
        Assert.Equal("Renamed", reloaded.MinecraftDirectoryDisplayNames[reloaded.MinecraftDirectory]);
        Assert.Equal("Light", reloaded.Theme);
    }
}
