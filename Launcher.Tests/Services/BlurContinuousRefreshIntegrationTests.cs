/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.Controls;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class BlurContinuousRefreshIntegrationTests
{
    [Fact]
    public void PageTransitionOwnsOneContinuousRefreshLeaseAndSyncReleasesIt()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture();
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var service = new PageTransitionService(
                    Dispatcher.CurrentDispatcher,
                    page => string.Equals(page, "Settings", StringComparison.Ordinal)
                        ? fixture.Scope
                        : null,
                    "Home",
                    ["Home", "Settings"]);

                service.MoveTo("Settings");
                service.SyncTo("Settings");
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(fixture.Control));
                Assert.False(coordinator.IsContinuousRenderingActive);

                service.SyncTo("Home");
                service.MoveTo("Settings");
                PumpDispatcher(DispatcherPriority.Render);

                Assert.True(coordinator.IsContinuousRenderingActive);

                service.SyncTo("Settings");

                Assert.False(coordinator.IsContinuousRenderingActive);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    [Fact]
    public void SlidingContentTransitionOwnsOneContinuousRefreshLeaseAndSyncReleasesIt()
    {
        RunOnStaThread(() =>
        {
            var fixture = CreateFixture(includeSecondaryLayer: true);
            try
            {
                fixture.Window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = new SlidingContentTransitionCoordinator(
                    fixture.Root,
                    fixture.Root,
                    fixture.Scope,
                    fixture.SecondaryScope!);
                coordinator.Sync(showSecondaryLayer: false);
                coordinator.AnimateTo(showSecondaryLayer: true);

                var refreshCoordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(fixture.Control));
                Assert.True(refreshCoordinator.IsContinuousRenderingActive);

                coordinator.Sync(showSecondaryLayer: true);

                Assert.False(refreshCoordinator.IsContinuousRenderingActive);
            }
            finally
            {
                fixture.Window.Close();
            }
        });
    }

    private static BlurFixture CreateFixture(bool includeSecondaryLayer = false)
    {
        var source = new Border();
        var control = new BackdropBlurBorder
        {
            SourceElement = source,
            IsSourcePreblurred = true
        };
        var scope = new Grid();
        scope.Children.Add(control);

        var root = new Grid();
        root.Children.Add(source);
        root.Children.Add(scope);

        Grid? secondaryScope = null;
        if (includeSecondaryLayer)
        {
            secondaryScope = new Grid();
            root.Children.Add(secondaryScope);
        }

        var window = new Window
        {
            Width = 320d,
            Height = 200d,
            Left = -10000d,
            Top = -10000d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = root
        };
        return new BlurFixture(window, root, scope, secondaryScope, control);
    }

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
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

    private sealed record BlurFixture(
        Window Window,
        Grid Root,
        Grid Scope,
        Grid? SecondaryScope,
        BackdropBlurBorder Control);
}
