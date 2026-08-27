/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public enum InstanceInstallNameAvailability
{
    Available,
    Occupied,
    Unknown
}

public interface IInstanceInstallNameAvailabilityService
{
    /// <param name="minecraftDirectory">
    /// 由调用方提供当前目录，避免高频调用（如逐字符校验实例名）反复读取 settings.json。
    /// </param>
    Task<InstanceInstallNameAvailability> CheckAsync(
        string minecraftDirectory,
        string instanceName,
        CancellationToken cancellationToken = default);
}
