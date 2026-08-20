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
using System.Windows.Media.Effects;
using Launcher.App.Controls;

namespace Launcher.App.Diagnostics;

internal readonly record struct VisualTreeSize(
    int ElementCount,
    int MaxDepth,
    bool IsTruncated,
    int EffectCount,
    int VisibleEffectCount,
    int DropShadowCount,
    int CardShadowCount,
    string EffectBreakdown,
    string VisibleEffectHosts);

/// <summary>
/// 统计一棵视觉树的实际规模。XAML 标签数会严重低估运行时元素数量，
/// 因为 GroupBox、ComboBox、TextBox 等控件的模板会展开成多层子树。
/// </summary>
internal static class VisualTreeStats
{
    // 上限防止异常大的树把诊断本身变成性能问题。
    internal const int MaximumElements = 20000;

    internal static VisualTreeSize Measure(DependencyObject? root)
    {
        if (root is null)
            return new VisualTreeSize(0, 0, false, 0, 0, 0, 0, string.Empty, string.Empty);

        var count = 0;
        var maxDepth = 0;
        var truncated = false;
        var effectCount = 0;
        var visibleEffectCount = 0;
        var dropShadowCount = 0;
        var cardShadowCount = 0;
        // 按具体 Effect 类型分组，才能区分阴影、模糊和自定义着色器各占多少。
        var effectTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        var visibleHosts = new List<string>();
        var stack = new Stack<(DependencyObject Node, int Depth)>();
        stack.Push((root, 1));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            count++;
            // 区分两类阴影：Effect 走离屏渲染 + 着色器，自绘 Chrome 只是普通绘制指令。
            if (node is UIElement { Effect: { } nodeEffect } effectHost)
            {
                effectCount++;
                if (nodeEffect is DropShadowEffect)
                    dropShadowCount++;

                // 折叠的元素不参与渲染，其 Effect 不产生离屏表面；
                // 只有可见的才是真实成本，必须分开计数。
                var isRendered = effectHost.IsVisible;
                if (isRendered)
                {
                    visibleEffectCount++;
                    // 报出宿主身份，才能把成本对应到具体控件而不是只有一个数字。
                    var hostName = effectHost is FrameworkElement { Name.Length: > 0 } named
                        ? $"{effectHost.GetType().Name}#{named.Name}"
                        : effectHost.GetType().Name;
                    visibleHosts.Add($"{hostName}({nodeEffect.GetType().Name})");
                }

                var typeName = nodeEffect.GetType().Name + (isRendered ? "" : "(hidden)");
                effectTypes[typeName] = effectTypes.GetValueOrDefault(typeName) + 1;
            }

            if (node is CardShadowChrome)
                cardShadowCount++;
            if (depth > maxDepth)
                maxDepth = depth;
            if (count >= MaximumElements)
            {
                truncated = true;
                break;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < childCount; index++)
                stack.Push((VisualTreeHelper.GetChild(node, index), depth + 1));
        }

        var breakdown = string.Join(
            ",",
            effectTypes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}:{pair.Value}"));
        return new VisualTreeSize(
            count,
            maxDepth,
            truncated,
            effectCount,
            visibleEffectCount,
            dropShadowCount,
            cardShadowCount,
            breakdown,
            string.Join(",", visibleHosts.Take(12)));
    }
}
