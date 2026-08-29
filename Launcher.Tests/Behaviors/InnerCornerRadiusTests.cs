/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Launcher.App.Behaviors;
using Launcher.App.Converters;

namespace Launcher.Tests.Behaviors;

public sealed class InnerCornerRadiusTests
{
    // WPF 的 Border 把边框画在中心线上，内沿半径是 R - t/2。填在边框内部的层必须用这个值，
    // 直接沿用 R 会在圆角处缩进去，露出一条亚像素的缝。
    [Theory]
    [InlineData(10d, 1d, 9.5d)]
    [InlineData(8d, 1d, 7.5d)]
    [InlineData(17d, 1d, 16.5d)]
    [InlineData(16d, 2d, 15d)]
    public void DeflateSubtractsHalfTheBorderThickness(double radius, double thickness, double expected)
    {
        var result = InnerCornerRadiusConverter.Deflate(
            new CornerRadius(radius),
            new Thickness(thickness));

        Assert.Equal(expected, result.TopLeft);
        Assert.Equal(expected, result.TopRight);
        Assert.Equal(expected, result.BottomRight);
        Assert.Equal(expected, result.BottomLeft);
    }

    [Fact]
    public void DeflateLeavesRadiusUnchangedWhenThereIsNoBorder()
    {
        var result = InnerCornerRadiusConverter.Deflate(new CornerRadius(12d), default);

        Assert.Equal(12d, result.TopLeft);
    }

    [Fact]
    public void DeflateNeverGoesNegative()
    {
        var result = InnerCornerRadiusConverter.Deflate(new CornerRadius(1d), new Thickness(8d));

        Assert.Equal(0d, result.TopLeft);
    }

    [Fact]
    public void DeflateUsesTheTwoAdjacentSidesForEachCorner()
    {
        // left=2 top=4 right=0 bottom=6 -> 每个角取相邻两条边半厚度的平均。
        var result = InnerCornerRadiusConverter.Deflate(
            new CornerRadius(10d),
            new Thickness(2d, 4d, 0d, 6d));

        Assert.Equal(10d - ((2d + 4d) / 4d), result.TopLeft);
        Assert.Equal(10d - ((4d + 0d) / 4d), result.TopRight);
        Assert.Equal(10d - ((0d + 6d) / 4d), result.BottomRight);
        Assert.Equal(10d - ((6d + 2d) / 4d), result.BottomLeft);
    }

    [Fact]
    public void InflateAddsHalfTheBorderThickness()
    {
        var result = InnerCornerRadiusConverter.Inflate(
            new CornerRadius(10d),
            new Thickness(1d));

        Assert.Equal(10.5d, result.TopLeft);
    }

    [Fact]
    public void InflateLeavesRadiusUnchangedWhenThereIsNoBorder()
    {
        var result = InnerCornerRadiusConverter.Inflate(new CornerRadius(10d), default);

        Assert.Equal(10d, result.TopLeft);
    }

    [Fact]
    public void ConverterReturnsTopLeftWhenTheTargetIsADouble()
    {
        var converted = InnerCornerRadiusConverter.Instance.Convert(
            [new CornerRadius(10d), new Thickness(1d)],
            typeof(double),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal(9.5d, Assert.IsType<double>(converted));
    }

    [Fact]
    public void ConverterHonoursTheOuterParameter()
    {
        var converted = InnerCornerRadiusConverter.Instance.Convert(
            [new CornerRadius(10d), new Thickness(1d)],
            typeof(CornerRadius),
            "Outer",
            CultureInfo.InvariantCulture);

        Assert.Equal(10.5d, Assert.IsType<CornerRadius>(converted).TopLeft);
    }

    [Fact]
    public void ConverterIsUnsetForUnexpectedInput()
    {
        var converted = InnerCornerRadiusConverter.Instance.Convert(
            [DependencyProperty.UnsetValue, DependencyProperty.UnsetValue],
            typeof(CornerRadius),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Same(DependencyProperty.UnsetValue, converted);
    }

    [Fact]
    public void AttachedSourceTracksLaterChangesToTheOuterBorder()
    {
        RunOnStaThread(() =>
        {
            var source = new Border
            {
                CornerRadius = new CornerRadius(10d),
                BorderThickness = new Thickness(1d)
            };
            var target = new Border();

            InnerCornerRadius.SetSource(target, source);

            Assert.Equal(9.5d, target.CornerRadius.TopLeft);

            // 半径和厚度都要能跟着变，否则改主题圆角时子层又会错位。
            source.CornerRadius = new CornerRadius(20d);
            Assert.Equal(19.5d, target.CornerRadius.TopLeft);

            source.BorderThickness = new Thickness(4d);
            Assert.Equal(18d, target.CornerRadius.TopLeft);
        });
    }

    [Fact]
    public void ClearingTheAttachedSourceRemovesTheBinding()
    {
        RunOnStaThread(() =>
        {
            var source = new Border
            {
                CornerRadius = new CornerRadius(10d),
                BorderThickness = new Thickness(1d)
            };
            var target = new Border();

            InnerCornerRadius.SetSource(target, source);
            Assert.NotNull(BindingOperations.GetMultiBindingExpression(target, Border.CornerRadiusProperty));

            InnerCornerRadius.SetSource(target, null);
            Assert.Null(BindingOperations.GetMultiBindingExpression(target, Border.CornerRadiusProperty));
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
