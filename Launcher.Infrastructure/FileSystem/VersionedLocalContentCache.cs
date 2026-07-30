/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using Launcher.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.FileSystem;

internal sealed class VersionedLocalContentCache<TValue>
{
    private const int CurrentVersion = 1;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string cachePath;
    private readonly ILogger logger;
    private readonly Dictionary<string, CacheEntry<TValue>> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool loaded;
    private bool dirty;

    public VersionedLocalContentCache(string cachePath, ILogger logger)
    {
        this.cachePath = cachePath;
        this.logger = logger;
    }

    public bool TryGet(
        LocalContentFileIdentity identity,
        Func<TValue, bool>? validate,
        out TValue value)
    {
        EnsureLoaded();
        if (entries.TryGetValue(identity.FullPath, out var entry)
            && entry.Length == identity.Length
            && entry.LastWriteTimeUtcTicks == identity.LastWriteTimeUtcTicks
            && entry.Value is not null
            && (validate is null || validate(entry.Value)))
        {
            if (DateTimeOffset.UtcNow - entry.LastUsedAtUtc >= TimeSpan.FromDays(1))
            {
                entries[identity.FullPath] = entry with { LastUsedAtUtc = DateTimeOffset.UtcNow };
                dirty = true;
            }
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(LocalContentFileIdentity identity, TValue value)
    {
        EnsureLoaded();
        entries[identity.FullPath] = new CacheEntry<TValue>(
            identity.FullPath,
            identity.Length,
            identity.LastWriteTimeUtcTicks,
            DateTimeOffset.UtcNow,
            value);
        dirty = true;
    }

    public bool TryMove(string oldPath, LocalContentFileIdentity newIdentity)
    {
        EnsureLoaded();
        if (!entries.Remove(oldPath, out var entry))
            return false;

        entries[newIdentity.FullPath] = entry with
        {
            Path = newIdentity.FullPath,
            Length = newIdentity.Length,
            LastWriteTimeUtcTicks = newIdentity.LastWriteTimeUtcTicks,
            LastUsedAtUtc = DateTimeOffset.UtcNow
        };
        dirty = true;
        return true;
    }

    public void PruneDirectory(string directory, IReadOnlySet<string> currentPaths)
    {
        EnsureLoaded();
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var pair in entries.ToArray())
        {
            var sameDirectory = string.Equals(
                Path.GetDirectoryName(pair.Key),
                directory,
                StringComparison.OrdinalIgnoreCase);
            if (pair.Value.LastUsedAtUtc < cutoff
                || sameDirectory && !currentPaths.Contains(pair.Key))
            {
                entries.Remove(pair.Key);
                dirty = true;
            }
        }
    }

    public async Task SaveIfChangedAsync(CancellationToken cancellationToken)
    {
        EnsureLoaded();
        if (!dirty)
            return;

        try
        {
            var document = new CacheDocument<TValue>(CurrentVersion, entries.Values.ToArray());
            await AtomicJsonFileWriter.WriteAsync(
                    cachePath,
                    document,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            dirty = false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to persist local content cache. CachePath={CachePath}",
                cachePath);
        }
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        if (!File.Exists(cachePath))
            return;

        try
        {
            var document = JsonSerializer.Deserialize<CacheDocument<TValue>>(
                File.ReadAllText(cachePath),
                SerializerOptions);
            if (document is null || document.Version != CurrentVersion)
            {
                dirty = true;
                return;
            }

            foreach (var entry in document.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Path))
                    entries[entry.Path] = entry;
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            entries.Clear();
            dirty = true;
            logger.LogDebug(
                exception,
                "Local content cache could not be read and will be rebuilt. CachePath={CachePath}",
                cachePath);
        }
    }

    private sealed record CacheDocument<T>(int Version, IReadOnlyList<CacheEntry<T>> Entries);

    private sealed record CacheEntry<T>(
        string Path,
        long Length,
        long LastWriteTimeUtcTicks,
        DateTimeOffset LastUsedAtUtc,
        T Value);
}
