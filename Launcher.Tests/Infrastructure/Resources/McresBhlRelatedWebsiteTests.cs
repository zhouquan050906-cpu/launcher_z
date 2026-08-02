/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using Launcher.Infrastructure.Resources;
using Microsoft.Extensions.Logging;

namespace Launcher.Tests.Infrastructure.Resources;

public sealed class McresBhlRelatedWebsiteTests
{
    [Fact]
    public async Task ModrinthLookupUsesStableProjectIdAndDoesNotLogApiKey()
    {
        const string testApiKey = "test-bhl-key";
        var logger = new CollectingLogger<ResourceCatalogService>();
        var handler = new RecordingHandler(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
            ? """{"code":0,"resources":[{"resource_type":"resourcepack","resource_id":17}]}"""
            : """{"code":0,"url":"https://www.mcresource.cn/resourcepack/17"}"""));
        var service = CreateService(handler, testApiKey, logger);

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ResourcePack,
            ResourceProjectSource.Modrinth,
            "stable/project id"));

        Assert.NotNull(website);
        Assert.Equal("MCRES", website.Name);
        Assert.Equal("https://www.mcresource.cn/resourcepack/17", website.Url);
        var lookupQuery = Uri.UnescapeDataString(handler.Requests[0].Query);
        Assert.Contains("provider=modrinth", lookupQuery, StringComparison.Ordinal);
        Assert.Contains("project_id=stable/project id", lookupQuery, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(testApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"code":0,"resources":[]}""")]
    public async Task MissingOrInvalidLookupResponseIsHidden(string responseBody)
    {
        var handler = new RecordingHandler(_ => Json(responseBody));
        var service = CreateService(handler, "test-key");

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ResourcePack,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://www.mcresource.cn/resourcepack/1")]
    public async Task UntrustedDetailUrlIsHidden(string url)
    {
        var handler = new RecordingHandler(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
            ? """{"code":0,"resources":[{"resource_type":"resourcepack","resource_id":1}]}"""
            : $$"""{"code":0,"url":"{{url}}"}"""));
        var service = CreateService(handler, "test-key");

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ResourcePack,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("""{"code":404,"message":"not found"}""")]
    public async Task InvalidDetailResponseIsHidden(string responseBody)
    {
        var handler = new RecordingHandler(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
            ? """{"code":0,"resources":[{"resource_type":"resourcepack","resource_id":1}]}"""
            : responseBody));
        var service = CreateService(handler, "test-key");

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ResourcePack,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task HttpFailureIsHidden(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        var service = CreateService(handler, "test-key");

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ShaderPack,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(ResourceProjectKind.Mod)]
    public async Task UnsupportedKindsDoNotSendMcresRequests(ResourceProjectKind kind)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("MCRES request must not start."));
        var service = CreateService(handler, "test-key");

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            kind,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingApiKeyDoesNotSendMcresRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("MCRES request must not start."));
        var service = CreateService(handler, null);

        var website = await service.GetRelatedWebsiteAsync(new ResourceProjectReference(
            ResourceProjectKind.ResourcePack,
            ResourceProjectSource.Modrinth,
            "project"));

        Assert.Null(website);
        Assert.Empty(handler.Requests);
    }

    private static ResourceCatalogService CreateService(
        HttpMessageHandler handler,
        string? apiKey,
        ILogger<ResourceCatalogService>? logger = null) =>
        new(
            new HttpClient(handler),
            logger: logger,
            mcresBhlApiKeyResolver: new StubApiKeyResolver(apiKey));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    };

    private sealed class StubApiKeyResolver(string? apiKey) : IMcresBhlApiKeyResolver
    {
        public Task<string?> TryResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(apiKey);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request));
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
