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
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Controls;

/// <summary>
/// Renders the launcher image background as a dedicated, shallow visual that local
/// backdrop surfaces can sample without capturing the complete window visual tree.
/// </summary>
public sealed class ImageBackdropSource : Border
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(ImageBackdropSource),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnImageSourceChanged));

    public static readonly DependencyProperty OverlayBrushProperty =
        DependencyProperty.Register(
            nameof(OverlayBrush),
            typeof(Brush),
            typeof(ImageBackdropSource),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayOpacityProperty =
        DependencyProperty.Register(
            nameof(OverlayOpacity),
            typeof(double),
            typeof(ImageBackdropSource),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender),
            IsValidOpacity);

    public ImageBackdropSource()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += ImageBackdropSource_Loaded;
        Background = CreateImageBrush(imageSource: null);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public Brush? OverlayBrush
    {
        get => (Brush?)GetValue(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    public double OverlayOpacity
    {
        get => (double)GetValue(OverlayOpacityProperty);
        set => SetValue(OverlayOpacityProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
            return;

        if (OverlayBrush is not { } overlayBrush || OverlayOpacity <= 0d)
            return;

        drawingContext.PushOpacity(OverlayOpacity);
        drawingContext.DrawRectangle(overlayBrush, null, bounds);
        drawingContext.Pop();
    }

    private static void OnImageSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ImageBackdropSource source)
            source.RefreshImageDrawing((ImageSource?)e.NewValue);
    }

    private void ImageBackdropSource_Loaded(object sender, RoutedEventArgs e)
    {
        // Replacing the native background after the element joins the presentation
        // source ensures VisualBrush consumers sample the displayed image.
        RefreshImageDrawing(ImageSource);
    }

    private void RefreshImageDrawing(ImageSource? imageSource)
    {
        Background = CreateImageBrush(imageSource);
        InvalidateVisual();
    }

    private static ImageBrush CreateImageBrush(ImageSource? imageSource)
    {
        return new ImageBrush(imageSource)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.UniformToFill
        };
    }

    private static bool IsValidOpacity(object value)
    {
        var opacity = (double)value;
        return double.IsFinite(opacity) && opacity is >= 0d and <= 1d;
    }
}
