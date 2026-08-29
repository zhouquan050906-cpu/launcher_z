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

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Launcher.App.Converters;

/// <summary>
/// 把一个 <see cref="System.Windows.Controls.Border"/> 的圆角半径换算成它内部填充层应该用的半径。
/// </summary>
/// <remarks>
/// WPF 的 Border 把边框画在中心线上：外沿半径是 <c>R + t/2</c>，内沿是 <c>R − t/2</c>，
/// 而 Child 被排布在扣掉整个 <c>BorderThickness</c> 之后的矩形里。所以一个填满内区的子层
/// 想和边框内沿同心，半径必须是 <c>R − t/2</c>，直接沿用 <c>R</c> 会在四个圆角上缩进去
/// 约 <c>t/2 × (1 − 1/√2)</c>，留下一条亚像素的缝——直边上两条弧完全重合，看不出来，
/// 只有圆角处会露出底色，表现为圆角发暗、发糊。
///
/// <see cref="CornerRadius"/> 每个角只有一个值，而 WPF 内部每个角是两个半径（各对应一条邻边）。
/// 这里取两条邻边半厚度的平均：厚度均匀时完全等价，不均匀时是一个合理的近似。
/// </remarks>
public sealed class InnerCornerRadiusConverter : IMultiValueConverter
{
    public static InnerCornerRadiusConverter Instance { get; } = new();

    /// <summary>
    /// 内缩：得到边框内沿的半径，供填在边框内部的层使用。
    /// </summary>
    public static CornerRadius Deflate(CornerRadius radius, Thickness border)
    {
        return new CornerRadius(
            Inset(radius.TopLeft, border.Left, border.Top),
            Inset(radius.TopRight, border.Top, border.Right),
            Inset(radius.BottomRight, border.Right, border.Bottom),
            Inset(radius.BottomLeft, border.Bottom, border.Left));
    }

    /// <summary>
    /// 外扩：得到边框外沿的半径，供覆盖整个 Border 外框的层（例如阴影）使用。
    /// </summary>
    public static CornerRadius Inflate(CornerRadius radius, Thickness border)
    {
        return new CornerRadius(
            Outset(radius.TopLeft, border.Left, border.Top),
            Outset(radius.TopRight, border.Top, border.Right),
            Outset(radius.BottomRight, border.Right, border.Bottom),
            Outset(radius.BottomLeft, border.Bottom, border.Left));
    }

    private static double Inset(double radius, double first, double second)
    {
        return Math.Max(0d, radius - HalfAverage(first, second));
    }

    private static double Outset(double radius, double first, double second)
    {
        // 厚度为 0 的边不产生外沿，WPF 在这种情况下让外沿直接落在矩形上。
        var offset = HalfAverage(first, second);
        return offset <= 0d ? radius : radius + offset;
    }

    private static double HalfAverage(double first, double second)
    {
        var left = double.IsFinite(first) ? Math.Max(0d, first) : 0d;
        var right = double.IsFinite(second) ? Math.Max(0d, second) : 0d;
        return (left + right) / 4d;
    }

    /// <summary>
    /// values = [CornerRadius, Thickness]。targetType 是 <see cref="double"/> 时返回左上角的半径，
    /// 方便喂给只接受单个半径的属性（例如 <c>RoundedClip.Radius</c>）。
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [CornerRadius radius, Thickness border])
            return DependencyProperty.UnsetValue;

        var inflate = parameter is string text
            && string.Equals(text, "Outer", StringComparison.OrdinalIgnoreCase);
        var result = inflate ? Inflate(radius, border) : Deflate(radius, border);

        return targetType == typeof(double) ? result.TopLeft : result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
