/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Launcher.App.Controls;

/// <summary>
/// Draws a retained, shader-free approximation of a WPF drop shadow.
/// </summary>
internal sealed class CardShadowChrome : FrameworkElement
{
    private const int MaximumBandCount = 24;
    private const double MinimumBandCount = 4d;
    // 位图缓存在软件渲染和 Tier 1 上得不到硬件加速，反而可能比重新绘制更慢。
    private const int MinimumRenderCacheTier = 2;
    // 单个阴影纹理的显存上限。超大表面缓存收益仍在，但会显著抬高显存占用，
    // 此时回退到直接绘制更稳妥。
    private const long MaximumRenderCacheBytes = 8L * 1024L * 1024L;

    internal static readonly DependencyProperty ReferenceEffectProperty =
        DependencyProperty.Register(
            nameof(ReferenceEffect),
            typeof(DropShadowEffect),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnReferenceEffectChanged));

    internal static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty SurfaceBrushProperty =
        DependencyProperty.Register(
            nameof(SurfaceBrush),
            typeof(Brush),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty SurfaceBorderBrushProperty =
        DependencyProperty.Register(
            nameof(SurfaceBorderBrush),
            typeof(Brush),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty OverlayBrushProperty =
        DependencyProperty.Register(
            nameof(OverlayBrush),
            typeof(Brush),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty IsBackdropBlurEnabledProperty =
        DependencyProperty.Register(
            nameof(IsBackdropBlurEnabled),
            typeof(bool),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    internal static readonly DependencyProperty BackdropSourceProperty =
        DependencyProperty.Register(
            nameof(BackdropSource),
            typeof(FrameworkElement),
            typeof(CardShadowChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingPropertyChanged));

    private DrawingGroup? drawing;
    private DropShadowEffect? subscribedEffect;

    internal CardShadowChrome()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <summary>
    /// 阴影绘制在尺寸不变时是静态的，但滚动会让它逐帧重新光栅化多层渐变环。
    /// 提升为位图缓存后滚动只需搬运纹理，视觉输出不变。
    /// 尺寸和 DPI 变化会走到这里重新判定，WPF 自身也会在这些变化时重建缓存内容。
    /// </summary>
    private void ApplyRenderCache()
    {
        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var shouldCache = ShouldUseRenderCache(
            RenderCapability.Tier >> 16,
            ActualWidth,
            ActualHeight,
            dpiScale);
        if (shouldCache == CacheMode is BitmapCache)
            return;

        CacheMode = shouldCache
            ? new BitmapCache
            {
                EnableClearType = false,
                RenderAtScale = 1d,
                SnapsToDevicePixels = true
            }
            : null;
    }

    internal static bool ShouldUseRenderCache(
        int renderingTier,
        double width,
        double height,
        double dpiScale)
    {
        if (renderingTier < MinimumRenderCacheTier)
            return false;
        if (width <= 0d || height <= 0d || dpiScale <= 0d)
            return false;

        var pixelWidth = (long)Math.Ceiling(width * dpiScale);
        var pixelHeight = (long)Math.Ceiling(height * dpiScale);
        if (pixelWidth > MaximumRenderCacheBytes / 4L / Math.Max(pixelHeight, 1L))
            return false;

        return pixelWidth * pixelHeight * 4L <= MaximumRenderCacheBytes;
    }

    internal DropShadowEffect? ReferenceEffect
    {
        get => (DropShadowEffect?)GetValue(ReferenceEffectProperty);
        set => SetValue(ReferenceEffectProperty, value);
    }

    internal CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    internal Brush? SurfaceBrush
    {
        get => (Brush?)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    internal Brush? SurfaceBorderBrush
    {
        get => (Brush?)GetValue(SurfaceBorderBrushProperty);
        set => SetValue(SurfaceBorderBrushProperty, value);
    }

    internal Brush? TintBrush
    {
        get => (Brush?)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    internal Brush? OverlayBrush
    {
        get => (Brush?)GetValue(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    internal bool IsBackdropBlurEnabled
    {
        get => (bool)GetValue(IsBackdropBlurEnabledProperty);
        set => SetValue(IsBackdropBlurEnabledProperty, value);
    }

    internal FrameworkElement? BackdropSource
    {
        get => (FrameworkElement?)GetValue(BackdropSourceProperty);
        set => SetValue(BackdropSourceProperty, value);
    }

    internal int DrawingBuildCount { get; private set; }

    internal int DrawingPrimitiveCount => drawing?.Children.Count ?? 0;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawing ??= BuildDrawing();
        if (drawing is not null)
            drawingContext.DrawDrawing(drawing);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateDrawing();
        ApplyRenderCache();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateDrawing();
        ApplyRenderCache();
    }

    private DrawingGroup? BuildDrawing()
    {
        DrawingBuildCount++;

        var effect = ReferenceEffect;
        if (effect is null || ActualWidth <= 0d || ActualHeight <= 0d || effect.Opacity <= 0d)
            return null;

        var surfaceAlpha = IsBackdropBlurEnabled && BackdropSource is not null
            ? 1d
            : ResolveBrushAlpha(SurfaceBrush);
        surfaceAlpha = CompositeAlpha(surfaceAlpha, ResolveBrushAlpha(TintBrush));
        surfaceAlpha = CompositeAlpha(surfaceAlpha, ResolveBrushAlpha(OverlayBrush));
        var borderAlpha = ResolveBrushAlpha(SurfaceBorderBrush);
        var sourceAlpha = CompositeAlpha(surfaceAlpha, borderAlpha);
        var opacity = Math.Clamp(effect.Opacity * (effect.Color.A / 255d) * sourceAlpha, 0d, 1d);
        if (opacity <= 0d)
            return null;

        var directionRadians = effect.Direction * Math.PI / 180d;
        var offsetX = effect.ShadowDepth * Math.Cos(directionRadians);
        var offsetY = -effect.ShadowDepth * Math.Sin(directionRadians);
        var baseRect = new Rect(offsetX, offsetY, ActualWidth, ActualHeight);
        var cornerRadius = ResolveUniformCornerRadius(CornerRadius);
        var group = new DrawingGroup();

        AddGeometryDrawing(
            group,
            CreateRoundedRectangleGeometry(baseRect, cornerRadius),
            CreateFrozenBrush(effect.Color, opacity));

        var blurRadius = Math.Max(0d, effect.BlurRadius);
        if (blurRadius > 0d)
        {
            var bandCount = Math.Clamp(
                (int)Math.Ceiling(blurRadius),
                (int)MinimumBandCount,
                MaximumBandCount);
            var bandWidth = blurRadius / bandCount;
            var sigma = Math.Max(blurRadius / 3d, 0.01d);

            for (var index = 0; index < bandCount; index++)
            {
                var innerExpansion = index * bandWidth;
                var outerExpansion = (index + 1) * bandWidth;
                var sampleDistance = (innerExpansion + outerExpansion) / 2d;
                // A blurred solid edge follows the tail of a normal distribution,
                // not the normal density itself. Using the density made the first
                // exterior band almost fully opaque and visually about twice as
                // heavy as WPF's DropShadowEffect at the card boundary.
                var bandOpacity = opacity * EvaluateNormalTail(sampleDistance / sigma);
                if (bandOpacity < 1d / 1024d)
                    continue;

                var outerGeometry = CreateRoundedRectangleGeometry(
                    Inflate(baseRect, outerExpansion),
                    cornerRadius + outerExpansion);
                var innerGeometry = CreateRoundedRectangleGeometry(
                    Inflate(baseRect, innerExpansion),
                    cornerRadius + innerExpansion);
                var ringGeometry = Geometry.Combine(
                    outerGeometry,
                    innerGeometry,
                    GeometryCombineMode.Exclude,
                    null);
                if (ringGeometry.CanFreeze)
                    ringGeometry.Freeze();

                AddGeometryDrawing(
                    group,
                    ringGeometry,
                    CreateFrozenBrush(effect.Color, bandOpacity));
            }
        }

        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private static void AddGeometryDrawing(DrawingGroup group, Geometry geometry, Brush brush)
    {
        var geometryDrawing = new GeometryDrawing(brush, null, geometry);
        if (geometryDrawing.CanFreeze)
            geometryDrawing.Freeze();
        group.Children.Add(geometryDrawing);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp((int)Math.Round(opacity * 255d), 0, 255),
            color.R,
            color.G,
            color.B));
        brush.Freeze();
        return brush;
    }

    private static RectangleGeometry CreateRoundedRectangleGeometry(Rect rect, double radius)
    {
        var geometry = new RectangleGeometry(rect, radius, radius);
        if (geometry.CanFreeze)
            geometry.Freeze();
        return geometry;
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        rect.Inflate(amount, amount);
        return rect;
    }

    private static double ResolveUniformCornerRadius(CornerRadius radius)
    {
        return Math.Max(0d, Math.Max(
            Math.Max(radius.TopLeft, radius.TopRight),
            Math.Max(radius.BottomRight, radius.BottomLeft)));
    }

    private static double ResolveBrushAlpha(Brush? brush)
    {
        if (brush is null)
            return 0d;

        var colorAlpha = brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.A / 255d
            : 1d;
        return Math.Clamp(colorAlpha * brush.Opacity, 0d, 1d);
    }

    private static double CompositeAlpha(double backgroundAlpha, double foregroundAlpha) =>
        1d - ((1d - backgroundAlpha) * (1d - foregroundAlpha));

    private static double EvaluateNormalTail(double value)
    {
        // Abramowitz-Stegun 26.2.17. The shadow samples only non-negative
        // distances, where this approximation is stable and sufficiently close
        // to the Gaussian convolution used by WPF.
        var t = 1d / (1d + (0.2316419d * Math.Max(0d, value)));
        var polynomial = t * (0.319381530d + (t * (-0.356563782d + (t *
            (1.781477937d + (t * (-1.821255978d + (t * 1.330274429d))))))));
        var density = 0.3989422804014327d * Math.Exp(-0.5d * value * value);
        return Math.Clamp(density * polynomial, 0d, 0.5d);
    }

    private static void OnReferenceEffectChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chrome = (CardShadowChrome)dependencyObject;
        chrome.ReplaceEffectSubscription(args.NewValue as DropShadowEffect);
        chrome.InvalidateDrawing();
    }

    private static void OnDrawingPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((CardShadowChrome)dependencyObject).InvalidateDrawing();
    }

    private void ReplaceEffectSubscription(DropShadowEffect? effect)
    {
        if (subscribedEffect is { IsFrozen: false })
            subscribedEffect.Changed -= ReferenceEffect_Changed;

        subscribedEffect = effect;
        // Theme resources are normally frozen by WPF. Adding or removing an event
        // handler mutates a Freezable and throws for those shared instances. A
        // DynamicResource replacement still reaches OnReferenceEffectChanged, so
        // only mutable effects need their in-place changes observed here.
        if (subscribedEffect is { IsFrozen: false })
            subscribedEffect.Changed += ReferenceEffect_Changed;
    }

    private void ReferenceEffect_Changed(object? sender, EventArgs e) => InvalidateDrawing();

    private void InvalidateDrawing()
    {
        drawing = null;
        InvalidateVisual();
    }
}
