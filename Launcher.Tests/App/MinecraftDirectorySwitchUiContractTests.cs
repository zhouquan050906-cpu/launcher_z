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

    private static string ReadSource(params string[] segments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return File.ReadAllText(Path.Combine([root.FullName, .. segments]));
    }
}
