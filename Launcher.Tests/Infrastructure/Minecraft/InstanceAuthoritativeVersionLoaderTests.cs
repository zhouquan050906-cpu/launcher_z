/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class InstanceAuthoritativeVersionLoaderTests : TestTempDirectory
{
    private const string FabricMainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient";

    [Theory]
    [InlineData("net.fabricmc.loader.impl.launch.knot.KnotClient", "net.fabricmc:fabric-loader:0.19.3")]
    [InlineData("cpw.mods.bootstraplauncher.BootstrapLauncher", "net.neoforged:neoforge:1.0.0")]
    public async Task SameNamedLocalVersionRemainsAuthoritativeDuringFileInstallation(
        string loaderMainClass,
        string loaderLibrary)
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var jsonPath = WriteVersionJson(
            minecraftDirectory,
            "26.2",
            $$"""
            {
              "id": "26.2",
              "type": "release",
              "mainClass": "{{loaderMainClass}}",
              "libraries": [
                {
                  "name": "{{loaderLibrary}}",
                  "rules": [ { "action": "disallow" } ]
                }
              ],
              "arguments": { "game": [], "jvm": [] }
            }
            """);
        var originalBytes = await File.ReadAllBytesAsync(jsonPath);

        await new FinalVersionInstaller().InstallAsync(
            minecraftDirectory,
            "26.2",
            DownloadSourcePreference.Official,
            progress: null,
            CancellationToken.None);

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(jsonPath));
        var launcher = VanillaLoaderProvider.CreateLauncher(minecraftDirectory, progress: null);
        var version = await launcher.GetVersionAsync("26.2");
        Assert.Equal(loaderMainClass, version.MainClass);
        Assert.Contains(loaderLibrary, await File.ReadAllTextAsync(jsonPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameNamedLocalVersionBuildsLoaderCommandWithoutChangingJson()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var jsonPath = WriteVersionJson(
            minecraftDirectory,
            "26.2",
            $$"""
            {
              "id": "26.2",
              "type": "release",
              "mainClass": "{{FabricMainClass}}",
              "libraries": [],
              "arguments": {
                "game": [],
                "jvm": [ "-cp", "${classpath}" ]
              }
            }
            """);
        var originalBytes = await File.ReadAllBytesAsync(jsonPath);
        var launcher = VanillaLoaderProvider.CreateLauncher(minecraftDirectory, progress: null);

        using var process = await launcher.BuildProcessAsync(
            "26.2",
            new MLaunchOption { JavaPath = "java.exe" },
            CancellationToken.None);

        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(jsonPath));
        Assert.Contains(FabricMainClass, process.StartInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedLocalVersionDoesNotFallBackToRemoteMetadata()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var jsonPath = WriteVersionJson(minecraftDirectory, "26.2", "{ not-json");
        var originalBytes = await File.ReadAllBytesAsync(jsonPath);
        var remoteRequested = false;
        var path = new MinecraftPath(minecraftDirectory);
        var loader = new InstanceAuthoritativeVersionLoader(
            path,
            (_, _) =>
            {
                remoteRequested = true;
                throw new InvalidOperationException("Remote metadata must not be used.");
            });
        var versions = await loader.GetVersionMetadatasAsync();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            versions.GetAndSaveVersionAsync("26.2", path, CancellationToken.None));

        Assert.False(remoteRequested);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(jsonPath));
    }

    [Fact]
    public async Task MissingOfficialParentIsResolvedInMemoryWithoutWritingEitherVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var childPath = WriteVersionJson(
            minecraftDirectory,
            "fabric-child",
            $$"""
            {
              "id": "fabric-child",
              "inheritsFrom": "26.2",
              "mainClass": "{{FabricMainClass}}",
              "libraries": []
            }
            """);
        var originalChildBytes = await File.ReadAllBytesAsync(childPath);
        var path = new MinecraftPath(minecraftDirectory);
        var remoteRequestCount = 0;
        var loader = new InstanceAuthoritativeVersionLoader(
            path,
            (versionName, _) =>
            {
                remoteRequestCount++;
                return Task.FromResult(
                    JsonNode.Parse(
                        $$"""
                        {
                          "id": "{{versionName}}",
                          "type": "release",
                          "mainClass": "net.minecraft.client.main.Main",
                          "libraries": []
                        }
                        """)!.AsObject());
            });
        var versions = await loader.GetVersionMetadatasAsync();

        var version = await versions.GetAndSaveVersionAsync("fabric-child", path, CancellationToken.None);
        var repeatedVersion = await versions.GetAndSaveVersionAsync("fabric-child", path, CancellationToken.None);

        Assert.Equal(FabricMainClass, version.MainClass);
        Assert.Equal(FabricMainClass, repeatedVersion.MainClass);
        Assert.NotNull(version.ParentVersion);
        Assert.Equal("26.2", version.ParentVersion!.Id);
        Assert.Equal(1, remoteRequestCount);
        Assert.Equal(originalChildBytes, await File.ReadAllBytesAsync(childPath));
        Assert.False(File.Exists(Path.Combine(minecraftDirectory, "versions", "26.2", "26.2.json")));
    }

    [Fact]
    public async Task LoaderInstallerJavaProvisioningUsesOfficialMetadataInMemory()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var jsonPath = WriteVersionJson(
            minecraftDirectory,
            "26.2",
            $$"""
            {
              "id": "26.2",
              "type": "release",
              "mainClass": "{{FabricMainClass}}",
              "libraries": []
            }
            """);
        var originalBytes = await File.ReadAllBytesAsync(jsonPath);
        var handler = new OfficialVersionMetadataHandler();
        using var httpClient = new HttpClient(handler);
        var service = new CmlLibJavaRuntimeProvisioningService(httpClient);

        await ((ILoaderInstallerJavaRuntimeProvisioner)service).ProvisionAsync(
            new LoaderInstallerJavaRuntimeRequest(
                "26.2",
                "26.2",
                LoaderKind.Fabric,
                "0.19.3",
                minecraftDirectory,
                DownloadSourcePreference.Official,
                DownloadSpeedLimitMbPerSecond: 0),
            CancellationToken.None);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(jsonPath));
        Assert.False(File.Exists(Path.Combine(minecraftDirectory, "versions", "version_manifest_v2.json")));
    }

    private static string WriteVersionJson(string minecraftDirectory, string versionName, string content)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        var path = Path.Combine(versionDirectory, $"{versionName}.json");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private sealed class OfficialVersionMetadataHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsoluteUri switch
            {
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json" => """
                    {
                      "versions": [
                        {
                          "id": "26.2",
                          "url": "https://example.test/26.2.json"
                        }
                      ]
                    }
                    """,
                "https://example.test/26.2.json" => """
                    {
                      "id": "26.2",
                      "type": "release",
                      "mainClass": "net.minecraft.client.main.Main",
                      "libraries": []
                    }
                    """,
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            });
        }
    }
}
