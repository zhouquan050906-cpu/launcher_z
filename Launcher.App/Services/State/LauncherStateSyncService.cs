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

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Services;

public sealed class LauncherStateSyncService : IDisposable
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(300);
    private readonly object syncLock = new();
    private readonly ILauncherStateMonitor stateMonitor;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LauncherStateSyncService> logger;
    private readonly TimeSpan debounceDelay;
    private Func<LauncherSettings>? settingsProvider;
    private Func<Task>? synchronize;
    private CancellationTokenSource? workerCancellation;
    private Task pendingTask = Task.CompletedTask;
    private DateTimeOffset ignoreMonitorChangesUntil;
    private long requestGeneration;
    private bool syncRequested;
    private bool workerRunning;
    private bool isStarted;
    private bool isDisposed;

    public LauncherStateSyncService(
        ILauncherStateMonitor stateMonitor,
        IUiDispatcher uiDispatcher,
        ILogger<LauncherStateSyncService>? logger = null,
        TimeSpan? debounceDelay = null)
    {
        this.stateMonitor = stateMonitor;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<LauncherStateSyncService>.Instance;
        this.debounceDelay = debounceDelay ?? DefaultDebounceDelay;
    }

    public void Start(Func<LauncherSettings> settingsProvider, Func<Task> synchronize)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settingsProvider);
        ArgumentNullException.ThrowIfNull(synchronize);

        if (isStarted)
            Stop();
        this.settingsProvider = settingsProvider;
        this.synchronize = synchronize;
        stateMonitor.StateChanged += StateMonitor_StateChanged;
        stateMonitor.Watch(settingsProvider());
        isStarted = true;
    }

    public void RequestSync()
    {
        lock (syncLock)
        {
            if (!isStarted)
                return;

            syncRequested = true;
            requestGeneration++;
            if (workerRunning)
                return;

            workerRunning = true;
            workerCancellation = new CancellationTokenSource();
            pendingTask = ProcessRequestsAsync(workerCancellation);
        }
    }

    public Task WaitForPendingSyncAsync()
    {
        lock (syncLock)
            return pendingTask;
    }

    public void AcknowledgeLocalStateChange()
    {
        Func<LauncherSettings>? getSettings;
        lock (syncLock)
        {
            if (!isStarted)
                return;

            // FileSystemWatcher also observes launcher-owned atomic writes. Cancel the refresh
            // already queued for that write and ignore its duplicate notifications briefly.
            ignoreMonitorChangesUntil = DateTimeOffset.UtcNow + debounceDelay;
            syncRequested = false;
            getSettings = settingsProvider;
        }

        if (getSettings is not null)
            stateMonitor.Watch(getSettings());
    }

    public void Stop()
    {
        if (!isStarted)
            return;

        stateMonitor.StateChanged -= StateMonitor_StateChanged;

        isStarted = false;
        settingsProvider = null;
        synchronize = null;
        lock (syncLock)
        {
            syncRequested = false;
            workerCancellation?.Cancel();
        }

        stateMonitor.Stop();
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        Stop();
        isDisposed = true;
    }

    private void StateMonitor_StateChanged(object? sender, EventArgs e)
    {
        lock (syncLock)
        {
            if (DateTimeOffset.UtcNow < ignoreMonitorChangesUntil)
                return;
        }

        RequestSync();
    }

    private async Task ProcessRequestsAsync(CancellationTokenSource cancellation)
    {
        try
        {
            while (true)
            {
                long observedGeneration;
                lock (syncLock)
                {
                    if (!isStarted || !syncRequested)
                    {
                        CompleteWorkerLocked(cancellation);
                        return;
                    }

                    observedGeneration = requestGeneration;
                }

                await Task.Delay(debounceDelay, cancellation.Token).ConfigureAwait(false);

                Func<Task>? synchronizeCurrentState;
                Func<LauncherSettings>? getSettings;
                lock (syncLock)
                {
                    if (!isStarted || !syncRequested)
                    {
                        CompleteWorkerLocked(cancellation);
                        return;
                    }

                    if (observedGeneration != requestGeneration)
                        continue;

                    syncRequested = false;
                    synchronizeCurrentState = synchronize;
                    getSettings = settingsProvider;
                }

                if (synchronizeCurrentState is null || getSettings is null)
                    continue;

                try
                {
                    await uiDispatcher.InvokeAsync(synchronizeCurrentState).ConfigureAwait(false);
                    if (!cancellation.IsCancellationRequested && isStarted)
                        stateMonitor.Watch(getSettings());
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to synchronize launcher state after a monitored change.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            lock (syncLock)
                CompleteWorkerLocked(cancellation);

            cancellation.Dispose();
        }
    }

    private void CompleteWorkerLocked(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(workerCancellation, cancellation))
            return;

        workerCancellation = null;
        workerRunning = false;
    }
}
