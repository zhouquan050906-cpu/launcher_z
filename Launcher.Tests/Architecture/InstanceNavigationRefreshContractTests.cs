/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.Architecture;

public sealed class InstanceNavigationRefreshContractTests
{
    [Fact]
    public void NavigationDoesNotCallInstanceRefreshPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var navigation = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.Navigation.cs"));
        var pageChanged = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.Dialogs.cs"));
        var gameSettings = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "ViewModels",
            "GameSettings",
            "GameSettingsPageViewModel.cs"));

        Assert.DoesNotContain("RefreshInstances", navigation, StringComparison.Ordinal);
        Assert.Contains("ActivateCurrentPageAsync()", pageChanged, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshForActivationAsync", gameSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshInstancesSilentlyAsync", gameSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowActivationDoesNotRequestInstanceSynchronization()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml.cs"));

        Assert.DoesNotContain("Activated +=", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "viewModel.SyncExternalInstanceCatalogAsync",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceInstallTargetsUseGameManagementCatalogSnapshot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resourcesDirectory = Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "ViewModels",
            "Resources");
        var resourcesPage = File.ReadAllText(Path.Combine(
            resourcesDirectory,
            "ResourcesPageViewModel.cs"));
        var projectVersions = File.ReadAllText(Path.Combine(
            resourcesDirectory,
            "ResourcesProjectVersionsViewModel.cs"));

        Assert.Contains("GameManagementViewModel? gameManagement", resourcesPage, StringComparison.Ordinal);
        Assert.Contains("gameManagement.GetInstanceCatalogSnapshot", resourcesPage, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameInstanceService", projectVersions, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInstancesAsync", projectVersions, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalResourceDetailsNavigateBeforePublishingHiddenPageState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var eventsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.Events.cs"));
        var loadIndex = eventsSource.IndexOf(
            "await ResourcesPage.LoadProjectDetailsAsync(reference)",
            StringComparison.Ordinal);
        var navigationIndex = eventsSource.IndexOf(
            "CurrentPage = NavigationCatalog.ResourcesPage",
            StringComparison.Ordinal);
        var postIndex = eventsSource.IndexOf("uiDispatcher.Post(", StringComparison.Ordinal);
        var showIndex = eventsSource.IndexOf(
            "ResourcesPage.ShowProjectDetails(reference, project)",
            StringComparison.Ordinal);

        Assert.True(loadIndex >= 0);
        Assert.True(navigationIndex > loadIndex);
        Assert.True(postIndex > navigationIndex);
        Assert.True(showIndex > postIndex);
        Assert.DoesNotContain(
            "GameManagement.EnsureInstancesLoadedAsync",
            eventsSource,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Launcher.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the launcher repository root.");
    }
}
