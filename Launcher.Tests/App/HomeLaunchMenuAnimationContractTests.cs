/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.App;

public sealed class HomeLaunchMenuAnimationContractTests
{
    [Fact]
    public void PanelTransitionUsesRenderTransformInsteadOfHeightAnimation()
    {
        var xaml = ReadSource("Launcher.App", "Views", "Home", "HomeLaunchGameListView.xaml");
        var code = ReadSource("Launcher.App", "Views", "Home", "HomeLaunchGameListView.xaml.cs");

        Assert.Contains("<TranslateTransform x:Name=\"HomeLaunchMenuPanelTranslate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TranslateTransform.YProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HomeLaunchMenuPanelShadow, HeightProperty", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CollapseDebounceAndShadowUseSharedContracts()
    {
        var xaml = ReadSource("Launcher.App", "Views", "Home", "HomeLaunchGameListView.xaml");
        var code = ReadSource("Launcher.App", "Views", "Home", "HomeLaunchGameListView.xaml.cs");
        var shared = ReadSource("Launcher.App", "Resources", "Themes", "Shared.xaml");

        Assert.Contains("HomeLaunchMenuCollapseDelayMilliseconds", shared, StringComparison.Ordinal);
        Assert.Contains("ScheduleDelayedCollapse", code, StringComparison.Ordinal);
        Assert.Contains("IsPointerOverMenu", code, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource Brush.SecondaryMenu.Shadow.Interior}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"#", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#", xaml, StringComparison.Ordinal);
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
