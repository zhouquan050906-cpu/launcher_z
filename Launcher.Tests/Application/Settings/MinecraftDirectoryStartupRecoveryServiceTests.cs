/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Settings;

public sealed class MinecraftDirectoryStartupRecoveryServiceTests : TestTempDirectory
{
    private readonly MinecraftDirectoryManagementService managementService = new();

    [Fact]
    public void AccessibleCurrentDirectoryDoesNotChangeSettings()
    {
        var current = Path.Combine(TempRoot, "current");
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem([current]);
        var settings = CreateSettings(current);
        var originalDirectories = settings.MinecraftDirectories.ToArray();
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, defaultDirectory);

        Assert.Null(result);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Equal(originalDirectories, settings.MinecraftDirectories);
        Assert.Empty(settings.MinecraftDirectoryDisplayNames);
        Assert.Null(fileSystem.EnsuredDirectory);
    }

    [Fact]
    public void InvalidCurrentDirectorySwitchesToFirstAccessibleListEntry()
    {
        var current = Path.Combine(TempRoot, "current");
        var unavailable = Path.Combine(TempRoot, "unavailable");
        var firstAvailable = Path.Combine(TempRoot, "first-available");
        var laterAvailable = Path.Combine(TempRoot, "later-available");
        var fileSystem = new FakeMinecraftDirectoryFileSystem([firstAvailable, laterAvailable]);
        var settings = CreateSettings(current, unavailable, firstAvailable, laterAvailable);
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, Path.Combine(TempRoot, "launcher", ".minecraft"));

        Assert.NotNull(result);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), result.InvalidDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(firstAvailable), result.SelectedDirectory);
        Assert.False(result.UsedDefaultDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(firstAvailable), settings.MinecraftDirectory);
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, current));
        Assert.Null(fileSystem.EnsuredDirectory);
    }

    [Fact]
    public void NewlyDiscoveredDirectoryCanBeSelectedAsFallback()
    {
        var current = Path.Combine(TempRoot, "current");
        var discovered = Path.Combine(TempRoot, "official", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem([discovered]);
        var settings = CreateSettings(current);
        managementService.RegisterDiscoveredDirectories(settings, [Official(discovered)]);
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, Path.Combine(TempRoot, "launcher", ".minecraft"));

        Assert.NotNull(result);
        Assert.Equal(MinecraftDirectoryPath.Normalize(discovered), settings.MinecraftDirectory);
        Assert.False(result.UsedDefaultDirectory);
    }

    [Fact]
    public void NoAccessibleEntryCreatesRegistersAndSelectsLauncherDefault()
    {
        var current = Path.Combine(TempRoot, "current");
        var unavailable = Path.Combine(TempRoot, "unavailable");
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem();
        var settings = CreateSettings(current, unavailable);
        settings.ExcludedMinecraftDirectories.Add(defaultDirectory);
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, defaultDirectory);

        Assert.NotNull(result);
        Assert.True(result.UsedDefaultDirectory);
        Assert.True(result.CreatedDefaultDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), settings.MinecraftDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), fileSystem.EnsuredDirectory);
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, current));
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, defaultDirectory));
        Assert.DoesNotContain(settings.ExcludedMinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, defaultDirectory));
        Assert.Equal(
            ".minecraft",
            settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(defaultDirectory)]);
    }

    [Fact]
    public void MissingCurrentLauncherDefaultIsRecreatedWithoutReportingARecovery()
    {
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem();
        var settings = CreateSettings(defaultDirectory);
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, defaultDirectory);

        // 目录只是还没建出来，补建后仍是同一个目录，不应该弹出"目录已失效"提示。
        Assert.Null(result);
        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), fileSystem.EnsuredDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), settings.MinecraftDirectory);
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, defaultDirectory));
    }

    [Fact]
    public void MissingCurrentDirectoryThatIsNotTheLauncherDefaultStillReportsARecovery()
    {
        var current = Path.Combine(TempRoot, "current");
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem();
        var settings = CreateSettings(current);
        var service = CreateService(fileSystem);

        var result = service.Recover(settings, defaultDirectory);

        Assert.NotNull(result);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), result.InvalidDirectory);
        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), result.SelectedDirectory);
        Assert.True(result.UsedDefaultDirectory);
        Assert.True(result.CreatedDefaultDirectory);
    }

    [Fact]
    public void DefaultDirectoryCreationFailureThrowsStartupRecoveryException()
    {
        var current = Path.Combine(TempRoot, "current");
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem
        {
            EnsureFailure = new UnauthorizedAccessException("Simulated access failure.")
        };
        var service = CreateService(fileSystem);

        var exception = Assert.Throws<MinecraftDirectoryStartupRecoveryException>(() =>
            service.Recover(CreateSettings(current), defaultDirectory));

        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), exception.DirectoryPath);
    }

    private MinecraftDirectoryStartupRecoveryService CreateService(
        IMinecraftDirectoryFileSystem fileSystem) =>
        new(fileSystem, managementService);

    private static LauncherSettings CreateSettings(string current, params string[] additional) => new()
    {
        MinecraftDirectory = MinecraftDirectoryPath.Normalize(current),
        MinecraftDirectories = [
            MinecraftDirectoryPath.Normalize(current),
            .. additional.Select(MinecraftDirectoryPath.Normalize)
        ]
    };

    private sealed class FakeMinecraftDirectoryFileSystem(
        IEnumerable<string>? initialAccessibleDirectories = null) : IMinecraftDirectoryFileSystem
    {
        private readonly HashSet<string> accessibleDirectories = new(
            (initialAccessibleDirectories ?? []).Select(MinecraftDirectoryPath.Normalize),
            MinecraftDirectoryPath.Comparer);

        public Exception? EnsureFailure { get; init; }

        public string? EnsuredDirectory { get; private set; }

        public bool DirectoryExists(string directoryPath) =>
            accessibleDirectories.Contains(MinecraftDirectoryPath.Normalize(directoryPath));

        public bool DirectoryIsAccessible(string directoryPath) =>
            accessibleDirectories.Contains(MinecraftDirectoryPath.Normalize(directoryPath));

        public string EnsureDirectoryExists(string directoryPath)
        {
            if (EnsureFailure is not null)
                throw EnsureFailure;

            EnsuredDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
            accessibleDirectories.Add(EnsuredDirectory);
            return EnsuredDirectory;
        }
    }
}
