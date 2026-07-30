/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.FileSystem;

public sealed class BoundedLocalContentSnapshotCacheTests : TestTempDirectory
{
    [Fact]
    public void GetOrCreatePromotesEntriesAndEvictsLeastRecentlyUsed()
    {
        var cache = new BoundedLocalContentSnapshotCache<string, object>(capacity: 2);
        var firstPath = Path.Combine(TempRoot, "first");
        var secondPath = Path.Combine(TempRoot, "second");
        var thirdPath = Path.Combine(TempRoot, "third");
        var first = cache.GetOrCreate(firstPath);
        var second = cache.GetOrCreate(secondPath);

        Assert.Same(first, cache.GetOrCreate(firstPath));
        cache.GetOrCreate(thirdPath);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet(firstPath, out var retainedFirst));
        Assert.Same(first, retainedFirst);
        Assert.False(cache.TryGet(secondPath, out _));
        Assert.True(cache.TryGet(thirdPath, out _));
        Assert.NotSame(second, cache.GetOrCreate(secondPath));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void PathsAreNormalizedAndComparedIgnoringCase()
    {
        var cache = new BoundedLocalContentSnapshotCache<string, object>();
        var directory = Path.Combine(TempRoot, "MixedCase");
        var first = cache.GetOrCreate(directory + Path.DirectorySeparatorChar);

        var second = cache.GetOrCreate(directory.ToUpperInvariant());

        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void RemoveReleasesEntryAndRepeatedRemoveIsSafe()
    {
        var cache = new BoundedLocalContentSnapshotCache<string, object>();
        var directory = Path.Combine(TempRoot, "removed");
        var original = cache.GetOrCreate(directory);

        Assert.True(cache.Remove(directory));
        Assert.False(cache.Remove(directory));
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(directory, out _));
        Assert.NotSame(original, cache.GetOrCreate(directory));
    }

    [Fact]
    public void CapacityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundedLocalContentSnapshotCache<string, object>(capacity: 0));
    }
}
