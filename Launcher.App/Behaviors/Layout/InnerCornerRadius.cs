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
using System.Windows.Controls;
using System.Windows.Data;
using Launcher.App.Converters;

namespace Launcher.App.Behaviors;

/// <summary>
/// 让一个填在带边框 Border 内部的层，自动使用边框内沿的圆角半径。
/// </summary>
/// <remarks>
/// 用法：<c>behaviors:InnerCornerRadius.Source="{Binding ElementName=Root}"</c>。
/// 直接照抄外层的 CornerRadius 会在四个圆角上留下一条亚像素的缝，成因见
/// <see cref="InnerCornerRadiusConverter"/>。外层 BorderThickness 为 0 时结果与外层相同，
/// 所以套在任何层上都是安全的。
/// </remarks>
public static class InnerCornerRadius
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(Border),
            typeof(InnerCornerRadius),
            new PropertyMetadata(null, OnSourceChanged));

    public static Border? GetSource(DependencyObject element) =>
        (Border?)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, Border? value) =>
        element.SetValue(SourceProperty, value);

    private static void OnSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Border target)
            return;

        if (e.NewValue is not Border source)
        {
            BindingOperations.ClearBinding(target, Border.CornerRadiusProperty);
            return;
        }

        var binding = new MultiBinding { Converter = InnerCornerRadiusConverter.Instance };
        binding.Bindings.Add(new Binding(nameof(Border.CornerRadius)) { Source = source });
        binding.Bindings.Add(new Binding(nameof(Border.BorderThickness)) { Source = source });
        BindingOperations.SetBinding(target, Border.CornerRadiusProperty, binding);
    }
}
