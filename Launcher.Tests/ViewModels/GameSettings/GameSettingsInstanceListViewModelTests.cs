/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.ViewModels.GameSettings;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class GameSettingsInstanceListViewModelTests
{
    [Fact]
    public void ApplyingSameCatalogDoesNotReplaceItemOrRaiseDisplayNotifications()
    {
        var viewModel = new GameSettingsInstanceListViewModel();
        var first = CreateInstance("instance-a", "Instance A");
        Assert.True(viewModel.ApplyInstanceCatalog([first], 1, playEntranceAnimation: true));
        var item = Assert.Single(viewModel.AllInstances);
        var notifications = new List<string?>();
        item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        var changed = viewModel.ApplyInstanceCatalog(
            [CreateInstance("instance-a", "Instance A")],
            2,
            playEntranceAnimation: true);

        Assert.False(changed);
        Assert.Same(item, Assert.Single(viewModel.AllInstances));
        Assert.Empty(notifications);
        Assert.Equal(1, viewModel.EntranceAnimationToken);
    }

    [Fact]
    public void ApplyingChangedCatalogUpdatesOnlyStableMatchingItem()
    {
        var viewModel = new GameSettingsInstanceListViewModel();
        viewModel.ApplyInstanceCatalog(
            [
                CreateInstance("instance-a", "Before"),
                CreateInstance("instance-b", "Unchanged")
            ],
            1,
            playEntranceAnimation: false);
        var changedItem = viewModel.AllInstances[0];
        var unchangedItem = viewModel.AllInstances[1];
        var changedNotifications = new List<string?>();
        var unchangedNotifications = new List<string?>();
        changedItem.PropertyChanged += (_, args) => changedNotifications.Add(args.PropertyName);
        unchangedItem.PropertyChanged += (_, args) => unchangedNotifications.Add(args.PropertyName);

        var changed = viewModel.ApplyInstanceCatalog(
            [
                CreateInstance("instance-a", "After"),
                CreateInstance("instance-b", "Unchanged")
            ],
            2,
            playEntranceAnimation: false);

        Assert.True(changed);
        Assert.Same(changedItem, viewModel.AllInstances[0]);
        Assert.Same(unchangedItem, viewModel.AllInstances[1]);
        Assert.Equal("After", changedItem.Name);
        Assert.Contains(nameof(GameSettingsInstanceItem.Name), changedNotifications);
        Assert.Empty(unchangedNotifications);
    }

    private static GameInstance CreateInstance(string id, string name)
    {
        return new GameInstance
        {
            Id = id,
            Name = name,
            MinecraftVersion = "1.21.4",
            VersionName = "1.21.4",
            VersionType = "release",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = Path.Combine("versions", id),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
    }
}
