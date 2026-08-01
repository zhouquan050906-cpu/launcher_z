/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Launcher.App.Controls;

namespace Launcher.App.Services;

/// <summary>
/// 协调双层内容页的滑动切换以及随页面显示的浮动元素淡入淡出。
/// </summary>
public sealed class SlidingContentTransitionCoordinator
{
    // token 标识当前过渡代次，旧动画结束回调不得修改新页面的可见性。
    private static readonly TimeSpan StepTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan FloatingElementFadeDuration = TimeSpan.FromMilliseconds(180);
    private const double DefaultTransitionScale = 0.985;

    private readonly FrameworkElement loadedElement;
    private readonly FrameworkElement contentHost;
    private readonly FrameworkElement primaryLayer;
    private readonly FrameworkElement secondaryLayer;
    private readonly IReadOnlyList<FrameworkElement> secondaryFloatingElements;
    private readonly bool useSlideTransition;
    private readonly bool useScaleTransition;
    private readonly double transitionScale;
    private bool isSecondaryLayerVisible;
    private int transitionToken;
    private IDisposable? blurRefreshLease;

    public SlidingContentTransitionCoordinator(
        FrameworkElement loadedElement,
        FrameworkElement contentHost,
        FrameworkElement primaryLayer,
        FrameworkElement secondaryLayer,
        IEnumerable<FrameworkElement>? secondaryFloatingElements = null,
        bool useSlideTransition = true,
        bool useScaleTransition = false,
        double transitionScale = DefaultTransitionScale)
    {
        this.loadedElement = loadedElement;
        this.contentHost = contentHost;
        this.primaryLayer = primaryLayer;
        this.secondaryLayer = secondaryLayer;
        this.secondaryFloatingElements = secondaryFloatingElements?.ToArray() ?? [];
        this.useSlideTransition = useSlideTransition;
        this.useScaleTransition = useScaleTransition;
        this.transitionScale = transitionScale;
    }

    public void Sync(bool showSecondaryLayer)
    {
        ReleaseBlurRefreshLease();
        // Sync 用于初始状态或禁用动画场景，先停止全部动画再直接设置稳定终值。
        transitionToken++;
        isSecondaryLayerVisible = showSecondaryLayer;

        ResetLayer(primaryLayer, isVisible: !showSecondaryLayer);
        ResetLayer(secondaryLayer, isVisible: showSecondaryLayer);
        SyncFloatingElements(showSecondaryLayer);
    }

    public void AnimateTo(bool showSecondaryLayer)
    {
        // 两层同时移动但方向相反，目标层在动画开始前可见，离开层在完成后折叠。
        if (isSecondaryLayerVisible == showSecondaryLayer)
        {
            Sync(showSecondaryLayer);
            return;
        }

        if (!loadedElement.IsLoaded || (useSlideTransition && contentHost.ActualWidth <= 0))
        {
            Sync(showSecondaryLayer);
            return;
        }

        var previousLayer = isSecondaryLayerVisible ? secondaryLayer : primaryLayer;
        var nextLayer = showSecondaryLayer ? secondaryLayer : primaryLayer;
        var direction = showSecondaryLayer ? 1 : -1;
        var width = Math.Max(contentHost.ActualWidth, 1);
        var token = ++transitionToken;
        isSecondaryLayerVisible = showSecondaryLayer;
        ReleaseBlurRefreshLease();
        blurRefreshLease = BackdropBlurRefreshCoordinator.BeginContinuousRefresh(previousLayer, nextLayer);

        var previousTransforms = EnsureLayerTransforms(previousLayer);
        var nextTransforms = EnsureLayerTransforms(nextLayer);

        previousLayer.Visibility = Visibility.Visible;
        previousLayer.Opacity = 1;
        previousTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        previousTransforms.Translate.X = 0;
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        previousTransforms.Scale.ScaleX = 1;
        previousTransforms.Scale.ScaleY = 1;

        nextLayer.Visibility = Visibility.Visible;
        nextLayer.Opacity = 0;
        nextTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        nextTransforms.Translate.X = useSlideTransition ? width * direction : 0;
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        nextTransforms.Scale.ScaleX = useScaleTransition ? transitionScale : 1;
        nextTransforms.Scale.ScaleY = useScaleTransition ? transitionScale : 1;

        AnimateFloatingElements(showSecondaryLayer, token);

        var previousSlide = CreateTransitionAnimation(0, useSlideTransition ? -width * direction : 0);
        var nextSlide = CreateTransitionAnimation(useSlideTransition ? width * direction : 0, 0);
        var previousFade = CreateTransitionAnimation(1, 0);
        var nextFade = CreateTransitionAnimation(0, 1);
        var previousScale = CreateTransitionAnimation(1, useScaleTransition ? transitionScale : 1);
        var nextScale = CreateTransitionAnimation(useScaleTransition ? transitionScale : 1, 1);

        var completionAnimation = useSlideTransition ? nextSlide : nextFade;
        completionAnimation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            ResetLayer(previousLayer, isVisible: false);
            ResetLayer(nextLayer, isVisible: true);
            ReleaseBlurRefreshLease();
        };

        previousLayer.BeginAnimation(UIElement.OpacityProperty, previousFade, HandoffBehavior.SnapshotAndReplace);
        previousTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, previousSlide, HandoffBehavior.SnapshotAndReplace);
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, previousScale, HandoffBehavior.SnapshotAndReplace);
        previousTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, previousScale.Clone(), HandoffBehavior.SnapshotAndReplace);
        nextLayer.BeginAnimation(UIElement.OpacityProperty, nextFade, HandoffBehavior.SnapshotAndReplace);
        nextTransforms.Translate.BeginAnimation(TranslateTransform.XProperty, nextSlide, HandoffBehavior.SnapshotAndReplace);
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, nextScale, HandoffBehavior.SnapshotAndReplace);
        nextTransforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, nextScale.Clone(), HandoffBehavior.SnapshotAndReplace);
    }

    private void ReleaseBlurRefreshLease()
    {
        blurRefreshLease?.Dispose();
        blurRefreshLease = null;
    }

    private void SyncFloatingElements(bool showSecondaryLayer)
    {
        foreach (var element in secondaryFloatingElements)
            ResetFloatingElement(element, showSecondaryLayer);
    }

    private void ResetLayer(FrameworkElement layer, bool isVisible)
    {
        layer.BeginAnimation(UIElement.OpacityProperty, null);
        var transforms = EnsureLayerTransforms(layer);
        transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
        transforms.Translate.X = 0;
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        transforms.Scale.ScaleX = 1;
        transforms.Scale.ScaleY = 1;
        layer.Opacity = isVisible ? 1 : 0;
        layer.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ResetFloatingElement(FrameworkElement element, bool isVisible)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = isVisible ? 1 : 0;
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        element.IsHitTestVisible = isVisible;
    }

    private void AnimateFloatingElements(bool showSecondaryLayer, int token)
    {
        // 浮动元素时长独立于页面滑动，使操作按钮更快响应但仍与目标层一致。
        foreach (var element in secondaryFloatingElements)
        {
            if (showSecondaryLayer)
                FadeFloatingElementIn(element, token);
            else
                FadeFloatingElementOut(element, token);
        }
    }

    private void FadeFloatingElementIn(FrameworkElement element, int token)
    {
        // 开始前清除旧 AnimationClock，并用 token 防止旧 Completed 覆盖新 Opacity。
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;

        var animation = CreateFloatingElementFadeAnimation(element.Opacity, 1);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.Visibility = Visibility.Visible;
            element.IsHitTestVisible = true;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void FadeFloatingElementOut(FrameworkElement element, int token)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.IsHitTestVisible = false;

        var animation = CreateFloatingElementFadeAnimation(element.Opacity, 0);
        animation.Completed += (_, _) =>
        {
            if (token != transitionToken)
                return;

            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0;
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateFloatingElementFadeAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, FloatingElementFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private static DoubleAnimation CreateTransitionAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, StepTransitionDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
    }

    private LayerTransforms EnsureLayerTransforms(FrameworkElement layer)
    {
        // 在保留模板已有 Transform 的前提下追加 Scale/Translate，并缓存本协调器创建的对象。
        if (useScaleTransition
            && Equals(layer.ReadLocalValue(UIElement.RenderTransformOriginProperty), DependencyProperty.UnsetValue))
        {
            layer.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        if (layer.RenderTransform is TransformGroup group)
        {
            var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (scale is null)
            {
                scale = new ScaleTransform();
                group.Children.Insert(0, scale);
            }

            var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (translate is null)
            {
                translate = new TranslateTransform();
                group.Children.Add(translate);
            }

            return new LayerTransforms(scale, translate);
        }

        if (layer.RenderTransform is TranslateTransform translateTransform)
        {
            var scale = new ScaleTransform();
            var groupWithTranslate = new TransformGroup();
            groupWithTranslate.Children.Add(scale);
            groupWithTranslate.Children.Add(translateTransform);
            layer.RenderTransform = groupWithTranslate;
            return new LayerTransforms(scale, translateTransform);
        }

        if (layer.RenderTransform is ScaleTransform scaleTransform)
        {
            var translate = new TranslateTransform();
            var groupWithScale = new TransformGroup();
            groupWithScale.Children.Add(scaleTransform);
            groupWithScale.Children.Add(translate);
            layer.RenderTransform = groupWithScale;
            return new LayerTransforms(scaleTransform, translate);
        }

        var scaleOnly = new ScaleTransform();
        var translateOnly = new TranslateTransform();
        var transformGroup = new TransformGroup();
        if (layer.RenderTransform is not null && layer.RenderTransform != Transform.Identity)
            transformGroup.Children.Add(layer.RenderTransform);

        transformGroup.Children.Add(scaleOnly);
        transformGroup.Children.Add(translateOnly);
        layer.RenderTransform = transformGroup;
        return new LayerTransforms(scaleOnly, translateOnly);
    }

    private sealed record LayerTransforms(ScaleTransform Scale, TranslateTransform Translate);
}
