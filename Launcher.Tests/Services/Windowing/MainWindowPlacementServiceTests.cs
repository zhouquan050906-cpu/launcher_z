/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using Launcher.App.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services.Windowing;

public sealed class MainWindowPlacementServiceTests
{
    [Fact]
    public void RestoreClampsSavedSizeToWindowMinimumAndAvailableWorkArea()
    {
        RunOnStaThread(() =>
        {
            var window = new Window
            {
                Width = LauncherDefaults.DefaultMainWindowWidth,
                Height = LauncherDefaults.DefaultMainWindowHeight,
                MinWidth = LauncherDefaults.MinimumMainWindowWidth,
                MinHeight = LauncherDefaults.MinimumMainWindowHeight
            };
            var settings = new LauncherSettings
            {
                MainWindowWidth = 5000d,
                MainWindowHeight = 100d,
                MainWindowWasMaximized = true
            };

            MainWindowPlacementService.Restore(window, settings, new Rect(0d, 0d, 1400d, 900d));

            Assert.Equal(1400d, window.Width);
            Assert.Equal(LauncherDefaults.MinimumMainWindowHeight, window.Height);
            Assert.Equal(WindowState.Maximized, window.WindowState);
        });
    }

    [Theory]
    [InlineData(WindowState.Normal, 1200d, 760d, 980d, 680d, 1200d, 760d, false)]
    [InlineData(WindowState.Maximized, 1920d, 1040d, 1180d, 740d, 1180d, 740d, true)]
    [InlineData(WindowState.Minimized, 0d, 0d, 1100d, 700d, 1100d, 700d, false)]
    public void CaptureUsesNormalBoundsAndNeverRestoresMinimizedState(
        WindowState state,
        double actualWidth,
        double actualHeight,
        double restoredWidth,
        double restoredHeight,
        double expectedWidth,
        double expectedHeight,
        bool expectedMaximized)
    {
        var snapshot = MainWindowPlacementService.CreateSnapshot(
            state,
            new Size(actualWidth, actualHeight),
            new Rect(0d, 0d, restoredWidth, restoredHeight),
            new Size(LauncherDefaults.DefaultMainWindowWidth, LauncherDefaults.DefaultMainWindowHeight));

        Assert.Equal(expectedWidth, snapshot.Width);
        Assert.Equal(expectedHeight, snapshot.Height);
        Assert.Equal(expectedMaximized, snapshot.WasMaximized);
    }

    [Fact]
    public async Task SaveUpdatesOnlyWindowPlacementSettings()
    {
        var settings = new LauncherSettings { Theme = "Light" };
        var settingsService = new TestSettingsService(settings);
        var service = new MainWindowPlacementService(settingsService);

        await service.SaveAsync(new MainWindowPlacementSnapshot(1280d, 800d, true));

        var saved = await settingsService.LoadAsync();
        Assert.Equal(1280d, saved.MainWindowWidth);
        Assert.Equal(800d, saved.MainWindowHeight);
        Assert.True(saved.MainWindowWasMaximized);
        Assert.Equal("Light", saved.Theme);
        Assert.Equal(1, settingsService.SaveCount);
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
