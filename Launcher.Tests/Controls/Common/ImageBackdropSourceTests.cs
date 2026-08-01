/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Launcher.App.Controls;

namespace Launcher.Tests.Controls.Common;

public sealed class ImageBackdropSourceTests
{
    [Fact]
    public void NativeImageBrushTracksImagePresentation()
    {
        RunOnStaThread(() =>
        {
            var image = CreateSolidImage(Colors.Red);
            var source = new ImageBackdropSource
            {
                Width = 640d,
                Height = 360d,
                ImageSource = image,
                OverlayBrush = Brushes.Black,
                OverlayOpacity = 0.35d
            };

            source.Measure(new Size(640d, 360d));
            source.Arrange(new Rect(0d, 0d, 640d, 360d));

            var brush = Assert.IsType<ImageBrush>(source.Background);
            Assert.Same(image, brush.ImageSource);
            Assert.Equal(Stretch.UniformToFill, brush.Stretch);
            Assert.Same(Brushes.Black, source.OverlayBrush);
            Assert.Equal(0.35d, source.OverlayOpacity);
        });
    }

    [Fact]
    public void ImageAssignedAfterInitialLayoutRendersWithoutAnotherLayoutPass()
    {
        RunOnStaThread(() =>
        {
            var source = new ImageBackdropSource
            {
                Width = 64d,
                Height = 64d
            };
            source.Measure(new Size(64d, 64d));
            source.Arrange(new Rect(0d, 0d, 64d, 64d));

            _ = RenderCenterPixel(source);
            source.ImageSource = CreateSolidImage(Colors.Red);
            PumpDispatcher(DispatcherPriority.Render);

            var rendered = RenderCenterPixel(source);

            Assert.True(rendered.R > 240);
            Assert.True(rendered.G < 15);
            Assert.True(rendered.B < 15);
            Assert.True(rendered.A > 240);
        });
    }

    [Theory]
    [InlineData(-0.01d)]
    [InlineData(1.01d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OverlayOpacityRejectsInvalidValues(double value)
    {
        RunOnStaThread(() =>
        {
            var source = new ImageBackdropSource();

            Assert.Throws<ArgumentException>(() => source.OverlayOpacity = value);
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
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw failure;
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

    private static void PumpDispatcher(DispatcherPriority priority)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            priority,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static Color RenderCenterPixel(ImageBackdropSource source)
    {
        var bitmap = new RenderTargetBitmap(
            64,
            64,
            96d,
            96d,
            PixelFormats.Pbgra32);
        bitmap.Render(source);

        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(32, 32, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
