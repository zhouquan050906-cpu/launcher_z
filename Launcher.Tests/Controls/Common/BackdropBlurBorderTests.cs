/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Common;

public sealed class BackdropBlurBorderTests
{
    [Fact]
    public void DefaultsUseTheHistoricalGaussianBlurConfiguration()
    {
        RunOnStaThread(() =>
        {
            var content = new Button();
            var control = CreateControl(content);
            Arrange(control, 80d, 40d);

            Assert.Same(content, control.Content);
            Assert.Equal(42d, control.BlurRadius);
            Assert.True(control.IsBlurEnabled);
            Assert.False(control.IsSourcePreblurred);
            Assert.False(control.IsTintEnabled);
            Assert.Equal(RenderingBias.Performance, control.BlurRenderingBias);
            Assert.Equal(KernelType.Gaussian, control.BackdropEffect?.KernelType);
            Assert.Equal(42d, control.BackdropEffect?.Radius);
        });
    }

    [Fact]
    public void InvalidOrDisabledSourcesLeaveTheForegroundAndFallbackLayersAvailable()
    {
        RunOnStaThread(() =>
        {
            var root = new Grid();
            var source = new Border();
            var content = new Button();
            var control = CreateControl(content);
            root.Children.Add(source);
            root.Children.Add(control);
            Arrange(root, 160d, 90d);

            control.SourceElement = root;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);
            Assert.Same(content, control.Content);

            control.SourceElement = source;
            control.IsBlurEnabled = false;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);

            control.IsBlurEnabled = true;
            control.SourceElement = null;
            control.RefreshBackdrop();
            Assert.False(control.IsBackdropActive);
            Assert.Null(control.BackdropBrush?.Visual);
        });
    }

    [Fact]
    public void HiddenAncestorStopsTrackingAndVisibleAncestorRestartsIt()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var host = new Grid();
            var control = CreateControl(new Border());
            control.SourceElement = source;
            control.IsSourcePreblurred = true;
            host.Children.Add(control);

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(host);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(control));
                Assert.True(control.IsRenderTrackingActive);
                Assert.Equal(1, coordinator.RegisteredCount);

                control.RefreshBackdrop();
                Assert.True(control.IsBackdropActive);

                host.Visibility = Visibility.Collapsed;
                PumpDispatcher(DispatcherPriority.Render);

                Assert.False(control.IsRenderTrackingActive);
                Assert.Equal(0, coordinator.RegisteredCount);
                Assert.False(control.IsBackdropActive);
                Assert.Null(control.BackdropBrush?.Visual);

                host.Visibility = Visibility.Visible;
                PumpDispatcher(DispatcherPriority.Render);

                Assert.True(control.IsRenderTrackingActive);
                Assert.Equal(1, coordinator.RegisteredCount);
                Assert.True(control.IsBackdropActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CoordinatorCoalescesDuplicateRequestsAndUsesOneContinuousRenderLease()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var scope = new Grid();
            var control = CreateControl(new Border());
            control.SourceElement = source;
            control.IsSourcePreblurred = true;
            scope.Children.Add(control);

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(scope);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(control));
                coordinator.ProcessPendingBatchForTesting();
                var initialBatches = coordinator.TotalBatchCount;
                var initialRefreshes = coordinator.TotalRefreshCount;

                coordinator.RequestRefresh(control, BackdropBlurRefreshReason.Layout);
                coordinator.RequestRefresh(control, BackdropBlurRefreshReason.Scroll);
                coordinator.RequestRefresh(control, BackdropBlurRefreshReason.Size);

                Assert.Equal(1, coordinator.PendingCount);
                Assert.True(coordinator.IsRenderingActive);
                var firstRenderingTime = TimeSpan.FromSeconds(10);
                coordinator.ProcessRenderFrameForTesting(firstRenderingTime);
                Assert.Equal(initialBatches + 1, coordinator.TotalBatchCount);
                Assert.Equal(initialRefreshes + 1, coordinator.TotalRefreshCount);
                Assert.False(coordinator.IsRenderingActive);

                coordinator.RequestRefresh(control, BackdropBlurRefreshReason.Layout);
                coordinator.ProcessRenderFrameForTesting(firstRenderingTime);
                Assert.Equal(initialBatches + 1, coordinator.TotalBatchCount);
                Assert.Equal(1, coordinator.PendingCount);
                Assert.True(coordinator.IsRenderingActive);

                coordinator.ProcessRenderFrameForTesting(firstRenderingTime + TimeSpan.FromTicks(1));
                Assert.Equal(initialBatches + 2, coordinator.TotalBatchCount);
                Assert.Equal(initialRefreshes + 2, coordinator.TotalRefreshCount);
                Assert.False(coordinator.IsRenderingActive);

                using (BackdropBlurRefreshCoordinator.BeginContinuousRefresh(scope))
                {
                    Assert.True(coordinator.IsContinuousRenderingActive);
                    var animationRenderingTime = firstRenderingTime + TimeSpan.FromTicks(2);
                    coordinator.ProcessRenderFrameForTesting(animationRenderingTime);
                    coordinator.ProcessRenderFrameForTesting(animationRenderingTime);
                    Assert.Equal(initialBatches + 3, coordinator.TotalBatchCount);
                    Assert.Equal(initialRefreshes + 3, coordinator.TotalRefreshCount);
                }

                Assert.False(coordinator.IsContinuousRenderingActive);
                Assert.False(coordinator.IsRenderingActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void CoordinatorSharesSourceAndScrollObserversAndReleasesThemWithTheLastControl()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var alternateSource = new Border();
            var firstControl = CreateControl(new Border());
            var secondControl = CreateControl(new Border());
            firstControl.SourceElement = source;
            secondControl.SourceElement = source;

            var content = new StackPanel();
            content.Children.Add(firstControl);
            content.Children.Add(secondControl);
            var scrollViewer = new ScrollViewer { Content = content };

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(alternateSource);
            root.Children.Add(scrollViewer);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(firstControl));
                coordinator.ProcessPendingBatchForTesting();

                Assert.Equal(2, coordinator.RegisteredCount);
                Assert.Equal(1, coordinator.ObservedSourceCount);
                Assert.Equal(1, coordinator.ObservedScrollViewerCount);
                Assert.True(coordinator.IsWindowLayoutObserved);

                var initialBatches = coordinator.TotalBatchCount;
                var initialRefreshes = coordinator.TotalRefreshCount;
                coordinator.ProcessScrollChangedForTesting(scrollViewer);
                coordinator.ProcessScrollChangedForTesting(scrollViewer);
                Assert.Equal(0, coordinator.PendingCount);
                Assert.True(coordinator.IsRenderingActive);
                coordinator.ProcessPendingBatchForTesting();
                Assert.Equal(initialBatches, coordinator.TotalBatchCount);
                Assert.Equal(initialRefreshes, coordinator.TotalRefreshCount);

                secondControl.SourceElement = alternateSource;
                Assert.Equal(2, coordinator.ObservedSourceCount);

                firstControl.SourceElement = alternateSource;
                Assert.Equal(1, coordinator.ObservedSourceCount);

                firstControl.Visibility = Visibility.Collapsed;
                Assert.Equal(1, coordinator.RegisteredCount);
                Assert.Equal(1, coordinator.ObservedSourceCount);
                Assert.Equal(1, coordinator.ObservedScrollViewerCount);

                secondControl.Visibility = Visibility.Collapsed;
                Assert.Equal(0, coordinator.RegisteredCount);
                Assert.Equal(0, coordinator.ObservedSourceCount);
                Assert.Equal(0, coordinator.ObservedScrollViewerCount);
                Assert.False(coordinator.IsWindowLayoutObserved);
                Assert.False(coordinator.IsRenderingActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ScrollObservationRefreshesOnlyControlsWhoseViewportGeometryChanges()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var visibleControl = CreateControl(new Border());
            var offscreenControl = CreateControl(new Border());
            visibleControl.SourceElement = source;
            offscreenControl.SourceElement = source;
            visibleControl.Height = 60d;
            offscreenControl.Height = 60d;

            var content = new StackPanel();
            content.Children.Add(visibleControl);
            content.Children.Add(new Border { Height = 300d });
            content.Children.Add(offscreenControl);

            var scrollViewer = new ScrollViewer
            {
                Width = 200d,
                Height = 100d,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = content
            };
            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(scrollViewer);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(visibleControl));
                coordinator.ProcessPendingBatchForTesting();
                visibleControl.RefreshBackdrop();
                offscreenControl.RefreshBackdrop();
                Assert.True(visibleControl.IsBackdropActive);
                Assert.False(offscreenControl.IsBackdropActive);

                var initialRefreshes = coordinator.TotalRefreshCount;
                scrollViewer.ScrollToVerticalOffset(20d);
                scrollViewer.UpdateLayout();
                coordinator.ProcessScrollChangedForTesting(scrollViewer);
                coordinator.ProcessPendingBatchForTesting();

                Assert.Equal(initialRefreshes + 1, coordinator.TotalRefreshCount);
                Assert.True(visibleControl.IsBackdropActive);
                Assert.False(offscreenControl.IsBackdropActive);

                var refreshesBeforeEnd = coordinator.TotalRefreshCount;
                scrollViewer.ScrollToEnd();
                scrollViewer.UpdateLayout();
                coordinator.ProcessScrollChangedForTesting(scrollViewer);
                coordinator.ProcessPendingBatchForTesting();

                Assert.Equal(refreshesBeforeEnd + 2, coordinator.TotalRefreshCount);
                Assert.False(visibleControl.IsBackdropActive);
                Assert.True(offscreenControl.IsBackdropActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LayoutObservationQueuesOnlyWhenBackdropGeometryChanges()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var control = CreateControl(new Border());
            control.SourceElement = source;
            control.Width = 100d;
            control.Height = 60d;
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.VerticalAlignment = VerticalAlignment.Top;

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(control);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                    BackdropBlurRefreshCoordinator.TryGet(control));
                coordinator.ProcessPendingBatchForTesting();
                control.RefreshBackdrop();

                var initialBatches = coordinator.TotalBatchCount;
                coordinator.ProcessLayoutUpdatedForTesting();
                Assert.Equal(0, coordinator.PendingCount);
                Assert.True(coordinator.IsRenderingActive);
                coordinator.ProcessRenderFrameForTesting(TimeSpan.FromSeconds(20));
                Assert.Equal(initialBatches, coordinator.TotalBatchCount);
                Assert.False(coordinator.IsRenderingActive);

                control.Margin = new Thickness(18d, 0d, 0d, 0d);
                root.UpdateLayout();
                coordinator.ProcessLayoutUpdatedForTesting();
                coordinator.ProcessLayoutUpdatedForTesting();
                Assert.Equal(0, coordinator.PendingCount);
                Assert.True(coordinator.IsRenderingActive);

                coordinator.ProcessRenderFrameForTesting(TimeSpan.FromSeconds(21));
                Assert.Equal(0, coordinator.PendingCount);
                Assert.Equal(initialBatches + 1, coordinator.TotalBatchCount);
                Assert.False(coordinator.IsRenderingActive);

                coordinator.ProcessLayoutUpdatedForTesting();
                coordinator.ProcessRenderFrameForTesting(TimeSpan.FromSeconds(22));
                Assert.Equal(0, coordinator.PendingCount);
                Assert.Equal(initialBatches + 1, coordinator.TotalBatchCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WindowCloseClearsPendingRenderingAndSharedObservers()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var control = CreateControl(new Border());
            control.SourceElement = source;

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(control);

            var window = CreateTestWindow(root);
            window.Show();
            PumpDispatcher(DispatcherPriority.Render);

            var coordinator = Assert.IsType<BackdropBlurRefreshCoordinator>(
                BackdropBlurRefreshCoordinator.TryGet(control));
            coordinator.RequestRefresh(control, BackdropBlurRefreshReason.Layout);
            Assert.True(coordinator.IsRenderingActive);

            window.Close();

            Assert.Equal(0, coordinator.RegisteredCount);
            Assert.Equal(0, coordinator.PendingCount);
            Assert.Equal(0, coordinator.ObservedSourceCount);
            Assert.Equal(0, coordinator.ObservedScrollViewerCount);
            Assert.False(coordinator.IsWindowLayoutObserved);
            Assert.False(coordinator.IsRenderingActive);
        });
    }

    [Fact]
    public void LocalBlurUsesFixedReducedRenderScale()
    {
        RunOnStaThread(() =>
        {
            var root = new Grid();
            var source = new Border();
            var control = CreateControl(new Border());
            control.SourceElement = source;
            root.Children.Add(source);
            root.Children.Add(control);
            var window = CreateTestWindow(root);
            window.Width = 1936d;
            window.Height = 1096d;
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                Assert.False(control.IsSourcePreblurred);
                control.RefreshBackdrop();
                Assert.Equal(0.2d, control.LocalBlurRenderScale);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ImageBackgroundUsesDedicatedVisualInsteadOfTheWholeWindowTree()
    {
        RunOnStaThread(() =>
        {
            var source = new ImageBackdropSource
            {
                OverlayBrush = Brushes.Black,
                OverlayOpacity = 0.25d
            };
            var control = CreateControl(new Border());
            control.SourceElement = source;

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(control);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                control.RefreshBackdrop();

                Assert.True(control.IsBackdropActive);
                Assert.Same(source, Assert.IsType<VisualBrush>(control.BackdropBrush).Visual);
                Assert.Null(control.BackdropDrawingBrush);
                Assert.False(control.IsUsingDrawingSource);
                Assert.Equal(0.2d, control.LocalBlurRenderScale);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ImageVisualSourceRendersBlurContentDuringInitialPresentation()
    {
        RunOnStaThread(() =>
        {
            var source = new ImageBackdropSource
            {
                ImageSource = CreateSolidImage(Colors.Red)
            };
            var control = CreateControl(content: null!);
            control.Width = 100d;
            control.Height = 60d;
            control.BaseBrush = Brushes.Transparent;
            control.TintBrush = Brushes.Transparent;
            control.OverlayBrush = Brushes.Transparent;
            control.SourceElement = source;

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(control);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = BackdropBlurRefreshCoordinator.TryGet(control);
                Assert.NotNull(coordinator);
                coordinator.ProcessPendingBatchForTesting();
                PumpDispatcher(DispatcherPriority.Render);

                var rendered = RenderCenterPixel(control, 100, 60);

                Assert.True(
                    rendered.R > 150
                    && rendered.G < 30
                    && rendered.B < 30
                    && rendered.A > 150,
                    $"Expected opaque red blur content, but rendered {rendered}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ImageVisualSourceContentChangeRefreshesSurface()
    {
        RunOnStaThread(() =>
        {
            var source = new ImageBackdropSource
            {
                ImageSource = CreateSolidImage(Colors.Red)
            };
            var control = CreateControl(content: null!);
            control.Width = 100d;
            control.Height = 60d;
            control.BaseBrush = Brushes.Transparent;
            control.TintBrush = Brushes.Transparent;
            control.OverlayBrush = Brushes.Transparent;
            control.SourceElement = source;

            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(control);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                var coordinator = BackdropBlurRefreshCoordinator.TryGet(control);
                Assert.NotNull(coordinator);
                coordinator.ProcessPendingBatchForTesting();
                var initialBrush = Assert.IsType<VisualBrush>(control.BackdropBrush);

                source.ImageSource = CreateSolidImage(Colors.Blue);
                PumpDispatcher(DispatcherPriority.Render);
                coordinator.ProcessPendingBatchForTesting();
                PumpDispatcher(DispatcherPriority.Render);

                var refreshedBrush = Assert.IsType<VisualBrush>(control.BackdropBrush);
                Assert.Same(initialBrush, refreshedBrush);
                Assert.Same(source, refreshedBrush.Visual);

                var rendered = RenderCenterPixel(control, 100, 60);
                Assert.True(
                    rendered.B > 150
                    && rendered.G < 30
                    && rendered.R < 30
                    && rendered.A > 150,
                    $"Expected opaque blue blur content, but rendered {rendered}. " +
                    $"Viewbox={refreshedBrush.Viewbox}, Viewport={refreshedBrush.Viewport}, " +
                    $"Effect={control.BackdropEffect is not null}, Scale={control.LocalBlurRenderScale}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void BlurOutsideNearestScrollViewportIsDeactivated()
    {
        RunOnStaThread(() =>
        {
            var source = new Border();
            var control = CreateControl(new Border());
            control.SourceElement = source;
            control.Height = 60d;

            var scrollContent = new StackPanel();
            scrollContent.Children.Add(new Border { Height = 400d });
            scrollContent.Children.Add(control);

            var scrollViewer = new ScrollViewer
            {
                Height = 100d,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = scrollContent
            };
            var root = new Grid();
            root.Children.Add(source);
            root.Children.Add(scrollViewer);

            var window = CreateTestWindow(root);
            try
            {
                window.Show();
                PumpDispatcher(DispatcherPriority.Render);

                control.RefreshBackdrop();
                Assert.False(control.IsBackdropActive);

                scrollViewer.ScrollToEnd();
                PumpDispatcher(DispatcherPriority.Render);

                control.RefreshBackdrop();
                Assert.True(control.IsBackdropActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static BackdropBlurBorder CreateControl(object content)
    {
        var control = new BackdropBlurBorder
        {
            Content = content,
            Template = CreateTemplate()
        };
        control.ApplyTemplate();
        return control;
    }

    private static ControlTemplate CreateTemplate()
    {
        const string xaml = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:controls="clr-namespace:Launcher.App.Controls;assembly=BlockHelm_Launcher_x64"
                             TargetType="{x:Type controls:BackdropBlurBorder}">
                <Grid>
                    <Border x:Name="PART_BlurLayer" Visibility="Collapsed">
                        <Border.CacheMode>
                            <BitmapCache EnableClearType="False"
                                         RenderAtScale="0.2"
                                         SnapsToDevicePixels="True" />
                        </Border.CacheMode>
                        <Border.Background>
                            <VisualBrush ViewboxUnits="Absolute"
                                         ViewportUnits="Absolute"
                                         TileMode="FlipXY" />
                        </Border.Background>
                        <Border.Effect>
                            <BlurEffect KernelType="Gaussian"
                                        Radius="{Binding BlurRadius, RelativeSource={RelativeSource TemplatedParent}}"
                                        RenderingBias="{Binding BlurRenderingBias, RelativeSource={RelativeSource TemplatedParent}}" />
                        </Border.Effect>
                    </Border>
                    <ContentPresenter Content="{TemplateBinding Content}" />
                </Grid>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(xaml);
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        var size = new Size(width, height);
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
    }

    private static Window CreateTestWindow(UIElement content)
    {
        return new Window
        {
            Width = 320d,
            Height = 200d,
            Left = -10000d,
            Top = -10000d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content
        };
    }

    private static DrawingImage CreateSolidImage(Color color)
    {
        var image = new DrawingImage(new GeometryDrawing(
            new SolidColorBrush(color),
            null,
            new RectangleGeometry(new Rect(0d, 0d, 1d, 1d))));
        image.Freeze();
        return image;
    }

    private static Color RenderCenterPixel(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96d,
            96d,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var pixel = new byte[4];
        bitmap.CopyPixels(
            new Int32Rect(width / 2, height / 2, 1, 1),
            pixel,
            4,
            0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
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
}
