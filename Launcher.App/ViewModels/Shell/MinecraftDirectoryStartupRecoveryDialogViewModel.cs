/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.Application.Services;

namespace Launcher.App.ViewModels.Shell;

public sealed partial class MinecraftDirectoryStartupRecoveryDialogViewModel : ObservableObject
{
    private MinecraftDirectoryStartupRecoveryResult? pendingRecovery;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string message = string.Empty;

    public void Prime(MinecraftDirectoryStartupRecoveryResult? recovery)
    {
        pendingRecovery = recovery;
        IsOpen = false;
        Message = string.Empty;
    }

    public void ShowPending()
    {
        var recovery = pendingRecovery;
        if (recovery is null)
            return;

        pendingRecovery = null;
        Message = string.Format(
            recovery.UsedDefaultDirectory
                ? Strings.Dialog_MinecraftDirectoryStartupDefaultMessageFormat
                : Strings.Dialog_MinecraftDirectoryStartupSwitchedMessageFormat,
            recovery.InvalidDirectory,
            recovery.SelectedDirectory);
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }
}
