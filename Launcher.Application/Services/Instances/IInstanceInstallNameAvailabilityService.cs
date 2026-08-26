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
    Task<InstanceInstallNameAvailability> CheckAsync(
        string instanceName,
        CancellationToken cancellationToken = default);
}
