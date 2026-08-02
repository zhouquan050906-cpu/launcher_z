/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class JavaRuntimeCompatibilityTests
{
    [Theory]
    [InlineData("1.8.0_320", 8, 0, 320, 0)]
    public void JavaVersionParserNormalizesLegacyAndModernFormats(
        string value,
        int major,
        int minor,
        int patch,
        int build)
    {
        Assert.True(JavaVersionNumber.TryParse(value, out var parsed));
        Assert.Equal(new JavaVersionNumber(major, minor, patch, build), parsed);
    }

    [Theory]
    [InlineData("1.12.2", "14.23.5.2860", "17.0.12", false)]
    public void ForgeCompatibilityRulesApplyAtDocumentedBoundaries(
        string minecraftVersion,
        string forgeVersion,
        string javaVersion,
        bool expectedCompatible)
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            minecraftVersion,
            LoaderKind.Forge,
            forgeVersion,
            metadataMajorVersion: null);

        Assert.Equal(expectedCompatible, requirement.IsCompatible(CreateRuntime(javaVersion)));
    }

    [Theory]
    [InlineData("1.20.1", "20.1.137", "21.0.5", true)]
    public void NeoForgeCompatibilityRulesApplyAtDocumentedBoundaries(
        string minecraftVersion,
        string neoForgeVersion,
        string javaVersion,
        bool expectedCompatible)
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            minecraftVersion,
            LoaderKind.NeoForge,
            neoForgeVersion,
            metadataMajorVersion: 17);

        Assert.Equal(expectedCompatible, requirement.IsCompatible(CreateRuntime(javaVersion)));
    }

    [Fact]
    public void PatchBoundRejectsRuntimeWhoseFullVersionCannotBeVerified()
    {
        var requirement = JavaRuntimeCompatibilityResolver.Resolve(
            "1.16.5",
            LoaderKind.Forge,
            "36.2.25",
            metadataMajorVersion: 8);
        var runtime = CreateRuntime(version: null, majorVersion: 8);

        Assert.False(requirement.IsCompatible(runtime));
    }

    private static JavaRuntimeInfo CreateRuntime(
        string? version,
        int? majorVersion = null,
        string architecture = "x64",
        string path = @"C:\Java\runtime\bin\java.exe")
    {
        var parsedMajorVersion = majorVersion
            ?? (JavaVersionNumber.TryParse(version, out var parsed) ? parsed.Major : null);
        return new JavaRuntimeInfo(
            parsedMajorVersion is int major ? $"Java {major}" : "Java",
            version,
            parsedMajorVersion,
            architecture,
            path,
            Path.GetDirectoryName(Path.GetDirectoryName(path)) ?? string.Empty,
            "Test");
    }

}
