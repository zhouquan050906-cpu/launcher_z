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

using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.App.Views.Account.Dialogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Views.Shell;

/// <summary>
/// 承载主窗口级拖放、关闭握手、标题栏交互和无法用 Binding 表达的视觉树协调。
/// </summary>
public partial class MainWindow : Window
{
    // code-behind 只处理窗口/视觉树生命周期，业务决策始终委托给 MainViewModel 和页面 ViewModel。
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    public static readonly DependencyProperty IsMenuExpandedProperty =
        DependencyProperty.Register(nameof(IsMenuExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    // Keep the existing binding contract while exposing a retained drawing instead of
    // making every local blur surface capture the complete window visual tree.
    public FrameworkElement LauncherPreblurredBackdropSourceElement => LauncherBackgroundVisualSource;

    private readonly NavigationMenuAnimationService navigationMenuService;
    private readonly IAccountDialogService accountDialogService;
    private readonly LauncherStateSyncService stateSyncService;
    private readonly LauncherShutdownService shutdownService;
    private readonly MainWindowPlacementService windowPlacementService;
    private readonly PageTransitionService pageTransitionService;
    private readonly MainViewModel viewModel;
    private readonly ILogger<MainWindow> logger;
    private bool isShutdownInProgress;
    private bool isShutdownComplete;

    public MainWindow(
        MainViewModel viewModel,
        IWindowService windowService,
        IAccountDialogService accountDialogService,
        LauncherStateSyncService stateSyncService,
        LauncherShutdownService shutdownService,
        MainWindowPlacementService windowPlacementService,
        IThemeService themeService,
        ILogger<MainWindow>? logger = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.accountDialogService = accountDialogService;
        this.stateSyncService = stateSyncService;
        this.shutdownService = shutdownService;
        this.windowPlacementService = windowPlacementService;
        this.logger = logger ?? NullLogger<MainWindow>.Instance;
        navigationMenuService = new NavigationMenuAnimationService(MenuColumn);
        pageTransitionService = new PageTransitionService(Dispatcher, ResolvePageRoot, viewModel.CurrentPage);

        DataContext = viewModel;
        windowService.Attach(this);
        var addAccountDialogView = AddAccountDialogHost.DialogContent as AddAccountDialogView
            ?? throw new InvalidOperationException("The add-account dialog content is not initialized.");
        accountDialogService.Attach(
            viewModel.AccountPage,
            AddAccountDialogHost,
            addAccountDialogView,
            DeleteAccountDialogHost,
            RenameAccountDialogHost,
            SkinModelDialogHost,
            SkinManagerDialogHost);

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        LauncherWindowBackdrop.Attach(this, themeService);
        NativeCaptionButtons.Hide(this);
        Loaded += MainWindow_Loaded;
        Closing += Window_OnClosing;
        Closed += (_, _) => stateSyncService.Stop();
    }

    public bool IsMenuExpanded
    {
        get => (bool)GetValue(IsMenuExpandedProperty);
        set => SetValue(IsMenuExpandedProperty, value);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel viewModel)
            return;

        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            pageTransitionService.MoveTo(viewModel.CurrentPage);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.IsMenuExpanded))
        {
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.AnimateExpanded(IsMenuExpanded);
        }
    }

    private void TitleBarDragArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ToggleWindowMaximizedState();
            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        DragMove();
        e.Handled = true;
    }

    private void ToggleWindowMaximizedState()
    {
        if (ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            return;

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private FrameworkElement? ResolvePageRoot(string page)
    {
        if (string.Equals(page, "Home", StringComparison.OrdinalIgnoreCase))
            return HomePageView.RootElement;

        if (string.Equals(page, "Account", StringComparison.OrdinalIgnoreCase))
            return AccountPageView.RootElement;

        if (string.Equals(page, "Download", StringComparison.OrdinalIgnoreCase))
            return DownloadPageView.RootElement;

        if (string.Equals(page, "Install", StringComparison.OrdinalIgnoreCase))
            return InstallPageView.RootElement;

        if (string.Equals(page, "GameSettings", StringComparison.OrdinalIgnoreCase))
            return GameSettingsPageView.RootElement;

        if (string.Equals(page, "Multiplayer", StringComparison.OrdinalIgnoreCase))
            return MultiplayerPageView.RootElement;

        if (string.Equals(page, "Resources", StringComparison.OrdinalIgnoreCase))
            return ResourcesPageView.RootElement;

        if (string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase))
            return SettingsPageView.RootElement;

        return GeneralPageView.RootElement;
    }

    private void PrewarmTransientUi()
    {
        accountDialogService.Prewarm();

        foreach (var comboBox in FindVisualChildren<AnimatedComboBox>(this))
            comboBox.ApplyTemplate();

        // 折叠的页面不会被测量，虚拟化面板要到第一次真正显示才初始化视口。
        // 实测设置页的这次初始化占用 UI 线程约 130ms，整段落在第一次切页的动画里。
        // 这里趁空闲把开销较大的页面提前实例化一次，让用户点进去时已经是热的。
        PrewarmPageContent([SettingsPageView, DownloadPageView], 0);
    }

    /// <summary>
    /// 逐个预热页面内容：临时显示、强制布局，再在更低优先级上收回。
    /// 一次只热一个页面，让两次预热之间能处理输入，避免连成一段长阻塞。
    /// </summary>
    private void PrewarmPageContent(IReadOnlyList<FrameworkElement> pages, int index)
    {
        if (index >= pages.Count)
            return;

        var page = pages[index];
        // 已经是当前页说明用户先一步点了过去，本来就热了，跳过。
        if (page.Visibility == Visibility.Visible)
        {
            PrewarmPageContent(pages, index + 1);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var visibilityBinding = PageContentPrewarm.Begin(page);

        // 虚拟化面板把首次视口初始化排在 Background 优先级，所以收尾必须排在更低的
        // ContextIdle 上，等它连同随后的布局一起跑完，页面才算真的热了。
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                if (!PageContentPrewarm.End(page, visibilityBinding))
                {
                    logger.LogError(
                        "Failed to restore the page visibility binding after prewarming. Page={PageName}",
                        page.Name);
                }

                logger.LogDebug(
                    "Page content prewarmed. Page={PageName} ElapsedMs={ElapsedMs:F1}",
                    page.Name,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                PrewarmPageContent(pages, index + 1);
            },
            DispatcherPriority.ContextIdle);
    }

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // 先显示已 Prime 的界面，再等待完整初始化，避免启动期间窗口长时间空白。
        try
        {
            if (!await viewModel.WaitForUserAgreementDecisionAsync())
                return;

            await viewModel.InitializeCommand.ExecuteAsync(null);
            IsMenuExpanded = viewModel.IsMenuExpanded;
            navigationMenuService.SetExpanded(IsMenuExpanded);
            stateSyncService.Start(() => viewModel.Settings, viewModel.SyncExternalInstanceCatalogAsync);
            _ = Dispatcher.BeginInvoke(PrewarmTransientUi, DispatcherPriority.ContextIdle);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to initialize the main window.");
        }
    }

    private async void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        // 第一次 Closing 立即隐藏窗口，再在后台完成有界关闭握手；收尾完成后允许真正关闭。
        if (isShutdownComplete)
            return;

        if (!viewModel.CanCloseWindow())
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        if (isShutdownInProgress)
            return;

        isShutdownInProgress = true;
        var placement = windowPlacementService.Capture(this);
        Hide();
        using var shutdownCancellation = new CancellationTokenSource(ShutdownTimeout);
        try
        {
            await windowPlacementService.SaveAsync(placement, shutdownCancellation.Token);
        }
        catch (OperationCanceledException) when (shutdownCancellation.IsCancellationRequested)
        {
            logger.LogWarning("Timed out saving the main window placement during launcher exit.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to save the main window placement during launcher exit.");
        }

        try
        {
            await shutdownService.PrepareForExitAsync(ShutdownTimeout, shutdownCancellation.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unexpected failure while preparing the launcher to exit.");
        }
        finally
        {
            isShutdownInProgress = false;
            isShutdownComplete = true;
            Close();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void Window_OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        if (HandleDownloadLocalImportPreview(e))
            return;

        if (HandleLocalImportPagePreview(e))
            return;

        HandleFileDropPreview(e);
    }

    private void Window_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // Preview 路由在页面控件前统一判断当前导航目标，保证全窗口拖放提示一致。
        if (HandleDownloadLocalImportPreview(e))
            return;

        if (HandleLocalImportPagePreview(e))
            return;

        HandleFileDropPreview(e);
    }

    private void Window_OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (viewModel.DownloadPage.LocalImportDialog.IsOpen)
        {
            if (!IsPointWithinWindow(e.GetPosition(this)))
                viewModel.DownloadPage.LocalImportDialog.ClearDropState();

            return;
        }

        if (IsPointWithinWindow(e.GetPosition(this)))
            return;

        if (IsLocalImportDropPage())
            viewModel.DownloadPage.ClearLocalImportDropState();
        else
            viewModel.GameSettingsPage.ClearImportDropState();
    }

    private async void Window_OnPreviewDrop(object sender, DragEventArgs e)
    {
        // Drop 后才执行压缩包识别或文件导入，高频 DragOver 阶段只做轻量预览。
        try
        {
            if (HandleDownloadLocalImportDrop(e))
                return;

            if (await HandleLocalImportPageDropAsync(e))
                return;

            var paths = TryGetDroppedPaths(e);
            if (paths is null)
                return;

            e.Handled = true;
            e.Effects = DragDropEffects.None;
            await viewModel.GameSettingsPage.HandleImportDropAsync(paths);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to handle files dropped onto the main window.");
        }
    }

    private void HandleFileDropPreview(DragEventArgs e)
    {
        var paths = TryGetDroppedPaths(e);
        if (paths is null)
        {
            viewModel.GameSettingsPage.ClearImportDropState();
            return;
        }

        var canAccept = viewModel.GameSettingsPage.UpdateImportDropState(paths);
        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private bool HandleLocalImportPagePreview(DragEventArgs e)
    {
        if (!IsLocalImportDropPage())
            return false;

        var paths = TryGetDroppedPaths(e);
        var canAccept = false;
        if (paths is null)
            viewModel.DownloadPage.ClearLocalImportDropState();
        else
            canAccept = viewModel.DownloadPage.UpdateLocalImportDropState(paths);

        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    private async Task<bool> HandleLocalImportPageDropAsync(DragEventArgs e)
    {
        if (!IsLocalImportDropPage())
            return false;

        var paths = TryGetDroppedPaths(e);
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        try
        {
            if (paths is not null)
                await viewModel.DownloadPage.HandleLocalImportDropAsync(paths);
        }
        finally
        {
            viewModel.DownloadPage.ClearLocalImportDropState();
        }

        return true;
    }

    private bool HandleDownloadLocalImportPreview(DragEventArgs e)
    {
        if (!viewModel.DownloadPage.LocalImportDialog.IsOpen)
            return false;

        var paths = TryGetDroppedPaths(e);
        if (paths is null)
        {
            viewModel.DownloadPage.LocalImportDialog.ClearDropState();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return true;
        }

        var canAccept = viewModel.DownloadPage.LocalImportDialog.PreviewDroppedFiles(paths);
        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    private bool HandleDownloadLocalImportDrop(DragEventArgs e)
    {
        if (!viewModel.DownloadPage.LocalImportDialog.IsOpen)
            return false;

        var paths = TryGetDroppedPaths(e);
        if (paths is not null)
            viewModel.DownloadPage.LocalImportDialog.ApplyDroppedFiles(paths);
        else
            viewModel.DownloadPage.LocalImportDialog.ClearDropState();

        e.Handled = true;
        e.Effects = DragDropEffects.None;
        return true;
    }

    private static string[]? TryGetDroppedPaths(DragEventArgs e)
    {
        // 只接受系统 FileDrop 格式，并复制为字符串快照，避免异步处理继续持有 DragEventArgs。
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return null;

        return e.Data.GetData(DataFormats.FileDrop) as string[];
    }

    private bool IsLocalImportDropPage()
    {
        return NavigationCatalog.UsesLocalModpackDrop(
            viewModel.CurrentPage,
            viewModel.GameSettingsPage.IsListStep);
    }

    private bool IsPointWithinWindow(Point point)
    {
        return point.X >= 0
               && point.Y >= 0
               && point.X <= ActualWidth
               && point.Y <= ActualHeight;
    }

}

