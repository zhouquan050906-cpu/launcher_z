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

    public async Task<InstanceInstallNameAvailability> CheckAsync(
        string minecraftDirectory,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minecraftDirectory))
            return InstanceInstallNameAvailability.Unknown;

        string normalizedName;
        try
        {
            normalizedName = VersionDirectoryName.NormalizeUserInput(instanceName);
        }
        catch (ArgumentException)
        {
            return InstanceInstallNameAvailability.Unknown;
        }

        try
        {
            // 占用检查要枚举整个 versions 目录并读取待提交安装的标记文件，冷缓存或杀毒软件
            // 实时扫描下可达数十毫秒。调用方是逐字符校验实例名的界面代码，直接在调用线程上
            // 跑完会阻塞 UI 线程，因此必须切到线程池。
            return await Task.Run(
                () =>
                {
                    if (installCoordinator.IsInstallingVersion(minecraftDirectory, normalizedName))
                        return InstanceInstallNameAvailability.Occupied;

                    var versionsDirectory = Path.GetFullPath(
                        Path.Combine(minecraftDirectory, "versions"));
                    var finalDirectory = Path.GetFullPath(
                        Path.Combine(versionsDirectory, normalizedName));
                    return InstanceInstallNameOccupancy.IsOccupied(
                        versionsDirectory,
                        finalDirectory,
                        normalizedName)
                        ? InstanceInstallNameAvailability.Occupied
                        : InstanceInstallNameAvailability.Available;
                },
                cancellationToken).ConfigureAwait(false);
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
            return InstanceInstallNameAvailability.Unknown;
        }
    }
}
