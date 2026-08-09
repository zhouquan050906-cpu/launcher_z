/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed class InstanceRepairException : Exception
{
    public LaunchDownloadDiagnostic? DownloadDiagnostic { get; }

    public GameFileRepairFailure? FileFailure { get; }

    public bool? AutoRepairEnabled { get; }

    public InstanceRepairException()
    {
    }

    public InstanceRepairException(string message)
        : base(message)
    {
    }

    public InstanceRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InstanceRepairException(string message, LaunchDownloadDiagnostic downloadDiagnostic)
        : base(message)
    {
        DownloadDiagnostic = downloadDiagnostic;
    }

    public InstanceRepairException(
        string message,
        GameFileRepairFailure fileFailure,
        bool autoRepairEnabled)
        : base(message)
    {
        FileFailure = fileFailure;
        AutoRepairEnabled = autoRepairEnabled;
    }

    public InstanceRepairException(
        string message,
        Exception innerException,
        LaunchDownloadDiagnostic downloadDiagnostic)
        : base(message, innerException)
    {
        DownloadDiagnostic = downloadDiagnostic;
    }
}
