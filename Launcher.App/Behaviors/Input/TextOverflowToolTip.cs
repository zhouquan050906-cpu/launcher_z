/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Launcher.App.Behaviors;

/// <summary>
/// Shows an existing plain-text tooltip only when its TextBlock cannot display all its text.
/// Measurement happens on opening, so recycled items and resized views use their current layout.
/// </summary>
public static class TextOverflowToolTip
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TextOverflowToolTip),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock)
            return;

        if ((bool)e.NewValue)
            textBlock.ToolTipOpening += OnToolTipOpening;
        else
            textBlock.ToolTipOpening -= OnToolTipOpening;
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is TextBlock textBlock && !HasOverflow(textBlock))
            e.Handled = true;
    }

    internal static bool HasOverflow(TextBlock textBlock)
    {
        if (string.IsNullOrEmpty(textBlock.Text) || !textBlock.IsArrangeValid)
            return false;

        var padding = textBlock.Padding;
        var width = Math.Max(0, textBlock.ActualWidth - padding.Left - padding.Right);
        var height = Math.Max(0, textBlock.ActualHeight - padding.Top - padding.Bottom);
        if (width <= 0 || height <= 0)
            return false;

        var dpi = VisualTreeHelper.GetDpi(textBlock);
        var text = new FormattedText(
            textBlock.Text,
            textBlock.Language.GetSpecificCulture(),
            textBlock.FlowDirection,
            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.Black,
            null,
            TextOptions.GetTextFormattingMode(textBlock),
            dpi.PixelsPerDip);

        if (textBlock.TextWrapping != TextWrapping.NoWrap)
            text.MaxTextWidth = width;
        if (!double.IsNaN(textBlock.LineHeight))
            text.LineHeight = textBlock.LineHeight;

        // Ignore subpixel metric differences introduced by layout rounding.
        return text.Width > width + 0.5 / dpi.DpiScaleX
            || text.Height > height + 0.5 / dpi.DpiScaleY;
    }
}
