/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Logging;

namespace Launcher.Tests.Diagnostics;

/// <summary>
/// 设置页的“清理日志”按钮直接删除日志目录内容，因此清理范围必须与滚动清理保持一致，
/// 且不能因为当前进程占用着自己的日志文件而报错。
/// </summary>
public sealed class LauncherLogCleanupTests : TestTempDirectory
{
    [Fact]
    public void ClearLogFilesRemovesLauncherAndUpdaterLogs()
    {
        var logDirectory = CreateLogDirectory();
        WriteFile(logDirectory, "bhl-20260101-000000-000-p1.log");
        WriteFile(logDirectory, "bhl-20260102-000000-000-p2_001.log");
        WriteFile(logDirectory, "updater-20260101.log");

        var result = LauncherLogConfiguration.ClearLogFiles(logDirectory);

        Assert.Equal(3, result.DeletedFileCount);
        Assert.Equal(0, result.RetainedFileCount);
        Assert.Empty(Directory.GetFiles(logDirectory));
    }

    [Fact]
    public void ClearLogFilesKeepsUnrelatedFiles()
    {
        var logDirectory = CreateLogDirectory();
        WriteFile(logDirectory, "bhl-20260101-000000-000-p1.log");
        WriteFile(logDirectory, "launch-report.json");
        WriteFile(logDirectory, "notes.txt");

        var result = LauncherLogConfiguration.ClearLogFiles(logDirectory);

        Assert.Equal(1, result.DeletedFileCount);
        Assert.Equal(
            new[] { "launch-report.json", "notes.txt" },
            Directory.GetFiles(logDirectory).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void ClearLogFilesReportsTheLogFileHeldByTheRunningLauncher()
    {
        var logDirectory = CreateLogDirectory();
        var activeLog = WriteFile(logDirectory, "bhl-20260103-000000-000-p3.log");
        WriteFile(logDirectory, "bhl-20260101-000000-000-p1.log");
        using var handle = new FileStream(activeLog, FileMode.Open, FileAccess.Write, FileShare.None);

        var result = LauncherLogConfiguration.ClearLogFiles(logDirectory);

        Assert.Equal(1, result.DeletedFileCount);
        Assert.Equal(1, result.RetainedFileCount);
        Assert.Equal(new[] { Path.GetFileName(activeLog) }, Directory.GetFiles(logDirectory).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void ClearLogFilesOnMissingDirectoryReportsNothingRemoved()
    {
        var result = LauncherLogConfiguration.ClearLogFiles(Path.Combine(TempRoot, "absent"));

        Assert.Equal(LauncherLogCleanupResult.Empty, result);
    }

    private string CreateLogDirectory()
    {
        var logDirectory = Path.Combine(TempRoot, "log");
        Directory.CreateDirectory(logDirectory);
        return logDirectory;
    }

    private static string WriteFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "log");
        return path;
    }
}
