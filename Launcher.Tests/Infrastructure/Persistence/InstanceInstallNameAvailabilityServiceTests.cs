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
        var service = CreateService(new GameInstallCoordinator());

        var result = await service.CheckAsync(minecraftDirectory, "occupied");

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
        var service = CreateService(new GameInstallCoordinator());

        var result = await service.CheckAsync(minecraftDirectory, "pending");

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
        var service = CreateService(coordinator);

        var result = await service.CheckAsync(minecraftDirectory, "installing");

        Assert.Equal(InstanceInstallNameAvailability.Occupied, result);
    }

    [Fact]
    public async Task CheckAnswersForWhicheverDirectoryTheCallerSupplies()
    {
        var firstDirectory = Path.Combine(TempRoot, "first", ".minecraft");
        var secondDirectory = Path.Combine(TempRoot, "second", ".minecraft");
        Directory.CreateDirectory(Path.Combine(secondDirectory, "versions", "target"));
        var service = CreateService(new GameInstallCoordinator());

        // 目录由调用方给出，服务不再自行读取 settings.json，因此切换目录只是换一个参数。
        Assert.Equal(
            InstanceInstallNameAvailability.Available,
            await service.CheckAsync(firstDirectory, "target"));
        Assert.Equal(
            InstanceInstallNameAvailability.Occupied,
            await service.CheckAsync(secondDirectory, "target"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingDirectoryReportsUnknownInsteadOfGuessing(string minecraftDirectory)
    {
        var service = CreateService(new GameInstallCoordinator());

        Assert.Equal(
            InstanceInstallNameAvailability.Unknown,
            await service.CheckAsync(minecraftDirectory, "target"));
    }

    private static InstanceInstallNameAvailabilityService CreateService(
        IGameInstallCoordinator coordinator) => new(coordinator);
}
