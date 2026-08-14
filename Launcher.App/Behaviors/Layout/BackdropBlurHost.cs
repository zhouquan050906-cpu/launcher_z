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
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Launcher.App.Controls;
using Serilog;

namespace Launcher.App.Behaviors;

public static class BackdropBlurHost
{
    public static readonly DependencyProperty IsAppliedProperty =
        DependencyProperty.RegisterAttached(
            "IsApplied",
            typeof(bool),
            typeof(BackdropBlurHost),
            new PropertyMetadata(false, OnIsAppliedChanged));

    public static readonly DependencyProperty IsBlurEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsBlurEnabled",
            typeof(bool),
            typeof(BackdropBlurHost),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsBlurSuppressedProperty =
        DependencyProperty.RegisterAttached(
            "IsBlurSuppressed",
            typeof(bool),
            typeof(BackdropBlurHost),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.Inherits,
                OnIsBlurSuppressedChanged));

    public static readonly DependencyProperty FallbackBrushProperty =
        DependencyProperty.RegisterAttached(
            "FallbackBrush",
            typeof(Brush),
            typeof(BackdropBlurHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LightweightShadowEffectProperty =
        DependencyProperty.RegisterAttached(
            "LightweightShadowEffect",
            typeof(DropShadowEffect),
            typeof(BackdropBlurHost),
            new PropertyMetadata(null, OnLightweightShadowEffectChanged));

    private static readonly DependencyProperty BackdropProperty =
        DependencyProperty.RegisterAttached(
            "Backdrop",
            typeof(BackdropBlurBorder),
            typeof(BackdropBlurHost),
            new PropertyMetadata(null));

    private static readonly DependencyProperty ShadowChromeProperty =
        DependencyProperty.RegisterAttached(
            "ShadowChrome",
            typeof(CardShadowChrome),
            typeof(BackdropBlurHost),
            new PropertyMetadata(null));

    public static bool GetIsApplied(DependencyObject element) =>
        (bool)element.GetValue(IsAppliedProperty);

    public static void SetIsApplied(DependencyObject element, bool value) =>
        element.SetValue(IsAppliedProperty, value);

    public static bool GetIsBlurEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsBlurEnabledProperty);

    public static void SetIsBlurEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsBlurEnabledProperty, value);

    public static bool GetIsBlurSuppressed(DependencyObject element) =>
        (bool)element.GetValue(IsBlurSuppressedProperty);

    public static void SetIsBlurSuppressed(DependencyObject element, bool value) =>
        element.SetValue(IsBlurSuppressedProperty, value);

    public static Brush? GetFallbackBrush(DependencyObject element) =>
        (Brush?)element.GetValue(FallbackBrushProperty);

    public static void SetFallbackBrush(DependencyObject element, Brush? value) =>
        element.SetValue(FallbackBrushProperty, value);

    public static DropShadowEffect? GetLightweightShadowEffect(DependencyObject element) =>
        (DropShadowEffect?)element.GetValue(LightweightShadowEffectProperty);

    public static void SetLightweightShadowEffect(DependencyObject element, DropShadowEffect? value) =>
        element.SetValue(LightweightShadowEffectProperty, value);

    private static void OnIsAppliedChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Border border)
            return;

        border.Loaded -= Border_Loaded;
        if (e.NewValue is true)
        {
            border.Loaded += Border_Loaded;
            if (border.IsLoaded)
                ApplyBackdrop(border);
        }
        else if (border.GetValue(BackdropProperty) is BackdropBlurBorder backdrop)
        {
            backdrop.IsBlurEnabled = false;
        }
    }

    private static void OnIsBlurSuppressedChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Border border || !GetIsApplied(border))
            return;

        if (e.NewValue is true)
        {
            if (border.GetValue(BackdropProperty) is BackdropBlurBorder backdrop)
            {
                BindingOperations.ClearBinding(
                    backdrop,
                    BackdropBlurBorder.IsBlurEnabledProperty);
                backdrop.IsBlurEnabled = false;
            }

            return;
        }

        if (border.GetValue(BackdropProperty) is BackdropBlurBorder existingBackdrop)
        {
            BindBlurEnabled(existingBackdrop, border);
        }
        else if (border.IsLoaded)
        {
            ApplyBackdrop(border);
        }
    }

    private static void OnLightweightShadowEffectChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is Border border
            && border.GetValue(BackdropProperty) is BackdropBlurBorder backdrop)
        {
            UpdateLightweightShadow(border, backdrop);
        }
    }

    private static void Border_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
            ApplyBackdrop(border);
    }

    private static void ApplyBackdrop(Border border)
    {
        if (border.GetValue(BackdropProperty) is BackdropBlurBorder)
            return;

        var originalChild = border.Child;
        if (originalChild is not null)
            border.Child = null;

        var backdrop = new BackdropBlurBorder
        {
            IsHitTestVisible = false
        };
        backdrop.SetResourceReference(
            FrameworkElement.StyleProperty,
            "SurfaceBackdropBlurStyle");
        BindBlurEnabled(backdrop, border);
        BindingOperations.SetBinding(
            backdrop,
            BackdropBlurBorder.CornerRadiusProperty,
            new Binding(nameof(Border.CornerRadius)) { Source = border });
        BindingOperations.SetBinding(
            backdrop,
            RoundedClip.RadiusProperty,
            new Binding("CornerRadius.TopLeft") { Source = border });

        var layers = new Grid();
        layers.Children.Add(backdrop);
        if (originalChild is not null)
        {
            var contentHost = new Border
            {
                Background = Brushes.Transparent,
                Padding = border.Padding,
                Child = originalChild
            };
            SetIsBlurSuppressed(contentHost, true);
            layers.Children.Add(contentHost);
        }

        border.Padding = default;
        border.Background = Brushes.Transparent;
        border.Child = layers;
        border.SetValue(BackdropProperty, backdrop);
        UpdateLightweightShadow(border, backdrop);
    }

    private static void UpdateLightweightShadow(Border border, BackdropBlurBorder backdrop)
    {
        var shadowEffect = GetLightweightShadowEffect(border);
        var existingChrome = border.GetValue(ShadowChromeProperty) as CardShadowChrome;

        if (shadowEffect is null)
        {
            if (existingChrome?.Parent is Panel parent)
                parent.Children.Remove(existingChrome);
            if (existingChrome is not null)
            {
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.ReferenceEffectProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.CornerRadiusProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.SurfaceBrushProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.SurfaceBorderBrushProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.TintBrushProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.OverlayBrushProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.IsBackdropBlurEnabledProperty);
                BindingOperations.ClearBinding(existingChrome, CardShadowChrome.BackdropSourceProperty);
                border.ClearValue(ShadowChromeProperty);
            }
            return;
        }

        if (existingChrome is not null)
            return;

        try
        {
            if (backdrop.Parent is not Grid layers)
                throw new InvalidOperationException("The surface backdrop is not hosted by the expected layer grid.");

            var borderThickness = border.BorderThickness;
            var chrome = new CardShadowChrome
            {
                Margin = new Thickness(
                    -borderThickness.Left,
                    -borderThickness.Top,
                    -borderThickness.Right,
                    -borderThickness.Bottom)
            };
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.ReferenceEffectProperty,
                new Binding
                {
                    Source = border,
                    Path = new PropertyPath("(0)", LightweightShadowEffectProperty)
                });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.CornerRadiusProperty,
                new Binding(nameof(Border.CornerRadius)) { Source = border });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.SurfaceBrushProperty,
                new Binding(nameof(BackdropBlurBorder.BaseBrush)) { Source = backdrop });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.SurfaceBorderBrushProperty,
                new Binding(nameof(Border.BorderBrush)) { Source = border });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.TintBrushProperty,
                new Binding(nameof(BackdropBlurBorder.TintBrush)) { Source = backdrop });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.OverlayBrushProperty,
                new Binding(nameof(BackdropBlurBorder.OverlayBrush)) { Source = backdrop });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.IsBackdropBlurEnabledProperty,
                new Binding(nameof(BackdropBlurBorder.IsBlurEnabled)) { Source = backdrop });
            BindingOperations.SetBinding(
                chrome,
                CardShadowChrome.BackdropSourceProperty,
                new Binding(nameof(BackdropBlurBorder.SourceElement)) { Source = backdrop });

            layers.Children.Insert(0, chrome);
            border.SetValue(ShadowChromeProperty, chrome);
            border.ClearValue(UIElement.EffectProperty);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to initialize the lightweight card shadow. The card shadow will remain disabled.");
            border.ClearValue(UIElement.EffectProperty);
        }
    }

    private static void BindBlurEnabled(
        BackdropBlurBorder backdrop,
        Border border)
    {
        if (GetIsBlurSuppressed(border))
        {
            BindingOperations.ClearBinding(
                backdrop,
                BackdropBlurBorder.IsBlurEnabledProperty);
            backdrop.IsBlurEnabled = false;
            return;
        }

        BindingOperations.SetBinding(
            backdrop,
            BackdropBlurBorder.IsBlurEnabledProperty,
            new Binding
            {
                Source = border,
                Path = new PropertyPath("(0)", IsBlurEnabledProperty)
            });
    }

}
