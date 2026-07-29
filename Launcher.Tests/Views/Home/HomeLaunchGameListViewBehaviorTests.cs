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

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Converters;
using Launcher.App.Services;
using Launcher.App.ViewModels.Home;
using Launcher.App.Views.Home;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Views.Home;

[Collection(HomeLaunchGameListViewBehaviorTests.WpfCollectionName)]
public sealed class HomeLaunchGameListViewBehaviorTests
{
    internal const string WpfCollectionName = "Home launch menu WPF";

    [Fact]
    public void CollapseNormalizesOffscreenItemsButPreservesVisibleItemStart()
    {
        RunOnStaThread(() =>
        {
            var application = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Window? window = null;

            try
            {
                EnsureApplicationResources(application);
                var viewModel = CreateViewModel();
                var instances = CreateInstances(44);
                viewModel.SetLaunchInstances(instances);
                viewModel.SetSelectedInstance(instances[17]);

                var view = new HomeLaunchGameListView
                {
                    DataContext = viewModel,
                    Width = 900,
                    Height = 700
                };
                window = new Window
                {
                    Width = 900,
                    Height = 700,
                    Content = view,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0
                };
                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                view.SetPointerExpandedForTest(true);
                PumpAnimation();

                var scrollViewer = FindVisualDescendant<ScrollViewer>(view.LaunchInstanceListBox)
                    ?? throw new InvalidOperationException("Home launch menu ScrollViewer was not generated.");
                scrollViewer.ScrollToTop();
                view.UpdateLayout();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var selectedItem = Assert.IsType<HomeLaunchInstanceItem>(viewModel.SelectedLaunchInstanceItem);
                var beforeContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var beforeTop = beforeContainer
                    .TransformToAncestor(scrollViewer)
                    .Transform(new Point(0, 0))
                    .Y;
                Assert.True(beforeTop >= scrollViewer.ViewportHeight);

                application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 10000d;
                view.SetPointerExpandedForTest(false);
                PumpDispatcher(DispatcherPriority.Background);

                var collapseStartContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var belowViewportCollapseStartTop = collapseStartContainer
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                var expectedExpandedAnchor = view.HeaderOverlayElement.ActualHeight + 1;
                Assert.InRange(
                    belowViewportCollapseStartTop,
                    expectedExpandedAnchor - 1,
                    expectedExpandedAnchor + 1);

                application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1d;
                view.SetPointerExpandedForTest(true);
                PumpAnimation();
                view.SetPointerExpandedForTest(false);
                PumpAnimation();

                var afterContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var afterTopInScrollViewport = afterContainer
                    .TransformToAncestor(scrollViewer)
                    .Transform(new Point(0, 0))
                    .Y;
                var afterTopInCollapsedMenu = afterContainer
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;

                Assert.True(scrollViewer.VerticalOffset > 0);
                Assert.InRange(afterTopInScrollViewport, 0, scrollViewer.ActualHeight - afterContainer.ActualHeight);
                Assert.InRange(
                    afterTopInCollapsedMenu,
                    (view.CollapsedMenuHeight - afterContainer.ActualHeight) / 2 - 1,
                    (view.CollapsedMenuHeight - afterContainer.ActualHeight) / 2 + 1);

                view.SetPointerExpandedForTest(true);
                PumpAnimation();
                scrollViewer.ScrollToEnd();
                view.UpdateLayout();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 10000d;
                view.SetPointerExpandedForTest(false);
                PumpDispatcher(DispatcherPriority.Background);

                var aboveViewportCollapseStartContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var aboveViewportCollapseStartTop = aboveViewportCollapseStartContainer
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                Assert.InRange(
                    aboveViewportCollapseStartTop,
                    belowViewportCollapseStartTop - 1,
                    belowViewportCollapseStartTop + 1);

                application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1d;
                view.SetPointerExpandedForTest(true);
                PumpAnimation();
                scrollViewer.ScrollToVerticalOffset(650);
                view.UpdateLayout();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);

                var visibleContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var visibleTopBeforeCollapse = visibleContainer
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                Assert.InRange(
                    visibleTopBeforeCollapse,
                    expectedExpandedAnchor + 100,
                    scrollViewer.ActualHeight - visibleContainer.ActualHeight);

                application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 10000d;
                view.SetPointerExpandedForTest(false);
                PumpDispatcher(DispatcherPriority.Background);

                var visibleCollapseStartContainer = Assert.IsType<ListBoxItem>(
                    view.LaunchInstanceListBox.ItemContainerGenerator.ContainerFromItem(selectedItem));
                var visibleCollapseStartTop = visibleCollapseStartContainer
                    .TransformToAncestor(view.MenuPanelShadowElement)
                    .Transform(new Point(0, 0))
                    .Y;
                Assert.InRange(
                    visibleCollapseStartTop,
                    visibleTopBeforeCollapse - 3,
                    visibleTopBeforeCollapse + 1);
            }
            finally
            {
                window?.Close();
                application.Shutdown();
            }
        });
    }

    private static HomeLaunchGameListViewModel CreateViewModel()
        => new(new StubGameVersionService(), new StubStatusService(), _ => Task.FromResult(true));

    private static GameInstance[] CreateInstances(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new GameInstance
            {
                Id = $"instance-{index}",
                Name = $"Instance {index}",
                MinecraftVersion = "1.21.4",
                VersionName = "1.21.4",
                Loader = LoaderKind.Vanilla,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(-index)
            })
            .ToArray();
    }

    private static void EnsureApplicationResources(System.Windows.Application application)
    {
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Shared.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Resources/Themes/Dark.xaml"));
        application.Resources.MergedDictionaries.Add(LoadDictionary("Styles/ControlStyles.xaml"));
        application.Resources["IconSourceImageConverter"] = new IconSourceImageConverter();
        application.Resources["HomeLaunchMenuAnimationDurationMilliseconds"] = 1d;
    }

    private static ResourceDictionary LoadDictionary(string relativePath)
        => new()
        {
            Source = new Uri(
                $"pack://application:,,,/BlockHelm_Launcher_x64;component/{relativePath}",
                UriKind.Absolute)
        };

    private static void PumpAnimation()
    {
        PumpDispatcher(DispatcherPriority.Background);
        Thread.Sleep(30);
        PumpDispatcher(DispatcherPriority.ApplicationIdle);
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typedRoot)
            return typedRoot;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var result = FindVisualDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (result is not null)
                return result;
        }

        return null;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class StubGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = LauncherDefaults.DefaultDownloadSourcePreference,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
            => Task.FromResult<IReadOnlyList<MinecraftVersionInfo>>([]);
    }

    private sealed class StubStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message) => MessageReported?.Invoke(message);
    }
}

[CollectionDefinition(HomeLaunchGameListViewBehaviorTests.WpfCollectionName, DisableParallelization = true)]
public sealed class HomeLaunchGameListViewWpfCollection;
