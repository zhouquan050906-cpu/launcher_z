/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Launcher.App.Controls.Account;

internal static class CarouselAnimationLifecycle
{
    internal static void CompleteAndRemoveClocks(
        ScaleTransform3D scale,
        TranslateTransform3D translate,
        double targetX,
        double targetScale)
    {
        ApplyBaseValues(scale, translate, targetX, targetScale);
        RemoveClocks(scale, translate);
    }

    internal static (double X, double Scale) CaptureCurrentAndRemoveClocks(
        ScaleTransform3D scale,
        TranslateTransform3D translate)
    {
        var currentX = translate.OffsetX;
        var currentScale = scale.ScaleX;
        ApplyBaseValues(scale, translate, currentX, currentScale);
        RemoveClocks(scale, translate);
        return (currentX, currentScale);
    }

    private static void ApplyBaseValues(
        ScaleTransform3D scale,
        TranslateTransform3D translate,
        double x,
        double scaleValue)
    {
        translate.OffsetX = x;
        scale.ScaleX = scaleValue;
        scale.ScaleY = scaleValue;
        scale.ScaleZ = scaleValue;
    }

    private static void RemoveClocks(ScaleTransform3D scale, TranslateTransform3D translate)
    {
        translate.BeginAnimation(TranslateTransform3D.OffsetXProperty, null);
        scale.BeginAnimation(ScaleTransform3D.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform3D.ScaleYProperty, null);
        scale.BeginAnimation(ScaleTransform3D.ScaleZProperty, null);
    }
}
