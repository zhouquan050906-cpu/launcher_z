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
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel
{
public void Dispose()
    {
        persistence.InstanceSaved -= Persistence_InstanceSaved;
        General.DeleteInstanceRequested -= General_DeleteInstanceRequested;
        General.Dispose();
        Launch.Dispose();
        Java.Dispose();
        persistence.Dispose();
    }

    partial void OnSelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        ApplySelectedInstanceChanged(value);
    }

    partial void OnSelectedSectionChanged(GameSettingsDetailSectionItem? value)
    {
        var previousSectionViewModel = CurrentSectionViewModel;
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsLaunchSection));
        OnPropertyChanged(nameof(IsJavaSection));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionPlaceholderBody));
        previousSectionViewModel?.OnSectionDeactivated();
        CurrentSectionViewModel = value?.Id?.ToLowerInvariant() switch
        {
            "general" => General,
            "launch" => Launch,
            "java" => Java,
            "mod_management" => ModManagement,
            "saves" => SaveManagement,
            "resource_packs" => ResourcePackManagement,
            "shaders" => ShaderPackManagement,
            "backup" => Backup,
            "export" => Export,
            _ => Placeholder
        };
        ActivateCurrentSection();
    }

    partial void OnCurrentSectionViewModelChanged(GameSettingsDetailsSectionViewModelBase? value)
    {
        OnPropertyChanged(nameof(ScrollSectionViewModel));
        OnPropertyChanged(nameof(FullViewportSectionViewModel));
        OnPropertyChanged(nameof(NonRetainedFullViewportSectionViewModel));
        OnPropertyChanged(nameof(IsModManagementSection));
        OnPropertyChanged(nameof(IsSaveManagementSection));
        OnPropertyChanged(nameof(IsResourcePackManagementSection));
        OnPropertyChanged(nameof(IsShaderPackManagementSection));
    }
}
