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

namespace Launcher.Tests.App;

public sealed class LauncherStartupPageActivationContractTests
{
    [Fact]
    public void ShellForwardsPageActivationWhileFullInitializationIsPending()
    {
        var source = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.Events.cs");
        var method = ExtractMethod(
            source,
            "public Task ActivateCurrentPageAsync()",
            "public Task SyncExternalInstanceCatalogAsync()");

        Assert.Contains("return sessionCoordinator.ActivatePageAsync(CurrentPage);", method, StringComparison.Ordinal);
        Assert.DoesNotContain("hasInitialized", method, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeInstanceListDoesNotDependOnRemoteVersionCatalog()
    {
        var listSource = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Home",
            "HomeLaunchGameListViewModel.cs");
        var factorySource = ReadSource(
            "Launcher.App",
            "Services",
            "Home",
            "HomePageViewModelFactory.cs");

        Assert.DoesNotContain("IGameVersionService", listSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVersionTypesLoadedAsync", listSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameVersionService", factorySource, StringComparison.Ordinal);
    }

    [Fact]
    public void CoordinatorLoadsDownloadOnActivationAndProjectsPrimedGameSettingsCatalog()
    {
        var source = ReadSource(
            "Launcher.App",
            "Services",
            "State",
            "LauncherSessionCoordinator.cs");
        var initializeMethod = ExtractMethod(
            source,
            "public async Task InitializeAsync()",
            "public async Task ActivatePageAsync(string page)");
        var activateMethod = ExtractMethod(
            source,
            "public async Task ActivatePageAsync(string page)",
            "public async Task RefreshExternalInstanceCatalogAsync()");

        Assert.Contains(
            "if (NavigationCatalog.IsPage(currentPage, NavigationCatalog.GameSettingsPage))",
            initializeMethod,
            StringComparison.Ordinal);
        Assert.Contains("SynchronizeGameSettingsInstances();", initializeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVersionTypesLoadedAsync", initializeMethod, StringComparison.Ordinal);

        var initializationGuard = activateMethod.IndexOf("if (!isInitialized)", StringComparison.Ordinal);
        var primedProjection = activateMethod.IndexOf(
            "if (isGameSettingsPage && settings is not null)",
            StringComparison.Ordinal);
        var downloadLoad = activateMethod.IndexOf("downloadPage.EnsureVersionsLoadedAsync()", StringComparison.Ordinal);

        Assert.True(initializationGuard >= 0);
        Assert.True(primedProjection > initializationGuard);
        Assert.True(downloadLoad >= 0);
        Assert.True(downloadLoad < initializationGuard);
        Assert.Contains("SynchronizeGameSettingsInstances();", activateMethod, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root.FullName, .. segments]));
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method marker '{startMarker}'.");
        Assert.True(end > start, $"Could not find method marker '{endMarker}'.");
        return source[start..end];
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
