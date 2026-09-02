/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.App;

public sealed class ThirdPartyAccountDropRoutingContractTests
{
    [Fact]
    public void ProtocolRoutingPrecedesEveryExistingFileDropBranch()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        // DragEnter 与 DragOver 共用同一条判定链，顺序只需在这一处保证，也不会再各自漂移。
        var preview = Slice(
            code,
            "private void HandleDragPreview",
            "private void Window_OnPreviewDragLeave");
        var drop = Slice(
            code,
            "private async void Window_OnPreviewDrop",
            "private bool HandleThirdPartyAccountDropPreview");

        AssertRoutingFirst(preview, "HandleThirdPartyAccountDropPreview", "HandleDownloadLocalImportPreview");
        AssertRoutingFirst(drop, "HandleThirdPartyAccountDrop(e)", "HandleDownloadLocalImportDrop(e)");
        Assert.Contains("HandleLocalImportPageDropAsync(e)", drop, StringComparison.Ordinal);
        Assert.Contains("TryGetDroppedPaths(e)", drop, StringComparison.Ordinal);
    }

    [Fact]
    public void DragLifecycleAllowsOnlyReusableAddAccountDialogSteps()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        var dialogService = ReadSource("Launcher.App", "Services", "Dialog", "AccountDialogService.cs");

        Assert.Contains("FindVisualChildren<DialogHost>(this).ToArray()", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host, AddAccountDialogHost)", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.AccountPage.Dialog.IsAccountTypeStep", code, StringComparison.Ordinal);
        Assert.Contains("viewModel.AccountPage.Dialog.IsThirdPartyCredentialsStep", code, StringComparison.Ordinal);
        Assert.Contains("accountPage.Dialog.ApplyThirdPartyAuthenticationServer(authenticationServer)", dialogService, StringComparison.Ordinal);
        Assert.Contains("ClearThirdPartyAccountDropState();", code, StringComparison.Ordinal);
        // 提示的去重与清除归浮层服务负责，见 FloatingMessageServiceTests。
        Assert.Contains("floatingMessageService.ShowDragHint(this, message)", code, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(cachedThirdPartyAccountDropData, e.Data)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedDirectAdditionNavigatesToSelectedAccountPage()
    {
        var mainViewModel = ReadSource("Launcher.App", "ViewModels", "Shell", "MainViewModel.cs");
        var events = ReadSource("Launcher.App", "ViewModels", "Shell", "MainViewModel.Events.cs");

        Assert.Contains(
            "AccountPage.Dialog.DroppedThirdPartyAccountAdditionCompleted +=",
            mainViewModel,
            StringComparison.Ordinal);
        var handler = Slice(
            events,
            "private void AccountDialog_DroppedThirdPartyAccountAdditionCompleted",
            "private void GameSettingsPage_LocalImportRequested");
        Assert.Contains("CurrentPage = NavigationCatalog.AccountPage;", handler, StringComparison.Ordinal);
    }

    private static void AssertRoutingFirst(string method, string protocolRoute, string fileRoute) =>
        Assert.True(
            method.IndexOf(protocolRoute, StringComparison.Ordinal)
            < method.IndexOf(fileRoute, StringComparison.Ordinal));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
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
