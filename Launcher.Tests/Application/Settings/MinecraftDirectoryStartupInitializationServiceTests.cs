/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Application.Settings;

public sealed class MinecraftDirectoryStartupInitializationServiceTests : TestTempDirectory
{
    [Fact]
    public void FirstRunCreatesRegistersAndSelectsLauncherDefault()
    {
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var officialDirectory = Path.Combine(TempRoot, "official", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem();
        var settings = new LauncherSettings
        {
            MinecraftDirectory = defaultDirectory,
            MinecraftDirectories = [defaultDirectory],
            ExcludedMinecraftDirectories = [defaultDirectory]
        };
        var managementService = new MinecraftDirectoryManagementService();
        var service = new MinecraftDirectoryStartupInitializationService(
            fileSystem,
            managementService);

        var initializedDirectory = service.InitializeDefaultDirectory(settings, defaultDirectory);
        managementService.RegisterDiscoveredDirectories(
            settings,
            [Official(officialDirectory), LauncherDefault(defaultDirectory)]);

        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), initializedDirectory);
        Assert.Equal(initializedDirectory, settings.MinecraftDirectory);
        Assert.Equal(initializedDirectory, fileSystem.EnsuredDirectory);
        Assert.Contains(settings.MinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, officialDirectory));
        Assert.DoesNotContain(settings.ExcludedMinecraftDirectories, directory =>
            MinecraftDirectoryPath.Equals(directory, defaultDirectory));
        Assert.Equal(".minecraft", settings.MinecraftDirectoryDisplayNames[initializedDirectory]);
    }

    [Fact]
    public void InaccessibleDirectoryAfterCreationFailsStartupInitialization()
    {
        var defaultDirectory = Path.Combine(TempRoot, "launcher", ".minecraft");
        var fileSystem = new FakeMinecraftDirectoryFileSystem { AccessibleAfterEnsure = false };
        var service = new MinecraftDirectoryStartupInitializationService(
            fileSystem,
            new MinecraftDirectoryManagementService());

        var exception = Assert.Throws<MinecraftDirectoryStartupRecoveryException>(() =>
            service.InitializeDefaultDirectory(
                new LauncherSettings { MinecraftDirectory = defaultDirectory },
                defaultDirectory));

        Assert.Equal(MinecraftDirectoryPath.Normalize(defaultDirectory), exception.DirectoryPath);
    }

    private sealed class FakeMinecraftDirectoryFileSystem : IMinecraftDirectoryFileSystem
    {
        public bool AccessibleAfterEnsure { get; init; } = true;

        public string? EnsuredDirectory { get; private set; }

        public bool DirectoryExists(string directoryPath) =>
            EnsuredDirectory is not null
            && MinecraftDirectoryPath.Equals(EnsuredDirectory, directoryPath);

        public bool DirectoryIsAccessible(string directoryPath) =>
            AccessibleAfterEnsure && DirectoryExists(directoryPath);

        public string EnsureDirectoryExists(string directoryPath)
        {
            EnsuredDirectory = MinecraftDirectoryPath.Normalize(directoryPath);
            return EnsuredDirectory;
        }
    }
}
