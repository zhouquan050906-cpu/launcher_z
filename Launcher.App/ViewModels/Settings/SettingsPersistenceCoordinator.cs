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

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

internal sealed class SettingsPersistenceCoordinator : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(350);
    private readonly ISettingsService settingsService;
    private readonly IStatusService statusService;
    private readonly ILogger logger;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private readonly object pendingUpdatesLock = new();
    private readonly List<Action<LauncherSettings>> pendingUpdates = [];
    private CancellationTokenSource? pendingSave;

    public SettingsPersistenceCoordinator(
        ISettingsService settingsService,
        IStatusService statusService,
        ILogger logger)
    {
        this.settingsService = settingsService;
        this.statusService = statusService;
        this.logger = logger;
    }

    public LauncherSettings Settings { get; private set; } = new();

    public bool IsPrimed { get; private set; }

    public void Prime(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CancelPendingSave();
        lock (pendingUpdatesLock)
            pendingUpdates.Clear();
        Settings = settings;
        IsPrimed = true;
    }

    public void Update(Action<LauncherSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!IsPrimed)
            return;

        update(Settings);
        lock (pendingUpdatesLock)
            pendingUpdates.Add(update);
        ScheduleSave();
    }

    public async Task SaveImmediatelyAsync(
        Action<LauncherSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!IsPrimed)
            return;

        CancelPendingSave();
        update(Settings);
        lock (pendingUpdatesLock)
            pendingUpdates.Add(update);
        await SaveCoreAsync(cancellationToken, restoreUpdatesOnFailure: false).ConfigureAwait(false);
    }

    public void Dispose()
    {
        CancelPendingSave();
        saveLock.Dispose();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingSave();
        await SaveCoreAsync(cancellationToken, restoreUpdatesOnFailure: true).ConfigureAwait(false);
    }

    private void ScheduleSave()
    {
        CancelPendingSave();
        var cancellation = new CancellationTokenSource();
        pendingSave = cancellation;
        _ = SaveAfterDelayAsync(cancellation);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SaveDelay, cancellation.Token).ConfigureAwait(false);
            await SaveCoreAsync(cancellation.Token, restoreUpdatesOnFailure: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save launcher settings.");
            statusService.Report(Strings.Status_SettingsSaveFailed);
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref pendingSave, null, cancellation), cancellation))
                cancellation.Dispose();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken, bool restoreUpdatesOnFailure)
    {
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Action<LauncherSettings>[] updates;
            lock (pendingUpdatesLock)
            {
                if (pendingUpdates.Count == 0)
                    return;
                updates = pendingUpdates.ToArray();
                pendingUpdates.Clear();
            }
            try
            {
                await settingsService.UpdateAsync(
                        latest =>
                        {
                            foreach (var update in updates)
                                update(latest);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                if (restoreUpdatesOnFailure)
                {
                    lock (pendingUpdatesLock)
                        pendingUpdates.InsertRange(0, updates);
                }
                throw;
            }
        }
        finally
        {
            saveLock.Release();
        }
    }

    private void CancelPendingSave()
    {
        var cancellation = Interlocked.Exchange(ref pendingSave, null);
        if (cancellation is null)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
