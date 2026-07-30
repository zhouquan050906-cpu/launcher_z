/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class LauncherStateMonitorTests : TestTempDirectory
{
    [Theory]
    [InlineData("instance-a/instance-a.json")]
    [InlineData("instance-a/custom-name.json")]
    [InlineData("instance-a/BHL/instance-settings.json")]
    [InlineData("instance-a/bhl/INSTANCE-SETTINGS.JSON")]
    public void RelevantInstanceMetadataPathsInvalidateCatalog(string relativePath)
    {
        var versions = Path.Combine(TempRoot, "versions");
        var fullPath = Path.Combine(
            versions,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(LauncherStateMonitor.IsRelevantMetadataPath(versions, fullPath));
    }

    [Theory]
    [InlineData("instance-a/mods/example.json")]
    [InlineData("instance-a/saves/world/level.json")]
    [InlineData("instance-a/resourcepacks/pack.json")]
    [InlineData("instance-a/shaderpacks/shader.json")]
    [InlineData("instance-a/logs/latest.json")]
    [InlineData("instance-a/BHL/loader-artifact-manifest.json")]
    [InlineData("instance-a/instance-a.jar")]
    [InlineData("root.json")]
    public void InstanceContentAndUnrelatedMetadataDoNotInvalidateCatalog(string relativePath)
    {
        var versions = Path.Combine(TempRoot, "versions");
        var fullPath = Path.Combine(
            versions,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.False(LauncherStateMonitor.IsRelevantMetadataPath(versions, fullPath));
    }

    [Fact]
    public void StorageDirectoryNameIsUsedForInstanceSettings()
    {
        var versions = Path.Combine(TempRoot, "versions");
        var fullPath = Path.Combine(
            versions,
            "instance-a",
            LauncherApplicationIdentity.StorageDirectoryName,
            "instance-settings.json");

        Assert.True(LauncherStateMonitor.IsRelevantMetadataPath(versions, fullPath));
    }
}
