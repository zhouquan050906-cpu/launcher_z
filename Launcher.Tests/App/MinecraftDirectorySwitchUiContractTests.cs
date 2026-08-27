/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.App;

public sealed class MinecraftDirectorySwitchUiContractTests
{
    [Fact]
    public void GameSettingsUsesPinnedFooterOnlyForListStep()
    {
        var source = ReadSource(
            "Launcher.App",
            "Views",
            "GameSettings",
            "GameSettingsPageView.xaml");

        Assert.Contains("SecondaryMenuFrame.FooterContent", source, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding IsListStep", source, StringComparison.Ordinal);
        Assert.Contains("instance_setting_page/folder-conversion", source, StringComparison.Ordinal);
        Assert.Contains("RequestMinecraftDirectorySwitchCommand", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryMenuFooterIsCollapsedWhenNoContentIsProvided()
    {
        var controlSource = ReadSource(
            "Launcher.App",
            "Controls",
            "Navigation",
            "SecondaryMenuFrame.xaml.cs");
        var viewSource = ReadSource(
            "Launcher.App",
            "Controls",
            "Navigation",
            "SecondaryMenuFrame.xaml");

        Assert.Contains("FooterContentProperty", controlSource, StringComparison.Ordinal);
        Assert.Contains("Binding Content, RelativeSource={RelativeSource Self}", viewSource, StringComparison.Ordinal);
        Assert.Contains("Value=\"{x:Null}\"", viewSource, StringComparison.Ordinal);
        Assert.Contains("Property=\"Visibility\" Value=\"Collapsed\"", viewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellForwardsGameSettingsRequestToGeneralSettingsSwitcher()
    {
        var shellSource = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.Events.cs");

        Assert.Contains(
            "SettingsPage.General.OpenMinecraftDirectorySwitchDialog();",
            shellSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShellForwardsLaunchActivityToGeneralSettingsDirectoryGate()
    {
        var shellSource = ReadSource("Launcher.App", "ViewModels", "Shell", "MainViewModel.cs");
        var shellEventsSource = ReadSource("Launcher.App", "ViewModels", "Shell", "MainViewModel.Events.cs");
        var homeSource = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Home",
            "HomePageViewModel.CommandsAndProgress.cs");

        // 启动准备状态必须从首页一路传到设置页，否则启动期间目录切换的门禁会静默失效。
        Assert.Contains("LaunchActivityChanged?.Invoke(this, EventArgs.Empty);", homeSource, StringComparison.Ordinal);
        Assert.Contains(
            "HomePage.LaunchActivityChanged += HomePage_LaunchActivityChanged;",
            shellSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "SettingsPage.General.SetGameLaunchInProgress(HomePage.IsLaunching);",
            shellEventsSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadPageReceivesTheMinecraftDirectoryOnPrimeAndOnSwitch()
    {
        var downloadSource = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Download",
            "DownloadPageViewModel.SettingsAndDrop.cs");
        var coordinatorSource = ReadSource(
            "Launcher.App",
            "Services",
            "State",
            "LauncherSessionCoordinator.cs");

        // 实例名可用性检查依赖被推送进来的目录；漏推会让重名校验静默失效而不是报错。
        Assert.Contains(
            "ApplyMinecraftDirectory(settings.MinecraftDirectory);",
            downloadSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "InstanceOptions.ApplyMinecraftDirectory(minecraftDirectory);",
            downloadSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "downloadPage.ApplyMinecraftDirectory(e.MinecraftDirectory);",
            coordinatorSource,
            StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return File.ReadAllText(Path.Combine([root.FullName, .. segments]));
    }
}
