/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class GeneralSettingsMinecraftDirectoryTests : TestTempDirectory
{
    [Fact]
    public async Task AddDirectoryPersistsRegistersSelectsAndRaisesChangedEvent()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var changed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.MinecraftDirectoryChanged += (_, args) => changed.TrySetResult(args.MinecraftDirectory);

        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal("target", viewModel.MinecraftDirectoryName);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Single(settings.MinecraftDirectories);
        viewModel.MinecraftDirectoryName = " Target Profile ";
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.Equal(MinecraftDirectoryPath.Normalize(target), await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(MinecraftDirectoryPath.Normalize(target), settings.MinecraftDirectory);
        Assert.Equal(2, settings.MinecraftDirectories.Count);
        Assert.Equal("Target Profile", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(target)]);
        Assert.False(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.True(MinecraftDirectoryPath.Equals(target, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
    }

    [Fact]
    public async Task SelectingAvailableItemPersistsBeforeConfirmingSelection()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var originalCurrentItem = viewModel.MinecraftDirectories[0];
        var originalTargetItem = viewModel.MinecraftDirectories[1];
        var collectionChangeCount = 0;
        viewModel.MinecraftDirectories.CollectionChanged += (_, _) => collectionChangeCount++;
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.MinecraftDirectoryChanged += (_, _) => changed.TrySetResult(true);

        viewModel.SelectedMinecraftDirectory = originalTargetItem;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MinecraftDirectoryPath.Normalize(target), settings.MinecraftDirectory);
        Assert.True(MinecraftDirectoryPath.Equals(target, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.Same(originalCurrentItem, viewModel.MinecraftDirectories[0]);
        Assert.Same(originalTargetItem, viewModel.MinecraftDirectories[1]);
        Assert.Equal(0, collectionChangeCount);
    }

    [Fact]
    public async Task AddButtonDoesNotEnterDisabledVisualStateDuringDirectorySwitch()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new BlockingSettingsService(settings);
        var status = new RecordingStatusService();
        var filePicker = new StubFilePickerService(target);
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            filePicker,
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.MinecraftDirectoryChanged += (_, _) => changed.TrySetResult(true);

        viewModel.SelectedMinecraftDirectory = viewModel.MinecraftDirectories[1];
        await settingsService.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.CanChangeMinecraftDirectory);
        Assert.True(MinecraftDirectoryPath.Equals(target, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.True(viewModel.CanAddMinecraftDirectory);
        Assert.True(viewModel.AddMinecraftDirectoryCommand.CanExecute(null));
        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(0, filePicker.PickFolderCallCount);

        settingsService.CompleteSave();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MissingDirectoryIsRetainedMarkedUnavailableAndCannotBeSelected()
    {
        var current = CreateDirectory("current");
        var missing = Path.Combine(TempRoot, "missing");
        var settings = CreateSettings(current, missing);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        var missingItem = viewModel.MinecraftDirectories[1];
        viewModel.SelectedMinecraftDirectory = missingItem;

        Assert.False(missingItem.IsAvailable);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.True(MinecraftDirectoryPath.Equals(current, settings.MinecraftDirectory));
        Assert.NotEmpty(status.Messages);
    }

    [Fact]
    public void DirectoryBecomingUnavailableAfterLoadCannotBeSelected()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var staleTargetItem = viewModel.MinecraftDirectories[1];
        Assert.True(staleTargetItem.IsAvailable);
        Directory.Delete(target);

        viewModel.SelectedMinecraftDirectory = staleTargetItem;

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.False(viewModel.MinecraftDirectories[1].IsAvailable);
        Assert.NotEmpty(status.Messages);
    }

    [Fact]
    public void OpenDirectoryCommandUsesItemPathAndDoesNotOpenUnavailableItem()
    {
        var current = CreateDirectory("current");
        var missing = Path.Combine(TempRoot, "missing");
        var settings = CreateSettings(current, missing);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        var folders = new StubInstanceFolderService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            folders);
        viewModel.Load(settings);

        viewModel.OpenMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[0]);
        viewModel.OpenMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[1]);

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), folders.OpenedDirectory);
        Assert.Single(folders.OpenAttempts);
    }

    [Fact]
    public async Task FailedAddRestoresDirectoryListAndSelection()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current);
        var settingsService = new FailingSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Single(settings.MinecraftDirectories);
        Assert.True(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal("target", viewModel.MinecraftDirectoryName);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
    }

    [Fact]
    public void ActiveDownloadTaskBlocksAddAndSelection()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        var downloadTasks = new DownloadTasksPageViewModel();
        downloadTasks.BeginTask("Download", "Running");
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService(),
            downloadTasks);
        viewModel.Load(settings);

        viewModel.SelectedMinecraftDirectory = viewModel.MinecraftDirectories[1];

        Assert.False(viewModel.AddMinecraftDirectoryCommand.CanExecute(null));
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.True(MinecraftDirectoryPath.Equals(current, settings.MinecraftDirectory));
    }

    [Fact]
    public async Task DownloadTaskStartingDuringSaveRestoresPreviousDirectory()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current);
        var downloadTasks = new DownloadTasksPageViewModel();
        var settingsService = new CallbackSettingsService(
            settings,
            () =>
            {
                if (!downloadTasks.HasActiveOperations)
                    downloadTasks.BeginTask("Download", "Running");
            });
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService(),
            downloadTasks);
        viewModel.Load(settings);

        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Single(settings.MinecraftDirectories);
        Assert.True(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
    }

    [Fact]
    public async Task DirectoryBecomingUnavailableDuringSaveRestoresPreviousDirectory()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new CallbackSettingsService(
            settings,
            () =>
            {
                if (Directory.Exists(target))
                    Directory.Delete(target);
            });
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var selectionFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        status.MessageReported += _ => selectionFinished.TrySetResult(true);

        viewModel.SelectedMinecraftDirectory = viewModel.MinecraftDirectories[1];
        await selectionFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
        Assert.False(viewModel.MinecraftDirectories[1].IsAvailable);
    }

    [Fact]
    public async Task ConfirmRemoveDirectoryPersistsExclusionWithoutDeletingFiles()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var targetItem = viewModel.MinecraftDirectories[1];

        viewModel.RequestRemoveMinecraftDirectoryCommand.Execute(targetItem);

        Assert.True(viewModel.IsRemoveMinecraftDirectoryDialogOpen);
        Assert.Contains(targetItem.DirectoryPath, viewModel.RemoveMinecraftDirectoryDialogMessage);
        await viewModel.ConfirmRemoveMinecraftDirectoryCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRemoveMinecraftDirectoryDialogOpen);
        Assert.True(Directory.Exists(target));
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), Assert.Single(settings.MinecraftDirectories));
        Assert.Equal(MinecraftDirectoryPath.Normalize(target), Assert.Single(settings.ExcludedMinecraftDirectories));
        Assert.DoesNotContain(settings.MinecraftDirectoryDisplayNames, pair =>
            MinecraftDirectoryPath.Equals(pair.Key, target));
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
    }

    [Fact]
    public void CancelRemoveDirectoryKeepsListAndCurrentDirectoryCannotBeRequested()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        Assert.False(viewModel.RequestRemoveMinecraftDirectoryCommand.CanExecute(
            viewModel.MinecraftDirectories[0]));
        viewModel.RequestRemoveMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[1]);
        viewModel.CancelRemoveMinecraftDirectoryCommand.Execute(null);

        Assert.False(viewModel.IsRemoveMinecraftDirectoryDialogOpen);
        Assert.Equal(2, viewModel.MinecraftDirectories.Count);
        Assert.Equal(2, settings.MinecraftDirectories.Count);
        Assert.Empty(settings.ExcludedMinecraftDirectories);
    }

    [Fact]
    public async Task FailedRemoveRestoresDirectoryListAndExclusions()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new FailingSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        viewModel.RequestRemoveMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[1]);
        await viewModel.ConfirmRemoveMinecraftDirectoryCommand.ExecuteAsync(null);

        Assert.Equal(2, settings.MinecraftDirectories.Count);
        Assert.Empty(settings.ExcludedMinecraftDirectories);
        Assert.Equal(2, viewModel.MinecraftDirectories.Count);
        Assert.True(MinecraftDirectoryPath.Equals(current, viewModel.SelectedMinecraftDirectory?.DirectoryPath));
    }

    [Fact]
    public async Task CancelAddNameDialogLeavesSettingsUnchanged()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);
        viewModel.CancelMinecraftDirectoryNameCommand.Execute(null);

        Assert.False(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal(MinecraftDirectoryPath.Normalize(current), settings.MinecraftDirectory);
        Assert.Single(settings.MinecraftDirectories);
        Assert.Empty(settings.MinecraftDirectoryDisplayNames);
    }

    [Fact]
    public async Task AddingRegisteredDirectorySwitchesWithoutOpeningNameDialog()
    {
        var current = CreateDirectory("current");
        var target = CreateDirectory("target");
        var settings = CreateSettings(current, target);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(target),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.MinecraftDirectoryChanged += (_, _) => changed.TrySetResult(true);

        await viewModel.AddMinecraftDirectoryCommand.ExecuteAsync(null);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal(MinecraftDirectoryPath.Normalize(target), settings.MinecraftDirectory);
        Assert.Equal(2, settings.MinecraftDirectories.Count);
    }

    [Fact]
    public async Task RenameCurrentDirectoryChangesOnlyDisplayName()
    {
        var current = CreateDirectory("current");
        var settings = CreateSettings(current);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        var changedCount = 0;
        viewModel.MinecraftDirectoryChanged += (_, _) => changedCount++;

        viewModel.RequestRenameMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[0]);
        Assert.True(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal("current", viewModel.MinecraftDirectoryName);
        viewModel.MinecraftDirectoryName = " Primary ";
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal("Primary", viewModel.MinecraftDirectories[0].DisplayName);
        Assert.Equal("Primary", settings.MinecraftDirectoryDisplayNames[MinecraftDirectoryPath.Normalize(current)]);
        Assert.Equal(0, changedCount);
        Assert.True(Directory.Exists(current));
    }

    [Fact]
    public async Task UnavailableDirectoryCanBeRenamed()
    {
        var current = CreateDirectory("current");
        var missing = Path.Combine(TempRoot, "missing");
        var settings = CreateSettings(current, missing);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        viewModel.RequestRenameMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[1]);
        viewModel.MinecraftDirectoryName = "Missing Profile";
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.Equal("Missing Profile", viewModel.MinecraftDirectories[1].DisplayName);
        Assert.False(viewModel.MinecraftDirectories[1].IsAvailable);
    }

    [Fact]
    public async Task ActiveDownloadTaskDoesNotBlockDisplayNameRename()
    {
        var current = CreateDirectory("current");
        var settings = CreateSettings(current);
        var settingsService = new TestSettingsService(settings);
        var status = new RecordingStatusService();
        var downloadTasks = new DownloadTasksPageViewModel();
        downloadTasks.BeginTask("Download", "Running");
        using var coordinator = CreateCoordinator(settingsService, settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService(),
            downloadTasks);
        viewModel.Load(settings);

        Assert.True(viewModel.RequestRenameMinecraftDirectoryCommand.CanExecute(
            viewModel.MinecraftDirectories[0]));
        viewModel.RequestRenameMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[0]);
        viewModel.MinecraftDirectoryName = "While Downloading";
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.Equal("While Downloading", viewModel.MinecraftDirectories[0].DisplayName);
        Assert.False(viewModel.IsMinecraftDirectoryNameDialogOpen);
    }

    [Fact]
    public void NameValidationRejectsBlankAndOverlongValues()
    {
        var current = CreateDirectory("current");
        var settings = CreateSettings(current);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(new TestSettingsService(settings), settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);
        viewModel.RequestRenameMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[0]);

        viewModel.MinecraftDirectoryName = "   ";
        Assert.True(viewModel.IsMinecraftDirectoryNameInvalid);
        Assert.False(viewModel.ConfirmMinecraftDirectoryNameCommand.CanExecute(null));
        viewModel.MinecraftDirectoryName = new string('a', MinecraftDirectoryDisplayName.MaximumLength + 1);
        Assert.True(viewModel.IsMinecraftDirectoryNameInvalid);
        Assert.False(viewModel.ConfirmMinecraftDirectoryNameCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedRenameRestoresNameAndKeepsDialogOpen()
    {
        var current = CreateDirectory("current");
        var settings = CreateSettings(current);
        var status = new RecordingStatusService();
        using var coordinator = CreateCoordinator(new FailingSettingsService(settings), settings, status);
        using var viewModel = CreateViewModel(
            coordinator,
            status,
            new StubFilePickerService(null),
            new StubInstanceFolderService());
        viewModel.Load(settings);

        viewModel.RequestRenameMinecraftDirectoryCommand.Execute(viewModel.MinecraftDirectories[0]);
        viewModel.MinecraftDirectoryName = "New Name";
        await viewModel.ConfirmMinecraftDirectoryNameCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMinecraftDirectoryNameDialogOpen);
        Assert.Equal("New Name", viewModel.MinecraftDirectoryName);
        Assert.Equal("current", viewModel.MinecraftDirectories[0].DisplayName);
        Assert.Empty(settings.MinecraftDirectoryDisplayNames);
    }

    private string CreateDirectory(string name)
    {
        var directory = Path.Combine(TempRoot, name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static LauncherSettings CreateSettings(string current, params string[] additional) => new()
    {
        MinecraftDirectory = MinecraftDirectoryPath.Normalize(current),
        MinecraftDirectories = [MinecraftDirectoryPath.Normalize(current), .. additional.Select(MinecraftDirectoryPath.Normalize)]
    };

    private static SettingsPersistenceCoordinator CreateCoordinator(
        ISettingsService settingsService,
        LauncherSettings settings,
        IStatusService statusService)
    {
        var coordinator = new SettingsPersistenceCoordinator(
            settingsService,
            statusService,
            NullLogger.Instance);
        coordinator.Prime(settings);
        return coordinator;
    }

    private static GeneralSettingsViewModel CreateViewModel(
        SettingsPersistenceCoordinator coordinator,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IInstanceFolderService instanceFolderService,
        DownloadTasksPageViewModel? downloadTasksPage = null) => new(
            coordinator,
            statusService,
            filePickerService,
            instanceFolderService,
            new StubMinecraftDirectoryFileSystem(),
            new MinecraftDirectoryManagementService(),
            downloadTasksPage,
            logLevelController: null,
            NullLogger.Instance);

    private sealed class StubMinecraftDirectoryFileSystem : IMinecraftDirectoryFileSystem
    {
        public bool DirectoryExists(string directoryPath) => Directory.Exists(directoryPath);

        public bool DirectoryIsAccessible(string directoryPath) => Directory.Exists(directoryPath);

        public string EnsureDirectoryExists(string directoryPath)
        {
            Directory.CreateDirectory(directoryPath);
            return MinecraftDirectoryPath.Normalize(directoryPath);
        }
    }

    private sealed class StubFilePickerService(string? folder) : IFilePickerService
    {
        public int PickFolderCallCount { get; private set; }

        public string? PickMinecraftSkin() => null;
        public string? PickJavaExecutable() => null;
        public string? PickLocalImportFile() => null;
        public string? PickModFile() => null;
        public string? PickSaveArchive() => null;
        public string? PickResourcePackArchive() => null;
        public string? PickShaderPackArchive() => null;
        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind) => null;
        public string? PickLaunchDiagnosticExportArchive(string instanceName) => null;
        public string? PickCustomDownloadDestination(string defaultFileName) => null;
        public string? PickFolder(string title, string? initialDirectory = null)
        {
            PickFolderCallCount++;
            return folder;
        }
    }

    private sealed class StubInstanceFolderService : IInstanceFolderService
    {
        public List<string> OpenAttempts { get; } = [];

        public string? OpenedDirectory => OpenAttempts.LastOrDefault();

        public bool DirectoryExists(string folderPath) => Directory.Exists(folderPath);

        public string EnsureDirectoryExists(string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            return MinecraftDirectoryPath.Normalize(folderPath);
        }

        public bool TryOpen(string folderPath)
        {
            OpenAttempts.Add(MinecraftDirectoryPath.Normalize(folderPath));
            return Directory.Exists(folderPath);
        }

        public bool TryOpenFile(string filePath) => false;

        public bool TryRevealFile(string filePath) => false;
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public List<string> Messages { get; } = [];

        public void Report(string message)
        {
            Messages.Add(message);
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FailingSettingsService(LauncherSettings settings) : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(LauncherSettings value, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated settings save failure."));
    }

    private sealed class CallbackSettingsService(LauncherSettings settings, Action onSave) : ISettingsService
    {
        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(LauncherSettings value, CancellationToken cancellationToken = default)
        {
            onSave();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSettingsService(LauncherSettings settings) : ISettingsService
    {
        private readonly TaskCompletionSource saveCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public async Task SaveAsync(
            LauncherSettings value,
            CancellationToken cancellationToken = default)
        {
            SaveStarted.TrySetResult();
            await saveCompletion.Task.WaitAsync(cancellationToken);
        }

        public void CompleteSave() => saveCompletion.TrySetResult();
    }
}
