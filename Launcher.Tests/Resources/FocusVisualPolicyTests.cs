/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.CompilerServices;
using System.Windows;

namespace Launcher.Tests.Resources;

public sealed class FocusVisualPolicyTests
{
    [Fact]
    public void AppDefaultsSuppressButtonAndScrollViewerFocusVisuals()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(Launcher.App.App).TypeHandle);

                var button = Assert.IsAssignableFrom<FrameworkElement>(
                    Activator.CreateInstance(Type.GetType(
                        "System.Windows.Controls.Button, PresentationFramework",
                        throwOnError: true)!));
                var scrollViewer = Assert.IsAssignableFrom<FrameworkElement>(
                    Activator.CreateInstance(Type.GetType(
                        "System.Windows.Controls.ScrollViewer, PresentationFramework",
                        throwOnError: true)!));

                button.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, button));
                scrollViewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, scrollViewer));

                Assert.Null(button.FocusVisualStyle);
                Assert.Null(scrollViewer.FocusVisualStyle);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
