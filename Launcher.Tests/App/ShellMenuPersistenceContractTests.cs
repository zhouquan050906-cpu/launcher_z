/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.Input;

namespace Launcher.Tests.App;

/// <summary>
/// 侧边菜单折叠曾经在命令里直接 await 落盘：网络盘上写入失败会顺着 AsyncRelayCommand
/// 冒泡到 Dispatcher 终止进程，写入变慢则表现为点击无响应（issue #38）。
/// </summary>
public sealed class ShellMenuPersistenceContractTests
{
    [Fact]
    public void ToggleMenuCommandStaysSynchronousSoClicksNeverAwaitDiskWrites()
    {
        var property = typeof(MainViewModel).GetProperty("ToggleMenuCommand");

        Assert.NotNull(property);
        Assert.True(typeof(IRelayCommand).IsAssignableFrom(property!.PropertyType));
        Assert.False(typeof(IAsyncRelayCommand).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public void ToggleMenuUsesTheSharedQueueThatShutdownFlushes()
    {
        var navigation = ReadSource("Launcher.App", "ViewModels", "Shell", "MainViewModel.Navigation.cs");
        var session = ReadSource("Launcher.App", "Services", "State", "LauncherSessionCoordinator.cs");
        var app = ReadSource("Launcher.App", "App.xaml.cs");
        var shutdown = ReadSource("Launcher.App", "Services", "State", "LauncherShutdownService.cs");

        Assert.Contains("sessionCoordinator.SetMenuExpanded(IsMenuExpanded);", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("settingsService.UpdateAsync", navigation, StringComparison.Ordinal);
        Assert.Contains(
            "settingsPersistence.Update(latest => latest.IsMenuExpanded = isExpanded);",
            session,
            StringComparison.Ordinal);
        Assert.DoesNotContain("shellPreferences", session, StringComparison.Ordinal);
        Assert.Contains("new SettingsPersistenceCoordinator(", app, StringComparison.Ordinal);
        Assert.Contains("settingsPage.FlushPendingSettingsAsync", shutdown, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot().FullName, .. parts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
