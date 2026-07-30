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
using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.CurseForge;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Modpacks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LocalFileFingerprintServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ConcurrentRequestsShareOneSeekableStreamAndMatchEstablishedFingerprints()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "shared.jar");
        var bytes = new byte[81920 + 17];
        new Random(42).NextBytes(bytes);
        bytes[81918] = 0x20;
        bytes[81919] = 0x0a;
        bytes[81920] = 0x0d;
        bytes[^1] = 0x09;
        await File.WriteAllBytesAsync(path, bytes);
        var openCount = 0;
        var service = new LocalFileFingerprintService(filePath =>
        {
            Interlocked.Increment(ref openCount);
            return OpenFile(filePath);
        });

        var requests = Enumerable.Range(0, 8)
            .Select(_ => service.GetFingerprintAsync(path))
            .ToArray();
        var results = await Task.WhenAll(requests);
        var expectedMurmur = await CurseForgeFingerprintUtility.ComputeFileFingerprintAsync(path);

        Assert.Equal(1, Volatile.Read(ref openCount));
        Assert.All(results, result =>
        {
            Assert.Equal(Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(), result.Sha1);
            Assert.Equal(expectedMurmur, result.CurseForgeFingerprint);
        });
    }

    [Fact]
    public async Task ChangedFileIdentityRecomputesFingerprint()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "changed.jar");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("first"));
        var openCount = 0;
        var service = new LocalFileFingerprintService(filePath =>
        {
            Interlocked.Increment(ref openCount);
            return OpenFile(filePath);
        });

        var first = await service.GetFingerprintAsync(path);
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("second-content"));
        var second = await service.GetFingerprintAsync(path);

        Assert.Equal(2, Volatile.Read(ref openCount));
        Assert.NotEqual(first.Sha1, second.Sha1);
        Assert.NotEqual(first.CurseForgeFingerprint, second.CurseForgeFingerprint);
    }

    [Fact]
    public async Task CancelingOneWaiterDoesNotCancelSharedComputation()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "cancel-shared.jar");
        var bytes = Encoding.UTF8.GetBytes("shared cancellation");
        await File.WriteAllBytesAsync(path, bytes);
        var stream = new CoordinatedReadStream(bytes);
        var service = new LocalFileFingerprintService(_ => stream);
        using var firstCancellation = new CancellationTokenSource();

        var first = service.GetFingerprintAsync(path, firstCancellation.Token);
        var second = service.GetFingerprintAsync(path);
        await stream.FirstReadStarted.WaitAsync(TimeSpan.FromSeconds(5));
        firstCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        stream.ReleaseReads();
        var result = await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant(), result.Sha1);
    }

    [Fact]
    public async Task IconAndCategoryEnrichmentShareTheSameModFingerprint()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "shared-consumer.jar");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("shared consumer mod"));
        var openCount = 0;
        var fingerprintService = new LocalFileFingerprintService(filePath =>
        {
            Interlocked.Increment(ref openCount);
            return OpenFile(filePath);
        });
        using var httpClient = new HttpClient(new EmptyMatchHandler());
        var pathProvider = new LauncherPathProvider(TempRoot);
        var iconService = new LocalModIconEnrichmentService(
            pathProvider,
            httpClient,
            logger: NullLogger<LocalModIconEnrichmentService>.Instance,
            fingerprintService: fingerprintService);
        var categoryService = new LocalResourceCategoryEnrichmentService(
            pathProvider,
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
            fingerprintService: fingerprintService);
        var mod = new LocalMod
        {
            Name = "Shared Consumer",
            FileName = Path.GetFileName(path),
            FullPath = path,
            IsEnabled = true
        };

        var iconTask = iconService.ResolveMissingIconSourcesAsync([mod]);
        var categoryTask = categoryService.ResolveCategoriesAsync(
            [new LocalResourceCategoryCandidate(path, ResourceProjectKind.Mod)]);
        await Task.WhenAll(iconTask, categoryTask);

        Assert.Equal(1, Volatile.Read(ref openCount));
    }

    private static FileStream OpenFile(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        useAsync: true);

    private sealed class EmptyMatchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }

    private sealed class CoordinatedReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private readonly TaskCompletionSource firstReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseReads =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int hasWaited;

        public Task FirstReadStarted => firstReadStarted.Task;

        public void ReleaseReads() => releaseReads.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref hasWaited, 1) == 0)
            {
                firstReadStarted.TrySetResult();
                await releaseReads.Task.WaitAsync(cancellationToken);
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
