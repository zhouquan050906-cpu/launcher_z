/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class InstanceInstallNameAvailabilityServiceTests : TestTempDirectory
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingDirectoryOrFileOccupiesTheInstallName(bool createDirectory)
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var target = Path.Combine(minecraftDirectory, "versions", "occupied");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (createDirectory)
            Directory.CreateDirectory(target);
        else
            await File.WriteAllTextAsync(target, "occupied");
        var service = CreateService(minecraftDirectory, new GameInstallCoordinator());

        var result = await service.CheckAsync("occupied");

        Assert.Equal(InstanceInstallNameAvailability.Occupied, result);
    }

    [Fact]
    public async Task PendingInstallTransactionOccupiesItsLogicalName()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var transactionService = new InstanceInstallTransactionService();
        await using var transaction = await transactionService.BeginAsync(
            minecraftDirectory,
            "pending",
            "instance",
            "game",
            initializeDefaultIfEmpty: false);
        var service = CreateService(minecraftDirectory, new GameInstallCoordinator());

        var result = await service.CheckAsync("pending");

        Assert.Equal(InstanceInstallNameAvailability.Occupied, result);
    }

    [Fact]
    public async Task InProcessInstallLeaseOccupiesItsLogicalName()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var coordinator = new GameInstallCoordinator();
        await using var lease = await coordinator.AcquireInstallAsync(
            minecraftDirectory,
            "installing",
            progress: null);
        var service = CreateService(minecraftDirectory, coordinator);

        var result = await service.CheckAsync("installing");

        Assert.Equal(InstanceInstallNameAvailability.Occupied, result);
    }

    [Fact]
    public async Task CheckUsesTheLatestSelectedMinecraftDirectory()
    {
        var firstDirectory = Path.Combine(TempRoot, "first", ".minecraft");
        var secondDirectory = Path.Combine(TempRoot, "second", ".minecraft");
        Directory.CreateDirectory(Path.Combine(secondDirectory, "versions", "target"));
        var settings = new LauncherSettings { MinecraftDirectory = firstDirectory };
        var settingsService = new TestSettingsService(settings);
        var service = new InstanceInstallNameAvailabilityService(
            settingsService,
            new GameInstallCoordinator());

        Assert.Equal(
            InstanceInstallNameAvailability.Available,
            await service.CheckAsync("target"));

        settings.MinecraftDirectory = secondDirectory;

        Assert.Equal(
            InstanceInstallNameAvailability.Occupied,
            await service.CheckAsync("target"));
    }

    private static InstanceInstallNameAvailabilityService CreateService(
        string minecraftDirectory,
        IGameInstallCoordinator coordinator) => new(
        new TestSettingsService(new LauncherSettings
        {
            MinecraftDirectory = minecraftDirectory
        }),
        coordinator);
}
