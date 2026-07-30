/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Home;

public sealed partial class HomeLaunchInstanceItem : ObservableObject
{
    private InstanceCatalogEntrySnapshot catalogSnapshot;

    public HomeLaunchInstanceItem(GameInstance instance, string versionType = "")
    {
        Instance = instance;
        VersionType = MinecraftVersionIconResolver.NormalizeVersionType(versionType);
        catalogSnapshot = InstanceCatalogEntrySnapshot.Create(instance);
    }

    public GameInstance Instance { get; private set; }

    public string VersionType { get; private set; }

    public string Name => GameInstanceDisplayFormatter.GetName(Instance);

    public string MinecraftVersion => GameInstanceDisplayFormatter.GetMinecraftVersion(Instance);

    public string VersionName => GameInstanceDisplayFormatter.GetVersionName(Instance);

    public string LoaderLabel => GameInstanceDisplayFormatter.GetLoaderLabel(Instance.Loader);

    public string LoaderVersionDisplay => LoaderVersionDisplayFormatter.Format(Instance.Loader, Instance.LoaderVersion);

    public string Subtitle => GameInstanceDisplayFormatter.GetSubtitle(Instance);

    public string IconSource => MinecraftVersionIconResolver.Resolve(Instance, VersionType, MinecraftVersion);

    [ObservableProperty]
    private bool isSelected;

    public bool Update(GameInstance instance, string versionType)
    {
        var normalizedVersionType = MinecraftVersionIconResolver.NormalizeVersionType(versionType);
        var nextSnapshot = InstanceCatalogEntrySnapshot.Create(instance);
        var changed = catalogSnapshot != nextSnapshot
            || !string.Equals(VersionType, normalizedVersionType, StringComparison.OrdinalIgnoreCase);

        Instance = instance;
        VersionType = normalizedVersionType;
        catalogSnapshot = nextSnapshot;
        if (!changed)
            return false;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(MinecraftVersion));
        OnPropertyChanged(nameof(VersionName));
        OnPropertyChanged(nameof(LoaderLabel));
        OnPropertyChanged(nameof(LoaderVersionDisplay));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IconSource));
        return true;
    }
}

