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

using Launcher.App.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Settings;

public sealed class SettingsMinecraftDirectoryItem : ObservableObject
{
    private string displayName;
    private bool isAvailable;
    private bool canRemove;

    public SettingsMinecraftDirectoryItem(
        string displayName,
        string directoryPath,
        bool isAvailable,
        bool canRemove)
    {
        this.displayName = displayName;
        DirectoryPath = directoryPath;
        this.isAvailable = isAvailable;
        this.canRemove = canRemove;
    }

    public string DisplayName
    {
        get => displayName;
        private set => SetProperty(ref displayName, value);
    }

    public string DirectoryPath { get; }

    public bool IsAvailable
    {
        get => isAvailable;
        private set
        {
            if (SetProperty(ref isAvailable, value))
                OnPropertyChanged(nameof(AvailabilityText));
        }
    }

    public bool CanRemove
    {
        get => canRemove;
        private set => SetProperty(ref canRemove, value);
    }

    public string AvailabilityText => IsAvailable
        ? string.Empty
        : Strings.Settings_MinecraftDirectoryUnavailable;

    public void Update(string newDisplayName, bool newIsAvailable, bool newCanRemove)
    {
        DisplayName = newDisplayName;
        IsAvailable = newIsAvailable;
        CanRemove = newCanRemove;
    }

    /// <summary>可用性由后台探测单独维护，因此需要独立于名称与可移除状态更新。</summary>
    public void SetAvailability(bool newIsAvailable)
    {
        IsAvailable = newIsAvailable;
    }
}
