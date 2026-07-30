/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Resources;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class RemoteThumbnailDownloadClientTests : TestTempDirectory
{
    [Fact]
    public async Task ContentCacheHitReportsBeforeLaterFingerprintCompletes()
    {
        Directory.CreateDirectory(TempRoot);
        var oldMods = CreateMods("old");
        var hashes = oldMods
            .Select(mod => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(mod.FullPath))).ToLowerInvariant())
            .ToArray();
        var seedHandler = new LocalModIconHandler(hashes);
        using (var seedClient = new HttpClient(seedHandler))
        {
            var seedService = new LocalModIconEnrichmentService(
                new LauncherPathProvider(TempRoot),
                seedClient,
                logger: NullLogger<LocalModIconEnrichmentService>.Instance);
            var seedTask = seedService.ResolveMissingIconSourcesAsync(oldMods);
            await seedHandler.AllIconsStarted.WaitAsync(TimeSpan.FromSeconds(5));
            seedHandler.ReleaseIcons();
            Assert.Equal(2, (await seedTask.WaitAsync(TimeSpan.FromSeconds(10))).Count);
        }

        var newMods = CreateMods("new");
        var secondFingerprintStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondFingerprint = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fingerprintService = new LocalFileFingerprintService(path =>
        {
            var stream = File.OpenRead(path);
            return string.Equals(path, newMods[1].FullPath, StringComparison.OrdinalIgnoreCase)
                ? new BlockingReadStream(stream, secondFingerprintStarted, releaseSecondFingerprint)
                : stream;
        });
        using var rejectingClient = new HttpClient(new RejectingHandler());
        var service = new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            rejectingClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance,
            fingerprintService: fingerprintService);
        var firstReported = new TaskCompletionSource<LocalContentIconResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<LocalContentIconResolution>(resolution =>
        {
            if (string.Equals(resolution.FullPath, newMods[0].FullPath, StringComparison.OrdinalIgnoreCase))
                firstReported.TrySetResult(resolution);
        });

        var enrichment = service.ResolveMissingIconSourcesAsync(newMods, progress: progress);
        await secondFingerprintStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = await firstReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(newMods[0].FullPath, first.FullPath);
        Assert.False(enrichment.IsCompleted);

        releaseSecondFingerprint.TrySetResult();
        var resolved = await enrichment.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, resolved.Count);
        return;

        LocalMod[] CreateMods(string prefix) => Enumerable.Range(0, 2)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"{prefix}-mod-{index}.jar");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"shared-mod-content-{index}"));
                return new LocalMod
                {
                    Name = $"Mod {index}",
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    IsEnabled = true
                };
            })
            .ToArray();
    }

    [Fact]
    public async Task LocalModEnrichmentStartsThumbnailDownloadsConcurrently()
    {
        Directory.CreateDirectory(TempRoot);
        var mods = Enumerable.Range(0, 2)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"mod-{index}.jar");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"mod-content-{index}"));
                return new LocalMod
                {
                    Name = $"Mod {index}",
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    IsEnabled = true
                };
            })
            .ToArray();
        var hashes = mods
            .Select(mod => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(mod.FullPath))).ToLowerInvariant())
            .ToArray();
        var handler = new LocalModIconHandler(hashes);
        using var httpClient = new HttpClient(handler);
        var service = new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance);
        var firstIconReported = new TaskCompletionSource<LocalContentIconResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<LocalContentIconResolution>(resolution =>
        {
            if (string.Equals(resolution.FullPath, mods[0].FullPath, StringComparison.OrdinalIgnoreCase))
                firstIconReported.TrySetResult(resolution);
        });

        var enrichment = service.ResolveMissingIconSourcesAsync(mods, progress: progress);
        await handler.AllIconsStarted.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.MaximumActiveIconRequests);

        handler.ReleaseIcon(0);
        var firstResolution = await firstIconReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(enrichment.IsCompleted);
        Assert.Equal(mods[0].FullPath, firstResolution.FullPath);
        Assert.False(string.IsNullOrWhiteSpace(firstResolution.IconSource));

        handler.ReleaseIcons();
        var resolved = await enrichment.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, resolved.Count);
        Assert.All(mods, mod => Assert.True(resolved.ContainsKey(mod.FullPath)));
    }

    [Fact]
    public async Task FirstRemoteBatchReportsBeforeLaterFingerprintCompletes()
    {
        Directory.CreateDirectory(TempRoot);
        var mods = Enumerable.Range(0, LocalResourceCategoryEnrichmentService.ProviderBatchSize + 1)
            .Select(index =>
            {
                var path = Path.Combine(TempRoot, $"batched-mod-{index}.jar");
                File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"batched-mod-content-{index}"));
                return new LocalMod
                {
                    Name = $"Mod {index}",
                    FileName = Path.GetFileName(path),
                    FullPath = path,
                    IsEnabled = true
                };
            })
            .ToArray();
        var hashes = mods
            .Select(mod => Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(mod.FullPath))).ToLowerInvariant())
            .ToArray();
        var laterFingerprintStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLaterFingerprint = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fingerprintService = new LocalFileFingerprintService(path =>
        {
            var stream = File.OpenRead(path);
            return string.Equals(path, mods[^1].FullPath, StringComparison.OrdinalIgnoreCase)
                ? new BlockingReadStream(stream, laterFingerprintStarted, releaseLaterFingerprint)
                : stream;
        });
        var handler = new LocalModIconHandler(
            hashes,
            RemoteThumbnailDownloadClient.MaximumConcurrency);
        using var httpClient = new HttpClient(handler);
        var service = new LocalModIconEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance,
            fingerprintService: fingerprintService);
        var firstReported = new TaskCompletionSource<LocalContentIconResolution>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new InlineProgress<LocalContentIconResolution>(resolution =>
        {
            if (string.Equals(resolution.FullPath, mods[0].FullPath, StringComparison.OrdinalIgnoreCase))
                firstReported.TrySetResult(resolution);
        });

        var enrichment = service.ResolveMissingIconSourcesAsync(mods, progress: progress);
        await laterFingerprintStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handler.AllIconsStarted.WaitAsync(TimeSpan.FromSeconds(5));
        handler.ReleaseIcon(0);
        await firstReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(enrichment.IsCompleted);

        releaseLaterFingerprint.TrySetResult();
        handler.ReleaseIcons();
        var resolved = await enrichment.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(mods.Length, resolved.Count);
    }

    private sealed class LocalModIconHandler(
        IReadOnlyList<string> hashes,
        int? expectedStartedCount = null) : HttpMessageHandler
    {
        private static readonly byte[] IconPayload = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        private readonly TaskCompletionSource allIconsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource[] releaseIcons = Enumerable.Range(0, hashes.Count)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        private int activeIconRequests;
        private int startedIconRequests;
        private int maximumActiveIconRequests;

        public Task AllIconsStarted => allIconsStarted.Task;
        public int MaximumActiveIconRequests => Volatile.Read(ref maximumActiveIconRequests);

        public void ReleaseIcon(int index) => releaseIcons[index].TrySetResult();

        public void ReleaseIcons()
        {
            foreach (var releaseIcon in releaseIcons)
                releaseIcon.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/version_files", StringComparison.OrdinalIgnoreCase))
            {
                var versions = hashes
                    .Select((hash, index) => new KeyValuePair<string, object>(
                        hash,
                        new Dictionary<string, string> { ["project_id"] = $"project-{index}" }))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                return JsonResponse(versions);
            }

            if (uri.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.EndsWith("/projects", StringComparison.OrdinalIgnoreCase))
            {
                var projects = hashes.Select((_, index) => new Dictionary<string, string>
                {
                    ["id"] = $"project-{index}",
                    ["icon_url"] = $"https://cdn.example.com/icons/{index}.png"
                });
                return JsonResponse(projects);
            }

            if (uri.Host.Equals("cdn.example.com", StringComparison.OrdinalIgnoreCase))
            {
                var active = Interlocked.Increment(ref activeIconRequests);
                UpdateMaximum(ref maximumActiveIconRequests, active);
                if (Interlocked.Increment(ref startedIconRequests) == (expectedStartedCount ?? hashes.Count))
                    allIconsStarted.TrySetResult();
                try
                {
                    var index = int.Parse(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
                    await releaseIcons[index].Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(IconPayload)
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref activeIconRequests);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected network request: {request.RequestUri}");
    }

    private sealed class BlockingReadStream(
        Stream inner,
        TaskCompletionSource started,
        TaskCompletionSource release) : Stream
    {
        private int blocked;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref blocked, 1) == 0)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await inner.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
