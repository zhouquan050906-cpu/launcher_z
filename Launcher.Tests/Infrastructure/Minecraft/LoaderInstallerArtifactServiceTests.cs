/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LoaderInstallerArtifactServiceTests : TestTempDirectory
{
    [Fact]
    public async Task PlanIncludesProfileProcessorAndVersionRuntimeLibraries()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: true);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));

        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);

        Assert.Equal(
            [
                "com/example/classpath/1.0/classpath-1.0.jar",
                "com/example/input/1.0/input-1.0.jar",
                "com/example/processor/1.0/processor-1.0.jar",
                "com/example/profile/1.0/profile-1.0.jar"
            ],
            plan.PrerequisiteLibraries.Select(library => library.Artifact.RelativePath));
        Assert.Equal("com/example/runtime/1.0/runtime-1.0.jar", Assert.Single(plan.RuntimeLibraries).Artifact.RelativePath);
        Assert.Equal("com/example/output/1.0/output-1.0.jar", Assert.Single(plan.ProcessorOutputs).RelativePath);
        Assert.NotNull(plan.PrerequisiteLibraries.Single(library => library.Artifact.LibraryName == "com.example:profile:1.0").EmbeddedEntryName);
    }

    [Fact]
    public async Task ManifestStoreCapturesCompleteInstallerClosureAndRejectsDifferentIdentity()
    {
        var installerPath = await WriteInstallerAsync(includeEmbeddedExternal: true);
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Arbitrary Pack");
        Directory.CreateDirectory(versionDirectory);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);
        await service.MaterializePrerequisitesAsync(
            installerPath,
            plan,
            minecraftDirectory,
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);
        await service.MaterializeRuntimeLibrariesAsync(
            installerPath,
            plan,
            minecraftDirectory,
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);
        var output = Assert.Single(plan.ProcessorOutputs);
        var outputPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "generated");
        var identity = new GameFileLoaderIdentity(LoaderKind.Forge, "9.9.9", "77.0.1");

        await LoaderArtifactManifestStore.WriteAsync(
            versionDirectory,
            minecraftDirectory,
            identity,
            installerPath,
            plan,
            CancellationToken.None);

        var result = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity,
            CancellationToken.None);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Manifest!.Artifacts.Count);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.InstallerPrerequisite);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.RuntimeLibrary);
        Assert.Contains(result.Manifest.Artifacts, artifact => artifact.Kind == LoaderArtifactKind.ProcessorOutput);
        Assert.All(result.Manifest.Artifacts, artifact => Assert.True(MinecraftFileIntegrity.IsSha1(artifact.Sha1)));
        Assert.All(result.Manifest.Artifacts, artifact => Assert.Equal(64, artifact.Sha256.Length));
        Assert.Equal(
            GameFileVerificationLevel.SizeVerified,
            result.Manifest.Artifacts.Single(artifact => artifact.Kind == LoaderArtifactKind.ProcessorOutput)
                .VerificationLevel);

        var mismatched = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity with { LoaderVersion = "77.0.2" },
            CancellationToken.None);
        Assert.False(mismatched.IsValid);
        Assert.Contains("identity", mismatched.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--out-jar", ModpackInstallEnvironment.Client)]
    [InlineData("--jar-out", ModpackInstallEnvironment.Server)]
    public async Task ProcessorOutputOptionsDoNotMaterializeGeneratedCoordinates(
        string outputOption,
        ModpackInstallEnvironment environment)
    {
        var installerPath = await WriteProcessorOutputInstallerAsync(outputOption);
        var handler = new RecordingHandler();
        var service = new LoaderInstallerArtifactService(new HttpClient(handler));

        var plan = await service.ReadPlanAsync(installerPath, environment, CancellationToken.None);

        var expectedRelativePath = environment is ModpackInstallEnvironment.Server
            ? "net/minecraft/server/1.0/server-1.0-srg.jar"
            : "net/minecraft/client/1.0/client-1.0-srg.jar";
        Assert.Contains(plan.ProcessorOutputs, output => output.RelativePath == expectedRelativePath);
        Assert.DoesNotContain(
            plan.PrerequisiteLibraries,
            library => library.Artifact.RelativePath == expectedRelativePath);

        await service.MaterializePrerequisitesAsync(
            installerPath,
            plan,
            Path.Combine(TempRoot, environment.ToString()),
            DownloadSourcePreference.Official,
            0,
            CancellationToken.None);

        Assert.Empty(handler.RequestUris);
    }

    [Theory]
    [InlineData(ModpackInstallEnvironment.Client, true)]
    [InlineData(ModpackInstallEnvironment.Server, false)]
    public async Task PatchedOutputUsesPostInjectionHashForFinalManifest(
        ModpackInstallEnvironment environment,
        bool usePlaceholder)
    {
        var installerPath = await WritePatchedOutputInstallerAsync(environment, usePlaceholder);
        var minecraftDirectory = Path.Combine(TempRoot, $"patched-{environment}-{usePlaceholder}");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Test");
        Directory.CreateDirectory(versionDirectory);
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, environment, CancellationToken.None);
        var output = Assert.Single(plan.ProcessorOutputs);
        Assert.Null(output.TrustedSha1);
        var outputPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        const string finalContent = "profile-injected-patched-output";
        await File.WriteAllTextAsync(outputPath, finalContent);

        await service.ValidatePublishedArtifactsAsync(
            minecraftDirectory,
            plan,
            CancellationToken.None);
        var identity = new GameFileLoaderIdentity(LoaderKind.Forge, "1.17.1", "37.1.1");
        await LoaderArtifactManifestStore.WriteAsync(
            versionDirectory,
            minecraftDirectory,
            identity,
            installerPath,
            plan,
            CancellationToken.None);

        var result = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity,
            CancellationToken.None);
        var artifact = Assert.Single(result.Manifest!.Artifacts);
        Assert.Equal($"libraries/{output.RelativePath}", artifact.RelativePath);
        Assert.Equal(Sha1(finalContent), artifact.Sha1, ignoreCase: true);
        Assert.Equal(64, artifact.Sha256.Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(finalContent), artifact.Size);
        Assert.Equal(GameFileVerificationLevel.SizeVerified, artifact.VerificationLevel);
    }

    [Fact]
    public async Task NonPatchedProcessorOutputRetainsDeclaredHashValidation()
    {
        var installerPath = await WriteHashedSrgOutputInstallerAsync();
        var minecraftDirectory = Path.Combine(TempRoot, "hashed-srg");
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("external")));
        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);
        var output = Assert.Single(plan.ProcessorOutputs);
        Assert.Equal(Sha1("expected-srg"), output.TrustedSha1, ignoreCase: true);
        var outputPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "different-srg");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ValidatePublishedArtifactsAsync(
                minecraftDirectory,
                plan,
                CancellationToken.None));

        await File.WriteAllTextAsync(outputPath, "expected-srg");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "Forge Test");
        Directory.CreateDirectory(versionDirectory);
        var identity = new GameFileLoaderIdentity(LoaderKind.Forge, "1.17.1", "37.1.1");
        await LoaderArtifactManifestStore.WriteAsync(
            versionDirectory,
            minecraftDirectory,
            identity,
            installerPath,
            plan,
            CancellationToken.None);

        var result = await LoaderArtifactManifestStore.ReadAsync(
            versionDirectory,
            identity,
            CancellationToken.None);
        Assert.Equal(
            GameFileVerificationLevel.HashVerified,
            Assert.Single(result.Manifest!.Artifacts).VerificationLevel);
    }

    [Fact]
    public async Task LegacyRuntimeNativeLibraryDoesNotIncludeNonexistentMainArtifact()
    {
        var installerPath = await WriteLegacyNativeInstallerAsync();
        var service = new LoaderInstallerArtifactService(new HttpClient(new TestHandler("native")));

        var plan = await service.ReadPlanAsync(installerPath, CancellationToken.None);

        var library = Assert.Single(plan.RuntimeLibraries);
        Assert.Equal(
            OperatingSystem.IsWindows()
                ? "org/lwjgl/lwjgl/lwjgl-platform/2.9.0/lwjgl-platform-2.9.0-natives-windows.jar"
                : OperatingSystem.IsMacOS()
                    ? "org/lwjgl/lwjgl/lwjgl-platform/2.9.0/lwjgl-platform-2.9.0-natives-osx.jar"
                    : "org/lwjgl/lwjgl/lwjgl-platform/2.9.0/lwjgl-platform-2.9.0-natives-linux.jar",
            library.Artifact.RelativePath);
    }

    private async Task<string> WriteInstallerAsync(bool includeEmbeddedExternal)
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"installer-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": {
                "INPUT": { "client": "[com.example:input:1.0]" },
                "OUTPUT": { "client": "[com.example:output:1.0]" }
              },
              "libraries": [
                {
                  "name": "com.example:profile:1.0",
                  "url": "https://example.test/",
                  "downloads": { "artifact": { "path": "com/example/profile/1.0/profile-1.0.jar", "sha1": "{{Sha1("external")}}", "size": 8 } }
                }
              ],
              "processors": [
                {
                  "sides": ["client"],
                  "jar": "com.example:processor:1.0",
                  "classpath": ["com.example:classpath:1.0"],
                  "args": ["--input", "{INPUT}", "--output", "{OUTPUT}"]
                },
                {
                  "sides": ["client"],
                  "jar": "com.example:processor:1.0",
                  "args": ["--consume-generated", "{OUTPUT}"]
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """
            {
              "libraries": [
                { "name": "com.example:runtime:1.0", "url": "https://example.test/" }
              ]
            }
            """);
        if (includeEmbeddedExternal)
            WriteEntry(archive, "maven/com/example/profile/1.0/profile-1.0.jar", "external");
        return installerPath;
    }

    private async Task<string> WriteProcessorOutputInstallerAsync(string outputOption)
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"processor-output-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": {
                "MC_SRG": {
                  "client": "[net.minecraft:client:1.0:srg]",
                  "server": "[net.minecraft:server:1.0:srg]"
                }
              },
              "processors": [
                {
                  "jar": "com.example:processor:1.0",
                  "args": ["{{outputOption}}", "{MC_SRG}"]
                },
                {
                  "jar": "com.example:processor:1.0",
                  "args": ["--clean", "{MC_SRG}"]
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """{ "libraries": [] }""");
        WriteEntry(archive, "maven/com/example/processor/1.0/processor-1.0.jar", "processor");
        return installerPath;
    }

    private async Task<string> WritePatchedOutputInstallerAsync(
        ModpackInstallEnvironment environment,
        bool usePlaceholder)
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"patched-output-{Guid.NewGuid():N}.jar");
        var coordinate = environment is ModpackInstallEnvironment.Server
            ? "net.minecraftforge:forge:1.17.1-37.1.1:server"
            : "net.minecraftforge:forge:1.17.1-37.1.1:client";
        var outputExpression = usePlaceholder ? "{PATCHED}" : $"[{coordinate}]";
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": {
                "PATCHED": {
                  "client": "[net.minecraftforge:forge:1.17.1-37.1.1:client]",
                  "server": "[net.minecraftforge:forge:1.17.1-37.1.1:server]"
                },
                "PATCHED_SHA": {
                  "client": "'{{Sha1("processor-stage-client")}}'",
                  "server": "'{{Sha1("processor-stage-server")}}'"
                }
              },
              "processors": [
                {
                  "outputs": {
                    "{{outputExpression}}": "{PATCHED_SHA}"
                  }
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """{ "libraries": [] }""");
        return installerPath;
    }

    private async Task<string> WriteHashedSrgOutputInstallerAsync()
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"hashed-srg-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", $$"""
            {
              "data": {
                "MC_SRG": {
                  "client": "[net.minecraft:client:1.17.1:srg]"
                },
                "MC_SRG_SHA": {
                  "client": "'{{Sha1("expected-srg")}}'"
                }
              },
              "processors": [
                {
                  "outputs": {
                    "{MC_SRG}": "{MC_SRG_SHA}"
                  }
                }
              ]
            }
            """);
        WriteEntry(archive, "version.json", """{ "libraries": [] }""");
        return installerPath;
    }

    private async Task<string> WriteLegacyNativeInstallerAsync()
    {
        Directory.CreateDirectory(TempRoot);
        var installerPath = Path.Combine(TempRoot, $"legacy-native-{Guid.NewGuid():N}.jar");
        await using var stream = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "install_profile.json", "{}");
        WriteEntry(archive, "version.json", """
            {
              "libraries": [
                {
                  "name": "org.lwjgl.lwjgl:lwjgl-platform:2.9.0",
                  "natives": {
                    "linux": "natives-linux",
                    "windows": "natives-windows",
                    "osx": "natives-osx"
                  }
                }
              ]
            }
            """);
        return installerPath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string Sha1(string value) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class TestHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(content)
            });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request
            });
        }
    }
}
