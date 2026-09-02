/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class LauncherDataDirectoryProbeTests : TestTempDirectory
{
    private readonly LauncherDataDirectoryProbe probe = new();

    [Fact]
    public async Task AWritableDirectoryPassesAndLeavesNoProbeFiles()
    {
        Assert.True(await probe.IsWritableAsync(TempRoot));
        Assert.Empty(Directory.GetFiles(TempRoot));
    }

    [Fact]
    public async Task AMissingDirectoryIsCreatedAndPasses()
    {
        var directory = Path.Combine(TempRoot, "nested", "data");

        Assert.True(await probe.IsWritableAsync(directory));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task APathOccupiedByAFileFails()
    {
        Directory.CreateDirectory(TempRoot);
        var occupied = Path.Combine(TempRoot, "not-a-directory");
        await File.WriteAllTextAsync(occupied, "x");

        Assert.False(await probe.IsWritableAsync(occupied));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnUnsetDirectoryFails(string? directory) =>
        Assert.False(await probe.IsWritableAsync(directory));
}
