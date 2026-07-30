/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LocalResourceCategoryEnrichmentServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ExactModrinthFileMatchMapsCategoriesForRequestedResourceKind()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "shader.zip");
        var duplicatePath = Path.Combine(TempRoot, "shader-copy.zip");
        var bytes = Encoding.UTF8.GetBytes("recognized shader archive");
        File.WriteAllBytes(path, bytes);
        File.WriteAllBytes(duplicatePath, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        using var httpClient = new HttpClient(new ModrinthMatchHandler(sha1));
        var service = new LocalResourceCategoryEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);

        var result = await service.ResolveCategoriesAsync(
        [
            new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack),
            new LocalResourceCategoryCandidate(duplicatePath, ResourceProjectKind.ShaderPack)
        ]);

        Assert.Equal(2, result.Count);
        Assert.All(result.Values, categories => Assert.Equal(
            [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
            categories));
    }

    [Fact]
    public async Task PersistedCategoriesAreAvailableAfterServiceRestartWithoutNetworkRequests()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "cached-mod.jar");
        var bytes = Encoding.UTF8.GetBytes("persisted recognized mod");
        File.WriteAllBytes(path, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var pathProvider = new LauncherPathProvider(TempRoot);

        using (var firstClient = new HttpClient(new ModrinthMatchHandler(sha1)))
        {
            var firstService = new LocalResourceCategoryEnrichmentService(
                pathProvider,
                firstClient,
                logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);
            var first = await firstService.ResolveCategoriesAsync(
                [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack)]);
            Assert.Equal(
                [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
                Assert.Single(first).Value);
        }

        using var secondClient = new HttpClient(new RejectingHandler());
        var restartedService = new LocalResourceCategoryEnrichmentService(
            pathProvider,
            secondClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance);
        var candidate = new LocalResourceCategoryCandidate(path, ResourceProjectKind.ShaderPack);

        var cached = await restartedService.ResolveCachedCategoriesAsync([candidate]);
        var resolved = await restartedService.ResolveCategoriesAsync([candidate]);

        Assert.Equal(
            [ResourceProjectCategory.Fantasy, ResourceProjectCategory.Realistic],
            Assert.Single(cached).Value);
        Assert.Equal(cached.Single().Value, Assert.Single(resolved).Value);
        Assert.True(File.Exists(Path.Combine(
            pathProvider.DefaultDataDirectory,
            "cache",
            "resources",
            "local-categories",
            "index.json")));
    }

    [Fact]
    public async Task ResourcePackMetadataDownloadsMatchedRemoteProjectIcon()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "matched-resource-pack.zip");
        var bytes = Encoding.UTF8.GetBytes("recognized resource pack");
        File.WriteAllBytes(path, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var thumbnailService = new RecordingThumbnailService(
            downloadedSource: "file:///cache/remote-resource-pack.png");
        using var httpClient = new HttpClient(new ModrinthMatchHandler(sha1));
        var service = new LocalResourceCategoryEnrichmentService(
            new LauncherPathProvider(TempRoot),
            httpClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
            thumbnailService: thumbnailService);

        var result = await service.ResolveMetadataAsync(
            [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ResourcePack)]);

        var metadata = Assert.Single(result).Value;
        Assert.Equal("file:///cache/remote-resource-pack.png", metadata.IconSource);
        Assert.Equal(
            [ResourceProjectCategory.Realistic],
            metadata.Categories);
        Assert.Equal(
            new ResourceProjectReference(
                ResourceProjectKind.ResourcePack,
                ResourceProjectSource.Modrinth,
                "shader-project"),
            metadata.ProjectReference);
        var project = Assert.Single(thumbnailService.DownloadedProjects);
        Assert.Equal(ResourceProjectKind.ResourcePack, project.Kind);
        Assert.Equal("https://cdn.example/shader.png", project.IconUrl);
    }

    [Fact]
    public async Task CachedResourcePackMetadataUsesCachedThumbnailWithoutNetworkOrDownload()
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, "cached-resource-pack.zip");
        var bytes = Encoding.UTF8.GetBytes("cached resource pack");
        File.WriteAllBytes(path, bytes);
        var sha1 = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        var pathProvider = new LauncherPathProvider(TempRoot);
        using (var firstClient = new HttpClient(new ModrinthMatchHandler(sha1)))
        {
            var firstService = new LocalResourceCategoryEnrichmentService(
                pathProvider,
                firstClient,
                logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
                thumbnailService: new RecordingThumbnailService(
                    downloadedSource: "file:///cache/first-download.png"));
            await firstService.ResolveMetadataAsync(
                [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ResourcePack)]);
        }

        var thumbnailService = new RecordingThumbnailService(
            cachedSource: "file:///cache/cached-resource-pack.png");
        using var rejectingClient = new HttpClient(new RejectingHandler());
        var restartedService = new LocalResourceCategoryEnrichmentService(
            pathProvider,
            rejectingClient,
            logger: NullLogger<LocalResourceCategoryEnrichmentService>.Instance,
            thumbnailService: thumbnailService);

        var result = await restartedService.ResolveCachedMetadataAsync(
            [new LocalResourceCategoryCandidate(path, ResourceProjectKind.ResourcePack)]);

        var metadata = Assert.Single(result).Value;
        Assert.Equal("file:///cache/cached-resource-pack.png", metadata.IconSource);
        Assert.Equal(ResourceProjectKind.ResourcePack, metadata.ProjectReference?.Kind);
        Assert.Empty(thumbnailService.DownloadedProjects);
        Assert.Single(thumbnailService.CachedProjects);
    }

    private sealed class ModrinthMatchHandler(string sha1) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/v2/version_files" => $"{{\"{sha1}\":{{\"project_id\":\"shader-project\"}}}}",
                "/v2/projects" => """[{"id":"shader-project","icon_url":"https://cdn.example/shader.png","categories":["fabric","fantasy","realistic","fantasy"]}]""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Directory resources must not trigger remote matching.");
    }

    private sealed class RecordingThumbnailService(
        string? cachedSource = null,
        string? downloadedSource = null) : IResourceThumbnailService
    {
        public List<ResourceProject> CachedProjects { get; } = [];

        public List<ResourceProject> DownloadedProjects { get; } = [];

        public string? TryGetCachedThumbnailSource(ResourceProject project)
        {
            CachedProjects.Add(project);
            return cachedSource;
        }

        public Task<string?> GetOrCreateThumbnailSourceAsync(
            ResourceProject project,
            CancellationToken cancellationToken = default)
        {
            DownloadedProjects.Add(project);
            return Task.FromResult(downloadedSource);
        }
    }

}
