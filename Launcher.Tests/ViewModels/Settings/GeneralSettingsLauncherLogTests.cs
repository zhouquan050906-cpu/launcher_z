/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

/// <summary>
/// 清理日志会不可逆地删除文件，因此按钮必须先弹确认框；取消时不得触碰日志目录。
/// </summary>
public sealed class GeneralSettingsLauncherLogTests : TestTempDirectory
{
    [Fact]
    public void RequestClearLauncherLogsOnlyOpensTheConfirmationDialog()
    {
        using var viewModel = CreateViewModel(out var status);

        viewModel.RequestClearLauncherLogsCommand.Execute(null);

        Assert.True(viewModel.IsClearLauncherLogsDialogOpen);
        Assert.Empty(status.Messages);
    }

    [Fact]
    public void CancelClearLauncherLogsClosesTheDialogWithoutClearing()
    {
        using var viewModel = CreateViewModel(out var status);
        viewModel.RequestClearLauncherLogsCommand.Execute(null);

        viewModel.CancelClearLauncherLogsCommand.Execute(null);

        Assert.False(viewModel.IsClearLauncherLogsDialogOpen);
        Assert.Empty(status.Messages);
    }

    [Fact]
    public async Task ConfirmClearLauncherLogsClosesTheDialogAndReportsTheOutcome()
    {
        using var viewModel = CreateViewModel(out var status);
        viewModel.RequestClearLauncherLogsCommand.Execute(null);

        await viewModel.ConfirmClearLauncherLogsCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsClearLauncherLogsDialogOpen);
        Assert.Equal(Strings.Status_NoLauncherLogsToClear, Assert.Single(status.Messages));
    }

    private GeneralSettingsViewModel CreateViewModel(out RecordingStatusService status)
    {
        var minecraftDirectory = Path.Combine(TempRoot, "current");
        Directory.CreateDirectory(minecraftDirectory);
        var settings = new LauncherSettings
        {
            MinecraftDirectory = minecraftDirectory,
            MinecraftDirectories = [minecraftDirectory],
        };
        status = new RecordingStatusService();
        var coordinator = new SettingsPersistenceCoordinator(
            new TestSettingsService(settings),
            status,
            NullLogger.Instance);
        coordinator.Prime(settings);
        var viewModel = new GeneralSettingsViewModel(
            coordinator,
            status,
            new StubFilePickerService(),
            new StubInstanceFolderService(),
            new StubMinecraftDirectoryFileSystem(),
            new MinecraftDirectoryManagementService(),
            downloadTasksPage: null,
            logLevelController: null,
            NullLogger.Instance);
        viewModel.Load(settings);
        return viewModel;
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

    private sealed class StubFilePickerService : IFilePickerService
    {
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
        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }

    private sealed class StubInstanceFolderService : IInstanceFolderService
    {
        public bool DirectoryExists(string folderPath) => Directory.Exists(folderPath);

        public string EnsureDirectoryExists(string folderPath)
        {
            Directory.CreateDirectory(folderPath);
            return MinecraftDirectoryPath.Normalize(folderPath);
        }

        public bool TryOpen(string folderPath) => Directory.Exists(folderPath);

        public bool TryOpenFile(string filePath) => false;

        public bool TryRevealFile(string filePath) => false;
    }
}
