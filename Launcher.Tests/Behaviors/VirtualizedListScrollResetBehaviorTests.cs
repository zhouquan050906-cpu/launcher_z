/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Launcher.App.Behaviors;
using Launcher.App.Utilities;

namespace Launcher.Tests.Behaviors;

public sealed class VirtualizedListScrollResetBehaviorTests
{
    [Fact]
    public void NewScrollResetTokenReturnsRetainedListToTopOnlyOnce()
    {
        RunOnStaThread(() =>
        {
            var listBox = new ListBox
            {
                Width = 360,
                Height = 220,
                ItemsSource = Enumerable.Range(0, 200)
                    .Select(index => $"Mod {index}")
                    .ToArray()
            };
            VirtualizedListItemStateBehavior.SetIsEnabled(listBox, true);

            var window = new Window
            {
                Width = 360,
                Height = 220,
                Content = listBox,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Opacity = 0
            };

            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.ApplicationIdle);
                listBox.UpdateLayout();

                var scrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(listBox, _ => true)
                    ?? throw new InvalidOperationException("ListBox ScrollViewer was not generated.");
                scrollViewer.ScrollToEnd();
                listBox.UpdateLayout();
                Assert.True(scrollViewer.VerticalOffset > 0);

                VirtualizedListItemStateBehavior.SetScrollResetToken(listBox, 1);
                PumpDispatcher(DispatcherPriority.Loaded);
                Assert.Equal(0, scrollViewer.VerticalOffset);

                scrollViewer.ScrollToVerticalOffset(240);
                listBox.UpdateLayout();
                var retainedOffset = scrollViewer.VerticalOffset;
                Assert.True(retainedOffset > 0);

                VirtualizedListItemStateBehavior.SetScrollResetToken(listBox, 1);
                PumpDispatcher(DispatcherPriority.Loaded);
                Assert.Equal(retainedOffset, scrollViewer.VerticalOffset);

                VirtualizedListItemStateBehavior.SetScrollResetToken(listBox, 2);
                PumpDispatcher(DispatcherPriority.Loaded);
                Assert.Equal(0, scrollViewer.VerticalOffset);
            }
            finally
            {
                window.Close();
            }
        });
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
}
