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

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.CurseForge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed partial class LocalModIconEnrichmentService
{
/// <summary>
    /// 先返回新鲜或可用的过期缓存，再远程刷新未命中项，最后执行一次缓存清理。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveMissingIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default,
        IProgress<IReadOnlyDictionary<string, string>>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var candidates = mods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .Where(mod => !string.IsNullOrWhiteSpace(mod.FullPath) && File.Exists(mod.FullPath))
            .GroupBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        logger.LogDebug(
            "Remote local mod icon enrichment started. CandidateCount={CandidateCount}",
            candidates.Count);

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staleResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<ModIconLookupCandidate>();
        var lookups = new List<ModIconLookupCandidate>(candidates.Count);

        // JAR 哈希是此流程最昂贵的本地操作。不能在缓存索引锁内执行，否则列表的轻量
        // 缓存查询（例如 Mod 启停后的图标复用）会被大型整合包的全量哈希阻塞数秒。
        foreach (var mod in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lookup = await CreateLookupCandidateAsync(mod, cancellationToken).ConfigureAwait(false);
            if (lookup is not null)
                lookups.Add(lookup);
        }

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        RemoteIconCacheIndex index;
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            index = await cacheIndexStore.LoadAsync(cancellationToken).ConfigureAwait(false);

            foreach (var lookup in lookups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cachedIcon = TryGetCachedIcon(
                    index,
                    lookup.Sha1Alias,
                    now,
                    allowStale: true,
                    updateLastUsed: true,
                    out var isStale);
                if (cachedIcon is not null)
                {
                    CacheFileAlias(index, lookup);
                    if (isStale)
                    {
                        staleResults[lookup.FullPath] = cachedIcon;
                        unresolved.Add(lookup);
                    }
                    else
                    {
                        result[lookup.FullPath] = cachedIcon;
                    }
                }
                else
                {
                    unresolved.Add(lookup);
                }
            }

            if (result.Count > 0 || staleResults.Count > 0)
            {
                await cacheIndexStore.SaveAsync(index, cancellationToken).ConfigureAwait(false);
                ReportProgress(progress, result);
                ReportProgress(progress, staleResults);
                logger.LogDebug(
                    "Remote local mod icon cache resolved icons. FreshCount={FreshCount} StaleCount={StaleCount}",
                    result.Count,
                    staleResults.Count);
            }
        }
        finally
        {
            cacheLock.Release();
        }

        if (unresolved.Count > 0)
        {
            try
            {
                var resolved = await ResolveRemoteIconsAsync(unresolved, cancellationToken, progress).ConfigureAwait(false);
                foreach (var pair in resolved)
                    result[pair.Key] = pair.Value;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to resolve remote local mod icons. Cached icons will still be used.");
            }
        }

        foreach (var pair in staleResults)
            result.TryAdd(pair.Key, pair.Value);

        await CleanupCacheOnceAsync(cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Remote local mod icon enrichment completed. CandidateCount={CandidateCount} ResolvedCount={ResolvedCount}",
            candidates.Count,
            result.Count);
        return result;
    }

    /// <summary>
    /// 仅通过文件别名读取现有新鲜缓存，适合列表首次发布前的快速同步。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveCachedIconSourcesAsync(
        IReadOnlyList<LocalMod> mods,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var candidates = mods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .Where(mod => !string.IsNullOrWhiteSpace(mod.FullPath) && File.Exists(mod.FullPath))
            .GroupBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (candidates.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await cacheIndexStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var mod in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileAlias = TryCreateFileAlias(mod.FullPath);
                if (fileAlias is null
                    || !index.FileAliases.TryGetValue(fileAlias, out var entryKey))
                    continue;

                var cachedIcon = TryGetCachedIconByEntryKey(
                    index,
                    entryKey,
                    now,
                    allowStale: true,
                    updateLastUsed: false,
                    out _);
                if (cachedIcon is not null)
                    result[mod.FullPath] = cachedIcon;
            }
        }
        finally
        {
            cacheLock.Release();
        }

        logger.LogDebug(
            "Remote local mod icon cache checked. CandidateCount={CandidateCount} HitCount={HitCount}",
            candidates.Count,
            result.Count);
        return result;
    }

    /// <summary>
    /// 优先批量查询 Modrinth，剩余候选再查询 CurseForge，并逐项报告可用图标。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveRemoteIconsAsync(
        IReadOnlyList<ModIconLookupCandidate> candidates,
        CancellationToken cancellationToken,
        IProgress<IReadOnlyDictionary<string, string>>? progress)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = candidates.ToDictionary(candidate => candidate.Sha1, StringComparer.OrdinalIgnoreCase);
        var progressGate = new object();

        var modrinthIcons = await providerClient.ResolveModrinthAsync(candidates, cancellationToken).ConfigureAwait(false);
        var resolvedModrinthIcons = await CacheProviderIconsAsync(
            modrinthIcons,
            unresolved,
            progress,
            progressGate,
            cancellationToken).ConfigureAwait(false);
        foreach (var (sha1, iconSource) in resolvedModrinthIcons)
        {
            if (!unresolved.TryGetValue(sha1, out var candidate))
                continue;

            result[candidate.FullPath] = iconSource;
            unresolved.Remove(sha1);
        }

        if (unresolved.Count > 0)
        {
            var curseForgeIcons = await providerClient.ResolveCurseForgeAsync(unresolved.Values.ToList(), cancellationToken)
                .ConfigureAwait(false);
            var resolvedCurseForgeIcons = await CacheProviderIconsAsync(
                curseForgeIcons,
                unresolved,
                progress,
                progressGate,
                cancellationToken).ConfigureAwait(false);
            foreach (var (sha1, iconSource) in resolvedCurseForgeIcons)
            {
                if (!unresolved.TryGetValue(sha1, out var candidate))
                    continue;

                result[candidate.FullPath] = iconSource;
                unresolved.Remove(sha1);
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> CacheProviderIconsAsync(
        IReadOnlyDictionary<string, RemoteIconCandidate> providerIcons,
        IReadOnlyDictionary<string, ModIconLookupCandidate> unresolved,
        IProgress<IReadOnlyDictionary<string, string>>? progress,
        object progressGate,
        CancellationToken cancellationToken)
    {
        var resolved = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tasks = providerIcons.Select(async pair =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!unresolved.TryGetValue(pair.Key, out var candidate))
                return;

            var iconSource = await TryCacheRemoteIconAsync(candidate, pair.Value, cancellationToken)
                .ConfigureAwait(false);
            if (iconSource is null)
                return;

            resolved[pair.Key] = iconSource;
            lock (progressGate)
                ReportProgress(progress, candidate.FullPath, iconSource);
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return resolved;
    }

    private static void ReportProgress(
        IProgress<IReadOnlyDictionary<string, string>>? progress,
        IReadOnlyDictionary<string, string> icons)
    {
        if (progress is null || icons.Count == 0)
            return;

        progress.Report(new Dictionary<string, string>(icons, StringComparer.OrdinalIgnoreCase));
    }

    private static void ReportProgress(
        IProgress<IReadOnlyDictionary<string, string>>? progress,
        string fullPath,
        string iconSource)
    {
        if (progress is null || string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(iconSource))
            return;

        progress.Report(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [fullPath] = iconSource
        });
    }
}
