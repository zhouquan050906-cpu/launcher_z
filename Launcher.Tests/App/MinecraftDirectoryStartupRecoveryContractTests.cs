/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.App;

public sealed class MinecraftDirectoryStartupRecoveryContractTests
{
    [Fact]
    public void FirstRunInitializesLauncherDefaultBeforeDiscoveryAndRecovery()
    {
        var source = ReadSource("Launcher.App", "App.xaml.cs");
        var metadataLoad = source.IndexOf(".LoadWithMetadataAsync()", StringComparison.Ordinal);
        var initialization = source.IndexOf(
            "await InitializeDefaultMinecraftDirectoryOnFirstRunAsync()",
            StringComparison.Ordinal);
        var discovery = source.IndexOf(
            "await RegisterDiscoveredMinecraftDirectoriesOnStartupAsync(startupSettings)",
            StringComparison.Ordinal);
        var recovery = source.IndexOf(
            "await RecoverInvalidMinecraftDirectoryOnStartupAsync(startupSettings)",
            StringComparison.Ordinal);

        Assert.True(metadataLoad >= 0);
        Assert.True(initialization > metadataLoad);
        Assert.True(discovery > initialization);
        Assert.True(recovery > discovery);
    }

    [Fact]
    public void StartupRecoversMinecraftDirectoryBeforeInstanceRecoveryAndPrime()
    {
        var source = ReadSource("Launcher.App", "App.xaml.cs");
        var recovery = source.IndexOf(
            "await RecoverInvalidMinecraftDirectoryOnStartupAsync(startupSettings)",
            StringComparison.Ordinal);
        var backupRecovery = source.IndexOf(
            "await RecoverPendingInstanceBackupsOnStartupAsync(",
            StringComparison.Ordinal);
        var renameRecovery = source.IndexOf(
            "await RecoverPendingInstanceRenamesOnStartupAsync(",
            StringComparison.Ordinal);
        var prime = source.IndexOf(
            "await mainViewModel.PrimeAsync(startupSettings, minecraftDirectoryStartupRecovery)",
            StringComparison.Ordinal);

        Assert.True(recovery >= 0);
        Assert.True(backupRecovery > recovery);
        Assert.True(renameRecovery > recovery);
        Assert.True(prime > recovery);
    }

    [Fact]
    public void FatalRecoveryFailureExitsBeforeInstanceInitialization()
    {
        var source = ReadSource("Launcher.App", "App.xaml.cs");
        var catchStart = source.IndexOf(
            "catch (MinecraftDirectoryStartupRecoveryException exception)",
            StringComparison.Ordinal);
        var genericCatch = source.IndexOf(
            "catch (Exception exception)",
            catchStart + 1,
            StringComparison.Ordinal);
        Assert.True(catchStart >= 0);
        Assert.True(genericCatch > catchStart);

        var recoveryCatch = source[catchStart..genericCatch];
        Assert.Contains("MessageBox.Show(", recoveryCatch, StringComparison.Ordinal);
        Assert.Contains("Shutdown(-1);", recoveryCatch, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowWaitsForAgreementBeforeShowingPendingRecovery()
    {
        var windowSource = ReadSource(
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml.cs");
        var agreement = windowSource.IndexOf(
            "await viewModel.WaitForUserAgreementDecisionAsync()",
            StringComparison.Ordinal);
        var initialize = windowSource.IndexOf(
            "await viewModel.InitializeCommand.ExecuteAsync(null)",
            StringComparison.Ordinal);
        Assert.True(agreement >= 0);
        Assert.True(initialize > agreement);

        var viewModelSource = ReadSource(
            "Launcher.App",
            "ViewModels",
            "Shell",
            "MainViewModel.cs");
        Assert.Contains(
            "MinecraftDirectoryStartupRecoveryDialog.ShowPending();",
            viewModelSource,
            StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] segments)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root.FullName, .. segments]));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
