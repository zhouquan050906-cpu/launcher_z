/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

public sealed class InstanceInstallNameAvailabilityService(
    IGameInstallCoordinator installCoordinator,
    ILogger<InstanceInstallNameAvailabilityService>? logger = null)
    : IInstanceInstallNameAvailabilityService
{
    private readonly ILogger logger = logger ?? NullLogger<InstanceInstallNameAvailabilityService>.Instance;

    public Task<InstanceInstallNameAvailability> CheckAsync(
        string minecraftDirectory,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
            return Task.FromResult(InstanceInstallNameAvailability.Unknown);

        string normalizedName;
        try
        {
            normalizedName = VersionDirectoryName.NormalizeUserInput(instanceName);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(InstanceInstallNameAvailability.Unknown);
        }

        try
        {
            if (installCoordinator.IsInstallingVersion(minecraftDirectory, normalizedName))
                return Task.FromResult(InstanceInstallNameAvailability.Occupied);

            var versionsDirectory = Path.GetFullPath(
                Path.Combine(minecraftDirectory, "versions"));
            var finalDirectory = Path.GetFullPath(
                Path.Combine(versionsDirectory, normalizedName));
            return Task.FromResult(
                InstanceInstallNameOccupancy.IsOccupied(
                    versionsDirectory,
                    finalDirectory,
                    normalizedName)
                    ? InstanceInstallNameAvailability.Occupied
                    : InstanceInstallNameAvailability.Available);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            logger.LogWarning(
                exception,
                "Failed to check Minecraft instance install name availability. InstanceName={InstanceName}",
                normalizedName);
            return Task.FromResult(InstanceInstallNameAvailability.Unknown);
        }
    }
}
