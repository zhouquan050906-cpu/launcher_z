/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Behaviors;
using Launcher.App.Controls;

namespace Launcher.Tests.Behaviors;

public sealed class BackdropBlurHostTests
{
    [Fact]
    public void AppliedHostPreservesContentAndPaddingAboveTheBackdrop()
    {
        RunOnStaThread(() =>
        {
            var content = new TextBlock { Text = "Foreground" };
            var host = new Border
            {
                Background = Brushes.Red,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Child = content
            };
            BackdropBlurHost.SetFallbackBrush(host, Brushes.Red);
            BackdropBlurHost.SetIsBlurEnabled(host, true);
            BackdropBlurHost.SetIsApplied(host, true);

            host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var layers = Assert.IsType<Grid>(host.Child);
            var backdrop = Assert.IsType<BackdropBlurBorder>(layers.Children[0]);
            var contentHost = Assert.IsType<Border>(layers.Children[1]);
            Assert.True(backdrop.IsBlurEnabled);
            Assert.Same(content, contentHost.Child);
            Assert.Equal(new Thickness(14, 10, 14, 10), contentHost.Padding);
            Assert.True(BackdropBlurHost.GetIsBlurSuppressed(contentHost));
            Assert.Equal(default, host.Padding);
            Assert.Equal(Brushes.Transparent, host.Background);
        });
    }

    [Fact]
    public void SuppressedAncestorCreatesTintOnlyBackdropAndPreservesContent()
    {
        RunOnStaThread(() =>
        {
            var content = new TextBlock { Text = "Dialog content" };
            var host = new Border
            {
                Background = Brushes.Red,
                Padding = new Thickness(12),
                Child = content
            };
            var dialogScope = new Grid();
            BackdropBlurHost.SetIsBlurSuppressed(dialogScope, true);
            dialogScope.Children.Add(host);

            BackdropBlurHost.SetFallbackBrush(host, Brushes.Blue);
            BackdropBlurHost.SetIsBlurEnabled(host, true);
            BackdropBlurHost.SetIsApplied(host, true);
            host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.True(BackdropBlurHost.GetIsBlurSuppressed(host));
            var layers = Assert.IsType<Grid>(host.Child);
            var backdrop = Assert.IsType<BackdropBlurBorder>(layers.Children[0]);
            var contentHost = Assert.IsType<Border>(layers.Children[1]);
            Assert.False(backdrop.IsBlurEnabled);
            Assert.Same(content, contentHost.Child);
            Assert.Equal(new Thickness(12), contentHost.Padding);
            Assert.Equal(Brushes.Transparent, host.Background);
            Assert.Equal(Brushes.Blue, BackdropBlurHost.GetFallbackBrush(host));
        });
    }

    [Fact]
    public void AppliedHostSuppressesNestedBackdropButKeepsItsFallbackSurface()
    {
        RunOnStaThread(() =>
        {
            var nestedContent = new TextBlock { Text = "Nested content" };
            var nestedHost = new Border
            {
                Background = Brushes.Blue,
                Child = nestedContent
            };
            BackdropBlurHost.SetFallbackBrush(nestedHost, Brushes.Blue);
            BackdropBlurHost.SetIsBlurEnabled(nestedHost, true);
            BackdropBlurHost.SetIsApplied(nestedHost, true);
            nestedHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var nestedLayers = Assert.IsType<Grid>(nestedHost.Child);
            var nestedBackdrop = Assert.IsType<BackdropBlurBorder>(nestedLayers.Children[0]);
            Assert.True(nestedBackdrop.IsBlurEnabled);

            var outerHost = new Border
            {
                Background = Brushes.Red,
                Child = nestedHost
            };
            BackdropBlurHost.SetFallbackBrush(outerHost, Brushes.Red);
            BackdropBlurHost.SetIsBlurEnabled(outerHost, true);
            BackdropBlurHost.SetIsApplied(outerHost, true);
            outerHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var outerLayers = Assert.IsType<Grid>(outerHost.Child);
            var outerBackdrop = Assert.IsType<BackdropBlurBorder>(outerLayers.Children[0]);
            var outerContentHost = Assert.IsType<Border>(outerLayers.Children[1]);

            Assert.True(outerBackdrop.IsBlurEnabled);
            Assert.True(BackdropBlurHost.GetIsBlurSuppressed(outerContentHost));
            Assert.True(BackdropBlurHost.GetIsBlurSuppressed(nestedHost));
            Assert.False(nestedBackdrop.IsBlurEnabled);
            Assert.Equal(Brushes.Blue, BackdropBlurHost.GetFallbackBrush(nestedHost));
            Assert.Same(nestedContent, Assert.IsType<Border>(nestedLayers.Children[1]).Child);
        });
    }

    [Fact]
    public void NestedHostLoadedAfterOuterHostCreatesTintOnlyBackdropWithoutBlur()
    {
        RunOnStaThread(() =>
        {
            var nestedContent = new TextBlock { Text = "Nested content" };
            var nestedHost = new Border
            {
                Background = Brushes.Blue,
                Padding = new Thickness(6),
                Child = nestedContent
            };
            BackdropBlurHost.SetFallbackBrush(nestedHost, Brushes.Blue);
            BackdropBlurHost.SetIsBlurEnabled(nestedHost, true);
            BackdropBlurHost.SetIsApplied(nestedHost, true);

            var outerHost = new Border
            {
                Background = Brushes.Red,
                Child = nestedHost
            };
            BackdropBlurHost.SetFallbackBrush(outerHost, Brushes.Red);
            BackdropBlurHost.SetIsBlurEnabled(outerHost, true);
            BackdropBlurHost.SetIsApplied(outerHost, true);
            outerHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            nestedHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.True(BackdropBlurHost.GetIsBlurSuppressed(nestedHost));
            var nestedLayers = Assert.IsType<Grid>(nestedHost.Child);
            var nestedBackdrop = Assert.IsType<BackdropBlurBorder>(nestedLayers.Children[0]);
            var nestedContentHost = Assert.IsType<Border>(nestedLayers.Children[1]);
            Assert.False(nestedBackdrop.IsBlurEnabled);
            Assert.Same(nestedContent, nestedContentHost.Child);
            Assert.Equal(new Thickness(6), nestedContentHost.Padding);
            Assert.Equal(Brushes.Transparent, nestedHost.Background);
            Assert.Equal(Brushes.Blue, BackdropBlurHost.GetFallbackBrush(nestedHost));
        });
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
