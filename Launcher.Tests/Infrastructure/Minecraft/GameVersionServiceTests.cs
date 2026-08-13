/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class GameVersionServiceTests
{
    [Fact]
    public void VersionCatalogUsesInteractiveMetadataTimeouts()
    {
        var options = GameVersionService.VersionCatalogRetryOptions;

        Assert.Equal(1, options.MaxAttemptsPerSource);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ResponseHeadersTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.FirstByteTimeout);
        Assert.Equal(TimeSpan.FromSeconds(8), options.BodyIdleTimeout);
    }

    [Fact]
    public async Task VersionCatalogFailureTriesPreferredAndFallbackSourceOnce()
    {
        var handler = new FailingRequestHandler();
        using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = new GameVersionService(httpClient);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.GetVersionsAsync(DownloadSourcePreference.BmclApi));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("bmclapi2.bangbang93.com", handler.Requests[0].Host);
        Assert.Equal("piston-meta.mojang.com", handler.Requests[1].Host);
    }

    private sealed class FailingRequestHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
        }
    }
}
