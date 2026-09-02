/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Tests.App;

/// <summary>
/// WPF 的 OleDragLeave 回调没有坐标，DragLeave 事件里的位置被硬编码为 Point(0,0)，
/// 因此任何"指针是否还在窗口内"的坐标判断都会恒为真、清理永远不执行。
/// 这里锁定改用延迟清理的实现，防止坐标判断被重新引入。
/// </summary>
public sealed class DragDropHintLifecycleContractTests
{
    [Fact]
    public void DragLeaveDefersTheResetInsteadOfTestingCoordinates()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        var dragLeave = Slice(
            code,
            "private void Window_OnPreviewDragLeave",
            "private void ScheduleDragStateReset");

        Assert.Contains("ScheduleDragStateReset();", dragLeave, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPosition", dragLeave, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPointWithinWindow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDragEventCancelsAPendingReset()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");

        foreach (var (start, end) in new[]
                 {
                     ("private void Window_OnPreviewDragEnter", "private void Window_OnPreviewDragOver"),
                     ("private void Window_OnPreviewDragOver", "private void Window_OnPreviewDragLeave"),
                     ("private async void Window_OnPreviewDrop", "private bool HandleThirdPartyAccountDropPreview")
                 })
        {
            Assert.Contains("CancelPendingDragStateReset();", Slice(code, start, end), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResetClearsEveryDropSurface()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        var reset = Slice(code, "private void ResetDragState", "private bool HandleThirdPartyAccountDropPreview");

        Assert.Contains("ClearThirdPartyAccountDropState();", reset, StringComparison.Ordinal);
        Assert.Contains("LocalImportDialog.ClearDropState();", reset, StringComparison.Ordinal);
        Assert.Contains("ClearLocalImportDropState();", reset, StringComparison.Ordinal);
        Assert.Contains("ClearImportDropState();", reset, StringComparison.Ordinal);
    }

    // 读取拖放数据是跨进程调用，可能失败。DragOver 是频率最高的拖放事件，而全局
    // DispatcherUnhandledException 只记日志、不标记已处理，异常从这里逃逸会直接终止进程。
    [Fact]
    public void DragPreviewCannotLetAnExceptionEscape()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        var preview = Slice(code, "private void HandleDragPreview", "private void Window_OnPreviewDragLeave");

        Assert.Contains("catch (Exception exception)", preview, StringComparison.Ordinal);
        Assert.Contains("ResetDragState();", preview, StringComparison.Ordinal);
        Assert.Contains("e.Effects = DragDropEffects.None;", preview, StringComparison.Ordinal);

        foreach (var (start, end) in new[]
                 {
                     ("private void Window_OnPreviewDragEnter", "private void Window_OnPreviewDragOver"),
                     ("private void Window_OnPreviewDragOver", "private void HandleDragPreview")
                 })
        {
            Assert.Contains("HandleDragPreview(e,", Slice(code, start, end), StringComparison.Ordinal);
        }
    }

    // 提示不会自动消失，因此必须在松手当下就清除；若拖到压缩包识别 await 之后才清，
    // 整个导入过程中提示都会挂在屏幕上。
    [Fact]
    public void DropClearsTheHintBeforeDoingAnyAsyncWork()
    {
        var code = ReadSource("Launcher.App", "Views", "Shell", "MainWindow.xaml.cs");
        var drop = Slice(
            code,
            "private async void Window_OnPreviewDrop",
            "private bool HandleThirdPartyAccountDropPreview");

        var clearIndex = drop.IndexOf("floatingMessageService.ClearDragHint();", StringComparison.Ordinal);
        var tryIndex = drop.IndexOf("try", StringComparison.Ordinal);
        Assert.True(clearIndex >= 0 && tryIndex > clearIndex);
    }

    [Fact]
    public void ImportDropDoesNotReshowTheHintItIsAboutToClear()
    {
        var code = ReadSource(
            "Launcher.App", "ViewModels", "GameSettings", "GameSettingsPageViewModel.cs");
        var handler = Slice(code, "public async Task HandleImportDropAsync", "public void PrimeFromSettings");

        Assert.DoesNotContain("ApplyImportDropHint", handler, StringComparison.Ordinal);
        Assert.Contains("ClearImportDropState();", handler, StringComparison.Ordinal);
    }

    // 三处拖放提示都必须走浮层服务的拖放通道，不能再各自缓存"上一条消息"做去重。
    [Theory]
    [InlineData("Launcher.App/Views/Shell/MainWindow.xaml.cs")]
    [InlineData("Launcher.App/ViewModels/GameSettings/GameSettingsPageViewModel.cs")]
    [InlineData("Launcher.App/ViewModels/Download/DownloadPageViewModel.SettingsAndDrop.cs")]
    public void DropHintsUseThePersistentDragHintChannel(string relativePath)
    {
        var code = ReadSource(relativePath.Split('/'));

        // 必须带归属实参：一次拖动会流过多个处理器，无归属的清除会让进场动画反复重播。
        Assert.Contains("ShowDragHint(this,", code, StringComparison.Ordinal);
        Assert.Contains("ClearDragHint(this)", code, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not slice between '{startMarker}' and '{endMarker}'.");
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
