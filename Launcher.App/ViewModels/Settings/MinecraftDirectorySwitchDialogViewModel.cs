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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class MinecraftDirectorySwitchDialogViewModel : ObservableObject
{
    private readonly Func<string> getCurrentDirectory;
    private readonly Func<bool> canChangeDirectory;
    private readonly Func<bool> isChangeBlockedByActiveTasks;
    private readonly Func<string, Task<bool>> switchDirectory;
    private bool suppressSelectionChanged;
    private SettingsMinecraftDirectoryItem? acceptedSelection;

    internal MinecraftDirectorySwitchDialogViewModel(
        ObservableCollection<SettingsMinecraftDirectoryItem> directories,
        Func<string> getCurrentDirectory,
        Func<bool> canChangeDirectory,
        Func<bool> isChangeBlockedByActiveTasks,
        Func<string, Task<bool>> switchDirectory)
    {
        Directories = directories;
        this.getCurrentDirectory = getCurrentDirectory;
        this.canChangeDirectory = canChangeDirectory;
        this.isChangeBlockedByActiveTasks = isChangeBlockedByActiveTasks;
        this.switchDirectory = switchDirectory;
    }

    public ObservableCollection<SettingsMinecraftDirectoryItem> Directories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SettingsMinecraftDirectoryItem? selectedDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isBusy;

    public bool IsChangeBlockedByActiveTasks => isChangeBlockedByActiveTasks();

    public bool CanConfirm =>
        IsOpen
        && !IsBusy
        && canChangeDirectory()
        && SelectedDirectory is { IsAvailable: true } selected
        && !MinecraftDirectoryPath.Equals(selected.DirectoryPath, getCurrentDirectory());

    public bool CanCancel => IsOpen && !IsBusy;

    public void Open()
    {
        IsOpen = true;
        RestoreCurrentSelection();
        NotifyDirectoryChangeStateChanged();
    }

    public void SynchronizeWithCurrentDirectory()
    {
        RestoreCurrentSelection();
        NotifyDirectoryChangeStateChanged();
    }

    public void NotifyDirectoryChangeStateChanged()
    {
        if (IsChangeBlockedByActiveTasks)
            RestoreCurrentSelection();

        OnPropertyChanged(nameof(IsChangeBlockedByActiveTasks));
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDirectoryChanged(SettingsMinecraftDirectoryItem? value)
    {
        if (suppressSelectionChanged)
            return;

        if (value is not null
            && (IsChangeBlockedByActiveTasks || !value.IsAvailable))
        {
            SetSelectedDirectory(acceptedSelection ?? FindCurrentDirectory());
            return;
        }

        acceptedSelection = value;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsOpen = false;
        RestoreCurrentSelection();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var target = SelectedDirectory;
        if (target is null || !CanConfirm)
            return;

        IsBusy = true;
        var succeeded = false;
        try
        {
            succeeded = await switchDirectory(target.DirectoryPath);
            if (succeeded)
                IsOpen = false;
        }
        finally
        {
            IsBusy = false;
            RestoreCurrentSelection();
            NotifyDirectoryChangeStateChanged();
        }
    }

    private void RestoreCurrentSelection()
    {
        var current = FindCurrentDirectory();
        acceptedSelection = current;
        SetSelectedDirectory(current);
    }

    private SettingsMinecraftDirectoryItem? FindCurrentDirectory()
    {
        var currentDirectory = getCurrentDirectory();
        return Directories.FirstOrDefault(item =>
            MinecraftDirectoryPath.Equals(item.DirectoryPath, currentDirectory));
    }

    private void SetSelectedDirectory(SettingsMinecraftDirectoryItem? item)
    {
        suppressSelectionChanged = true;
        try
        {
            SelectedDirectory = item;
        }
        finally
        {
            suppressSelectionChanged = false;
        }
    }
}
