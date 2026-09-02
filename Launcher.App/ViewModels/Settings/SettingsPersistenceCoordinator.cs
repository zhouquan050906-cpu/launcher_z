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

public sealed class SettingsPersistenceCoordinator : IDisposable
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
        // 调用方在失败时会自行回滚内存状态，因此本次更新不能重新入队，
        // 但同批次里其他分区排队的更新与这次回滚无关，必须保留重试机会。
        await SaveCoreAsync(cancellationToken, updateToDiscardOnFailure: update).ConfigureAwait(false);
    }

    public void Dispose()
    {
        CancelPendingSave();
        saveLock.Dispose();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingSave();
        await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
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
            await SaveCoreAsync(cancellation.Token).ConfigureAwait(false);
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

    /// <param name="updateToDiscardOnFailure">
    /// 保存失败时不重新入队的那一个更新（调用方已自行回滚它）；其余更新一律回填等待重试。
    /// 传 null 表示整批都回填。
    /// </param>
    private async Task SaveCoreAsync(
        CancellationToken cancellationToken,
        Action<LauncherSettings>? updateToDiscardOnFailure = null)
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
                var restorableUpdates = updates.ToList();
                if (updateToDiscardOnFailure is not null)
                {
                    // 只剔除本次这一个；同一个委托实例可能重复排队，因此按索引删除最后一次追加的那个。
                    var discardedIndex = restorableUpdates.LastIndexOf(updateToDiscardOnFailure);
                    if (discardedIndex >= 0)
                        restorableUpdates.RemoveAt(discardedIndex);
                }

                if (restorableUpdates.Count > 0)
                {
                    lock (pendingUpdatesLock)
                        pendingUpdates.InsertRange(0, restorableUpdates);
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
