/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using Launcher.App.ViewModels.Shared;

namespace Launcher.Tests.ViewModels.Shared;

public sealed class ObservableCollectionExtensionsTests
{
    [Fact]
    public void SynchronizeByKeyDoesNotRaiseEventsForSameSnapshot()
    {
        var first = new Item("first");
        var second = new Item("second");
        var collection = new ObservableCollection<Item> { first, second };
        var eventCount = 0;
        collection.CollectionChanged += (_, _) => eventCount++;

        var changed = collection.SynchronizeByKey(
            new[] { first, second },
            static item => item.Key,
            StringComparer.OrdinalIgnoreCase);

        Assert.False(changed);
        Assert.Equal(0, eventCount);
    }

    [Fact]
    public void SynchronizeByKeyReplacesOnlyChangedItem()
    {
        var first = new Item("first");
        var second = new Item("second");
        var replacement = new Item("second");
        var collection = new ObservableCollection<Item> { first, second };
        var eventCount = 0;
        collection.CollectionChanged += (_, _) => eventCount++;

        var changed = collection.SynchronizeByKey(
            new[] { first, replacement },
            static item => item.Key,
            StringComparer.OrdinalIgnoreCase);

        Assert.True(changed);
        Assert.Same(first, collection[0]);
        Assert.Same(replacement, collection[1]);
        Assert.Equal(1, eventCount);
    }

    private sealed record Item(string Key);
}
