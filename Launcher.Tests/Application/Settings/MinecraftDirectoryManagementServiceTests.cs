/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Settings;

public sealed class MinecraftDirectoryManagementServiceTests : TestTempDirectory
{
    private readonly MinecraftDirectoryManagementService service = new();

    [Fact]
    public void RegisterDiscoveredDirectoriesAppendsNewPathsWithoutChangingCurrentDirectory()
    {
        var current = Path.Combine(TempRoot, "current");
        var discovered = Path.Combine(TempRoot, "official");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current]
        };

        var changed = service.RegisterDiscoveredDirectories(
            settings,
            [discovered, discovered + Path.DirectorySeparatorChar]);

        Assert.True(changed);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Equal(
            [MinecraftDirectoryPath.Normalize(current), MinecraftDirectoryPath.Normalize(discovered)],
            settings.MinecraftDirectories);
        Assert.Equal("current", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(current)]);
        Assert.Equal("official", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(discovered)]);
    }

    [Fact]
    public void EnsureCurrentDirectoryRegisteredMigratesLegacySingleDirectorySettings()
    {
        var current = Path.Combine(TempRoot, "legacy");
        var settings = new LauncherSettings { MinecraftDirectory = current };

        var changed = service.EnsureCurrentDirectoryRegistered(settings);

        Assert.True(changed);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), Assert.Single(settings.MinecraftDirectories));
        Assert.Equal("legacy", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(current)]);
    }

    [Fact]
    public void AddAndSelectDirectoryDoesNotDuplicateAnExistingPath()
    {
        var current = Path.Combine(TempRoot, "current");
        var target = Path.Combine(TempRoot, "target");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current, target]
        };

        service.AddAndSelectDirectory(settings, target + Path.DirectorySeparatorChar, " Target Name ");

        Assert.Equal(MinecraftDirectoryPath.Normalize(target), settings.MinecraftDirectory);
        Assert.Equal(2, settings.MinecraftDirectories.Count);
        Assert.Equal("Target Name", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(target)]);
    }

    [Fact]
    public void RemoveDirectoryFromListKeepsFilesAndExcludesFutureDiscovery()
    {
        var current = Directory.CreateDirectory(Path.Combine(TempRoot, "current")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(TempRoot, "target")).FullName;
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current, target]
        };

        var removed = service.RemoveDirectoryFromList(settings, target);
        var discoveredChanged = service.RegisterDiscoveredDirectories(settings, [target]);

        Assert.True(removed);
        Assert.False(discoveredChanged);
        Assert.True(Directory.Exists(target));
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), Assert.Single(settings.MinecraftDirectories));
        Assert.Equal(MinecraftDirectoryPath.Normalize(target), Assert.Single(settings.ExcludedMinecraftDirectories));
        Assert.DoesNotContain(settings.MinecraftDirectoryDisplayNames, pair =>
            MinecraftDirectoryPath.Equals(pair.Key, target));
    }

    [Fact]
    public void AddAndSelectDirectoryClearsPreviousDiscoveryExclusion()
    {
        var current = Path.Combine(TempRoot, "current");
        var target = Path.Combine(TempRoot, "target");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current],
            ExcludedMinecraftDirectories = [target]
        };

        service.AddAndSelectDirectory(settings, target);

        Assert.Equal(MinecraftDirectoryPath.Normalize(target), settings.MinecraftDirectory);
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, target));
        Assert.Empty(settings.ExcludedMinecraftDirectories);
    }

    [Fact]
    public void CurrentDirectoryCannotBeRemovedFromList()
    {
        var current = Path.Combine(TempRoot, "current");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current]
        };

        Assert.Throws<InvalidOperationException>(() =>
            service.RemoveDirectoryFromList(settings, current));
        Assert.Single(settings.MinecraftDirectories);
        Assert.Empty(settings.ExcludedMinecraftDirectories);
    }

    [Fact]
    public void RenameDirectoryChangesOnlyDisplayNameAndAllowsDuplicateNames()
    {
        var current = Path.Combine(TempRoot, "current");
        var target = Path.Combine(TempRoot, "target");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current, target]
        };

        service.RenameDirectory(settings, current, " Shared ");
        service.RenameDirectory(settings, target, "Shared");

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Equal(2, settings.MinecraftDirectories.Count);
        Assert.All(settings.MinecraftDirectoryDisplayNames.Values, name => Assert.Equal("Shared", name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameDirectoryRejectsBlankDisplayName(string displayName)
    {
        var current = Path.Combine(TempRoot, "current");
        var settings = new LauncherSettings
        {
            MinecraftDirectory = current,
            MinecraftDirectories = [current]
        };

        Assert.Throws<ArgumentException>(() =>
            service.RenameDirectory(settings, current, displayName));
    }
}
