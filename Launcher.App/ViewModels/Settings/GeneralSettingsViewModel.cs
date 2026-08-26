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
using Launcher.App.Logging;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : SettingsSectionViewModelBase, IDisposable
{
    private readonly IStatusService statusService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IMinecraftDirectoryFileSystem minecraftDirectoryFileSystem;
    private readonly MinecraftDirectoryManagementService minecraftDirectoryManagementService;
    private readonly DownloadTasksPageViewModel? downloadTasksPage;
    private readonly ILauncherLogLevelController? logLevelController;
    private readonly ILogger logger;
    private bool suppressMinecraftDirectorySelectionChanged;
    private bool isChangingMinecraftDirectory;
    private string? minecraftDirectoryPendingNamePath;
    private bool isMinecraftDirectoryNameDialogForAdd;

    internal GeneralSettingsViewModel(
        SettingsPersistenceCoordinator persistence,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IInstanceFolderService instanceFolderService,
        IMinecraftDirectoryFileSystem minecraftDirectoryFileSystem,
        MinecraftDirectoryManagementService minecraftDirectoryManagementService,
        DownloadTasksPageViewModel? downloadTasksPage,
        ILauncherLogLevelController? logLevelController,
        ILogger logger)
        : base(persistence)
    {
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.instanceFolderService = instanceFolderService;
        this.minecraftDirectoryFileSystem = minecraftDirectoryFileSystem;
        this.minecraftDirectoryManagementService = minecraftDirectoryManagementService;
        this.downloadTasksPage = downloadTasksPage;
        this.logLevelController = logLevelController;
        this.logger = logger;
        MinecraftDirectorySwitchDialog = new MinecraftDirectorySwitchDialogViewModel(
            MinecraftDirectories,
            () => MinecraftDirectory,
            () => CanChangeMinecraftDirectory,
            () => IsMinecraftDirectoryChangeBlocked,
            SelectMinecraftDirectoryAsync);
        if (downloadTasksPage is not null)
            downloadTasksPage.ActivityChanged += DownloadTasksPage_ActivityChanged;
    }

    public event EventHandler<SettingsMinecraftDirectoryChangedEventArgs>? MinecraftDirectoryChanged;

    public bool CanChangeMinecraftDirectory =>
        !isChangingMinecraftDirectory && downloadTasksPage?.HasActiveOperations != true;

    public bool CanAddMinecraftDirectory =>
        downloadTasksPage?.HasActiveOperations != true;

    public bool IsMinecraftDirectoryChangeBlocked =>
        downloadTasksPage?.HasActiveOperations == true;

    [ObservableProperty]
    private string minecraftDirectory = string.Empty;

    [ObservableProperty]
    private string launcherLogDirectory = string.Empty;

    [ObservableProperty]
    private bool diagnosticLoggingEnabled;

    [ObservableProperty]
    private SettingsMinecraftDirectoryItem? selectedMinecraftDirectory;

    [ObservableProperty]
    private bool isRemoveMinecraftDirectoryDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinecraftDirectoryNameDialogTitle))]
    [NotifyPropertyChangedFor(nameof(MinecraftDirectoryNameDialogConfirmButtonText))]
    [NotifyPropertyChangedFor(nameof(CanConfirmMinecraftDirectoryName))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMinecraftDirectoryNameCommand))]
    private bool isMinecraftDirectoryNameDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMinecraftDirectoryNameInvalid))]
    [NotifyPropertyChangedFor(nameof(CanConfirmMinecraftDirectoryName))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMinecraftDirectoryNameCommand))]
    private string minecraftDirectoryName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmMinecraftDirectoryName))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMinecraftDirectoryNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestRenameMinecraftDirectoryCommand))]
    private bool isMinecraftDirectoryNameDialogBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveMinecraftDirectoryDialogMessage))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveMinecraftDirectoryCommand))]
    private SettingsMinecraftDirectoryItem? minecraftDirectoryPendingRemoval;

    public ObservableCollection<SettingsMinecraftDirectoryItem> MinecraftDirectories { get; } = [];

    public MinecraftDirectorySwitchDialogViewModel MinecraftDirectorySwitchDialog { get; }

    public string MinecraftDirectoryNameDialogTitle => isMinecraftDirectoryNameDialogForAdd
        ? Strings.Dialog_AddMinecraftDirectoryNameTitle
        : Strings.Dialog_RenameMinecraftDirectoryNameTitle;

    public string MinecraftDirectoryNameDialogConfirmButtonText => isMinecraftDirectoryNameDialogForAdd
        ? Strings.Settings_AddMinecraftDirectoryConfirmButton
        : Strings.Settings_RenameMinecraftDirectoryConfirmButton;

    public bool IsMinecraftDirectoryNameInvalid =>
        string.IsNullOrWhiteSpace(MinecraftDirectoryName)
        || MinecraftDirectoryName.Trim().Length > MinecraftDirectoryDisplayName.MaximumLength;

    public bool CanConfirmMinecraftDirectoryName =>
        IsMinecraftDirectoryNameDialogOpen
        && !IsMinecraftDirectoryNameDialogBusy
        && !IsMinecraftDirectoryNameInvalid
        && (!isMinecraftDirectoryNameDialogForAdd || CanChangeMinecraftDirectory);

    public string RemoveMinecraftDirectoryDialogMessage => MinecraftDirectoryPendingRemoval is null
        ? string.Empty
        : string.Format(
            Strings.Dialog_RemoveMinecraftDirectoryMessageFormat,
            MinecraftDirectoryPendingRemoval.DirectoryPath);

    public void Load(LauncherSettings settings)
    {
        LoadState(() =>
        {
            LoadMinecraftDirectories(settings);
            LauncherLogDirectory = LauncherLogConfiguration.ResolveLogDirectory();
            DiagnosticLoggingEnabled = settings.EnableDiagnosticLogging;
        });
    }

    public void OpenMinecraftDirectorySwitchDialog()
    {
        LoadState(() => LoadMinecraftDirectories(Settings));
        MinecraftDirectorySwitchDialog.Open();
    }

    partial void OnDiagnosticLoggingEnabledChanged(bool value)
    {
        if (!CanPersist)
            return;

        logLevelController?.SetDiagnosticLoggingEnabled(value);
        Persist(settings => settings.EnableDiagnosticLogging = value);
        logger.LogInformation(
            "Diagnostic logging changed. Enabled={Enabled}",
            value);
    }

    partial void OnSelectedMinecraftDirectoryChanged(SettingsMinecraftDirectoryItem? value)
    {
        if (suppressMinecraftDirectorySelectionChanged || value is null || !CanPersist)
            return;

        var currentItem = FindMinecraftDirectoryItem(MinecraftDirectory);
        if (MinecraftDirectoryPath.Equals(value.DirectoryPath, MinecraftDirectory))
            return;

        if (!CanChangeMinecraftDirectory)
        {
            SetSelectedMinecraftDirectory(currentItem);
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        if (!value.IsAvailable
            || !minecraftDirectoryFileSystem.DirectoryIsAccessible(value.DirectoryPath))
        {
            LoadState(() => LoadMinecraftDirectories(Settings));
            statusService.Report(Strings.Status_MinecraftDirectoryUnavailable);
            return;
        }

        _ = SelectMinecraftDirectoryAsync(value.DirectoryPath);
    }

    [RelayCommand]
    private void OpenMinecraftDirectory(SettingsMinecraftDirectoryItem? item)
    {
        try
        {
            if (item is not null && item.IsAvailable && instanceFolderService.TryOpen(item.DirectoryPath))
                return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open Minecraft directory.");
        }

        statusService.Report(Strings.Status_OpenMinecraftDirectoryFailed);
    }

    private bool CanRequestRenameMinecraftDirectory(SettingsMinecraftDirectoryItem? item) =>
        !IsMinecraftDirectoryNameDialogBusy && item is not null;

    [RelayCommand(CanExecute = nameof(CanRequestRenameMinecraftDirectory))]
    private void RequestRenameMinecraftDirectory(SettingsMinecraftDirectoryItem? item)
    {
        if (!CanRequestRenameMinecraftDirectory(item))
            return;

        OpenMinecraftDirectoryNameDialog(
            item!.DirectoryPath,
            item.DisplayName,
            forAdd: false);
    }

    [RelayCommand]
    private void CancelMinecraftDirectoryName()
    {
        if (IsMinecraftDirectoryNameDialogBusy)
            return;

        CloseMinecraftDirectoryNameDialog();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmMinecraftDirectoryName))]
    private async Task ConfirmMinecraftDirectoryNameAsync()
    {
        var directoryPath = minecraftDirectoryPendingNamePath;
        if (directoryPath is null || !CanConfirmMinecraftDirectoryName)
            return;

        var displayName = MinecraftDirectoryDisplayName.Normalize(MinecraftDirectoryName);
        IsMinecraftDirectoryNameDialogBusy = true;
        try
        {
            if (isMinecraftDirectoryNameDialogForAdd)
            {
                var succeeded = await ChangeMinecraftDirectoryAsync(
                    directoryPath,
                    addDirectory: true,
                    displayName,
                    Strings.Status_MinecraftDirectoryAdded);
                if (succeeded)
                    CloseMinecraftDirectoryNameDialog();
                return;
            }

            var previousDisplayNames = CloneMinecraftDirectoryDisplayNames();
            try
            {
                await PersistImmediatelyAsync(settings =>
                    minecraftDirectoryManagementService.RenameDirectory(
                        settings,
                        directoryPath,
                        displayName));
                LoadState(() => LoadMinecraftDirectories(Settings));
                logger.LogInformation(
                    "Minecraft directory display name changed. MinecraftDirectory={MinecraftDirectory}",
                    directoryPath);
                statusService.Report(Strings.Status_MinecraftDirectoryRenamed);
                CloseMinecraftDirectoryNameDialog();
            }
            catch (Exception exception)
            {
                Settings.MinecraftDirectoryDisplayNames = previousDisplayNames;
                LoadState(() => LoadMinecraftDirectories(Settings));
                logger.LogError(
                    exception,
                    "Failed to change Minecraft directory display name. MinecraftDirectory={MinecraftDirectory}",
                    directoryPath);
                statusService.Report(Strings.Status_RenameMinecraftDirectoryFailed);
            }
        }
        finally
        {
            IsMinecraftDirectoryNameDialogBusy = false;
        }
    }

    private bool CanRequestRemoveMinecraftDirectory(SettingsMinecraftDirectoryItem? item) =>
        !isChangingMinecraftDirectory && item?.CanRemove == true;

    [RelayCommand(CanExecute = nameof(CanRequestRemoveMinecraftDirectory))]
    private void RequestRemoveMinecraftDirectory(SettingsMinecraftDirectoryItem? item)
    {
        if (!CanRequestRemoveMinecraftDirectory(item))
            return;

        MinecraftDirectoryPendingRemoval = item;
        IsRemoveMinecraftDirectoryDialogOpen = true;
    }

    [RelayCommand]
    private void CancelRemoveMinecraftDirectory()
    {
        IsRemoveMinecraftDirectoryDialogOpen = false;
        MinecraftDirectoryPendingRemoval = null;
    }

    private bool CanConfirmRemoveMinecraftDirectory =>
        !isChangingMinecraftDirectory && MinecraftDirectoryPendingRemoval?.CanRemove == true;

    [RelayCommand(CanExecute = nameof(CanConfirmRemoveMinecraftDirectory))]
    private async Task ConfirmRemoveMinecraftDirectoryAsync()
    {
        var pendingRemoval = MinecraftDirectoryPendingRemoval;
        if (pendingRemoval is null || !CanConfirmRemoveMinecraftDirectory)
            return;

        IsRemoveMinecraftDirectoryDialogOpen = false;
        MinecraftDirectoryPendingRemoval = null;
        SetMinecraftDirectoryChangeInProgress(true);
        var previousDirectories = Settings.MinecraftDirectories.ToList();
        var previousDisplayNames = CloneMinecraftDirectoryDisplayNames();
        var previousExcludedDirectories = Settings.ExcludedMinecraftDirectories.ToList();
        try
        {
            await PersistImmediatelyAsync(settings =>
            {
                minecraftDirectoryManagementService.RemoveDirectoryFromList(
                    settings,
                    pendingRemoval.DirectoryPath);
            });
            LoadState(() => LoadMinecraftDirectories(Settings));
            logger.LogInformation(
                "Minecraft directory removed from launcher list. MinecraftDirectory={MinecraftDirectory}",
                pendingRemoval.DirectoryPath);
            statusService.Report(Strings.Status_MinecraftDirectoryRemovedFromList);
        }
        catch (Exception exception)
        {
            Settings.MinecraftDirectories = previousDirectories;
            Settings.MinecraftDirectoryDisplayNames = previousDisplayNames;
            Settings.ExcludedMinecraftDirectories = previousExcludedDirectories;
            LoadState(() => LoadMinecraftDirectories(Settings));
            logger.LogError(
                exception,
                "Failed to remove Minecraft directory from launcher list. MinecraftDirectory={MinecraftDirectory}",
                pendingRemoval.DirectoryPath);
            statusService.Report(Strings.Status_RemoveMinecraftDirectoryFromListFailed);
        }
        finally
        {
            SetMinecraftDirectoryChangeInProgress(false);
        }
    }

    [RelayCommand]
    private void OpenLauncherLogDirectory()
    {
        var directory = LauncherLogConfiguration.ResolveLogDirectory();
        if (TryPrepareAndOpenDirectory(directory, Strings.Status_OpenLaunchLogFolderFailed))
            LauncherLogDirectory = directory;
    }

    [RelayCommand(CanExecute = nameof(CanAddMinecraftDirectory))]
    private async Task AddMinecraftDirectoryAsync()
    {
        if (isChangingMinecraftDirectory)
            return;

        if (!CanAddMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        var selectedDirectory = filePickerService.PickFolder(
            Strings.FilePicker_MinecraftDirectoryTitle,
            MinecraftDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        if (!minecraftDirectoryFileSystem.DirectoryIsAccessible(selectedDirectory))
        {
            statusService.Report(Strings.Status_AddMinecraftDirectoryFailed);
            return;
        }

        string normalizedDirectory;
        try
        {
            normalizedDirectory = MinecraftDirectoryPath.Normalize(selectedDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            logger.LogWarning(exception, "Invalid Minecraft directory selected for registration.");
            statusService.Report(Strings.Status_AddMinecraftDirectoryFailed);
            return;
        }

        if (isChangingMinecraftDirectory)
            return;

        if (!CanAddMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return;
        }

        var wasAlreadyRegistered = Settings.MinecraftDirectories.Contains(
            normalizedDirectory,
            MinecraftDirectoryPath.Comparer);
        if (wasAlreadyRegistered && MinecraftDirectoryPath.Equals(normalizedDirectory, MinecraftDirectory))
        {
            SetSelectedMinecraftDirectory(FindMinecraftDirectoryItem(MinecraftDirectory));
            return;
        }

        if (!wasAlreadyRegistered)
        {
            OpenMinecraftDirectoryNameDialog(
                normalizedDirectory,
                MinecraftDirectoryDisplayName.GetDefault(normalizedDirectory),
                forAdd: true);
            return;
        }

        await ChangeMinecraftDirectoryAsync(
            normalizedDirectory,
            addDirectory: false,
            displayName: null,
            successMessage: Strings.Status_MinecraftDirectoryChanged);
    }

    public void Dispose()
    {
        if (downloadTasksPage is not null)
            downloadTasksPage.ActivityChanged -= DownloadTasksPage_ActivityChanged;
    }

    private Task<bool> SelectMinecraftDirectoryAsync(string directoryPath) =>
        ChangeMinecraftDirectoryAsync(
            directoryPath,
            addDirectory: false,
            displayName: null,
            successMessage: Strings.Status_MinecraftDirectoryChanged);

    private async Task<bool> ChangeMinecraftDirectoryAsync(
        string directoryPath,
        bool addDirectory,
        string? displayName,
        string successMessage)
    {
        if (!CanChangeMinecraftDirectory)
        {
            statusService.Report(Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
            return false;
        }

        if (!minecraftDirectoryFileSystem.DirectoryIsAccessible(directoryPath))
        {
            LoadState(() => LoadMinecraftDirectories(Settings));
            statusService.Report(addDirectory
                ? Strings.Status_AddMinecraftDirectoryFailed
                : Strings.Status_MinecraftDirectoryUnavailable);
            return false;
        }

        SetMinecraftDirectoryChangeInProgress(true);
        var previousDirectory = Settings.MinecraftDirectory;
        var previousDirectories = Settings.MinecraftDirectories.ToList();
        var previousDisplayNames = CloneMinecraftDirectoryDisplayNames();
        var previousExcludedDirectories = Settings.ExcludedMinecraftDirectories.ToList();
        try
        {
            await PersistImmediatelyAsync(settings =>
            {
                if (addDirectory)
                    minecraftDirectoryManagementService.AddAndSelectDirectory(
                        settings,
                        directoryPath,
                        displayName);
                else
                    minecraftDirectoryManagementService.SelectDirectory(settings, directoryPath);
            });

            var becameUnavailable =
                !minecraftDirectoryFileSystem.DirectoryIsAccessible(directoryPath);
            if (downloadTasksPage?.HasActiveOperations == true || becameUnavailable)
            {
                await PersistImmediatelyAsync(settings =>
                {
                    settings.MinecraftDirectory = previousDirectory;
                    settings.MinecraftDirectories = previousDirectories.ToList();
                    settings.MinecraftDirectoryDisplayNames = new Dictionary<string, string>(
                        previousDisplayNames,
                        MinecraftDirectoryPath.Comparer);
                    settings.ExcludedMinecraftDirectories = previousExcludedDirectories.ToList();
                });
                LoadState(() => LoadMinecraftDirectories(Settings));
                statusService.Report(becameUnavailable
                    ? addDirectory
                        ? Strings.Status_AddMinecraftDirectoryFailed
                        : Strings.Status_MinecraftDirectoryUnavailable
                    : Strings.Settings_MinecraftDirectoryChangeBlockedByActiveTasks);
                return false;
            }

            LoadState(() => LoadMinecraftDirectories(Settings));
        }
        catch (Exception exception)
        {
            Settings.MinecraftDirectory = previousDirectory;
            Settings.MinecraftDirectories = previousDirectories;
            Settings.MinecraftDirectoryDisplayNames = previousDisplayNames;
            Settings.ExcludedMinecraftDirectories = previousExcludedDirectories;
            LoadState(() => LoadMinecraftDirectories(Settings));
            logger.LogError(
                exception,
                addDirectory
                    ? "Failed to add and select Minecraft directory."
                    : "Failed to save selected Minecraft directory.");
            statusService.Report(addDirectory
                ? Strings.Status_AddMinecraftDirectoryFailed
                : Strings.Status_MinecraftDirectorySwitchFailed);
            return false;
        }
        finally
        {
            SetMinecraftDirectoryChangeInProgress(false);
        }

        statusService.Report(successMessage);
        MinecraftDirectoryChanged?.Invoke(
            this,
            new SettingsMinecraftDirectoryChangedEventArgs(Settings.MinecraftDirectory));
        return true;
    }

    private void DownloadTasksPage_ActivityChanged(object? sender, EventArgs e)
    {
        NotifyMinecraftDirectoryCommandStateChanged();
    }

    private void SetMinecraftDirectoryChangeInProgress(bool value)
    {
        if (isChangingMinecraftDirectory == value)
            return;

        isChangingMinecraftDirectory = value;
        NotifyMinecraftDirectoryCommandStateChanged();
    }

    private void NotifyMinecraftDirectoryCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanChangeMinecraftDirectory));
        OnPropertyChanged(nameof(CanAddMinecraftDirectory));
        OnPropertyChanged(nameof(IsMinecraftDirectoryChangeBlocked));
        AddMinecraftDirectoryCommand.NotifyCanExecuteChanged();
        RequestRemoveMinecraftDirectoryCommand.NotifyCanExecuteChanged();
        ConfirmRemoveMinecraftDirectoryCommand.NotifyCanExecuteChanged();
        ConfirmMinecraftDirectoryNameCommand.NotifyCanExecuteChanged();
        MinecraftDirectorySwitchDialog.NotifyDirectoryChangeStateChanged();
    }

    private void LoadMinecraftDirectories(LauncherSettings settings)
    {
        MinecraftDirectory = settings.MinecraftDirectory;
        for (var targetIndex = 0; targetIndex < settings.MinecraftDirectories.Count; targetIndex++)
        {
            var directory = settings.MinecraftDirectories[targetIndex];
            var existingIndex = FindMinecraftDirectoryItemIndex(directory, targetIndex);
            SettingsMinecraftDirectoryItem item;
            if (existingIndex >= 0)
            {
                if (existingIndex != targetIndex)
                    MinecraftDirectories.Move(existingIndex, targetIndex);

                item = MinecraftDirectories[targetIndex];
                item.Update(
                    GetMinecraftDirectoryDisplayName(settings, directory),
                    minecraftDirectoryFileSystem.DirectoryIsAccessible(directory),
                    !MinecraftDirectoryPath.Equals(directory, settings.MinecraftDirectory));
            }
            else
            {
                item = new SettingsMinecraftDirectoryItem(
                    GetMinecraftDirectoryDisplayName(settings, directory),
                    directory,
                    minecraftDirectoryFileSystem.DirectoryIsAccessible(directory),
                    !MinecraftDirectoryPath.Equals(directory, settings.MinecraftDirectory));
                MinecraftDirectories.Insert(targetIndex, item);
            }
        }

        while (MinecraftDirectories.Count > settings.MinecraftDirectories.Count)
            MinecraftDirectories.RemoveAt(MinecraftDirectories.Count - 1);

        SetSelectedMinecraftDirectory(FindMinecraftDirectoryItem(settings.MinecraftDirectory));
        MinecraftDirectorySwitchDialog.SynchronizeWithCurrentDirectory();
    }

    private int FindMinecraftDirectoryItemIndex(string directoryPath, int startIndex)
    {
        for (var index = startIndex; index < MinecraftDirectories.Count; index++)
        {
            if (MinecraftDirectoryPath.Equals(
                    MinecraftDirectories[index].DirectoryPath,
                    directoryPath))
            {
                return index;
            }
        }

        return -1;
    }

    private SettingsMinecraftDirectoryItem? FindMinecraftDirectoryItem(string directoryPath) =>
        MinecraftDirectories.FirstOrDefault(item =>
            MinecraftDirectoryPath.Equals(item.DirectoryPath, directoryPath));

    private void SetSelectedMinecraftDirectory(SettingsMinecraftDirectoryItem? item)
    {
        suppressMinecraftDirectorySelectionChanged = true;
        try
        {
            SelectedMinecraftDirectory = item;
        }
        finally
        {
            suppressMinecraftDirectorySelectionChanged = false;
        }
    }

    private void OpenMinecraftDirectoryNameDialog(
        string directoryPath,
        string displayName,
        bool forAdd)
    {
        minecraftDirectoryPendingNamePath = directoryPath;
        isMinecraftDirectoryNameDialogForAdd = forAdd;
        MinecraftDirectoryName = displayName;
        OnPropertyChanged(nameof(MinecraftDirectoryNameDialogTitle));
        OnPropertyChanged(nameof(MinecraftDirectoryNameDialogConfirmButtonText));
        OnPropertyChanged(nameof(CanConfirmMinecraftDirectoryName));
        IsMinecraftDirectoryNameDialogOpen = true;
    }

    private void CloseMinecraftDirectoryNameDialog()
    {
        IsMinecraftDirectoryNameDialogOpen = false;
        minecraftDirectoryPendingNamePath = null;
        isMinecraftDirectoryNameDialogForAdd = false;
        MinecraftDirectoryName = string.Empty;
        OnPropertyChanged(nameof(MinecraftDirectoryNameDialogTitle));
        OnPropertyChanged(nameof(MinecraftDirectoryNameDialogConfirmButtonText));
        OnPropertyChanged(nameof(CanConfirmMinecraftDirectoryName));
    }

    private Dictionary<string, string> CloneMinecraftDirectoryDisplayNames() =>
        new(Settings.MinecraftDirectoryDisplayNames, MinecraftDirectoryPath.Comparer);

    private static string GetMinecraftDirectoryDisplayName(
        LauncherSettings settings,
        string directoryPath)
    {
        foreach (var pair in settings.MinecraftDirectoryDisplayNames)
        {
            if (MinecraftDirectoryPath.Equals(pair.Key, directoryPath))
                return MinecraftDirectoryDisplayName.NormalizeOrDefault(pair.Value, directoryPath);
        }

        return MinecraftDirectoryDisplayName.GetDefault(directoryPath);
    }

    private bool TryPrepareAndOpenDirectory(string directory, string failureMessage)
    {
        try
        {
            var prepared = instanceFolderService.EnsureDirectoryExists(directory);
            if (instanceFolderService.TryOpen(prepared))
                return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open launcher directory.");
        }

        statusService.Report(failureMessage);
        return false;
    }
}
