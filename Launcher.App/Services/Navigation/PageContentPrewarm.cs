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
using System.Windows.Data;

namespace Launcher.App.Services;

/// <summary>
/// 折叠的页面不会被测量，虚拟化面板要到第一次真正显示才初始化视口，
/// 这笔开销会整段落进第一次切页的动画里。本类型负责让页面在空闲时先完成一次布局。
/// </summary>
internal static class PageContentPrewarm
{
    /// <summary>
    /// 让页面进入可测量状态并返回原有的 Visibility 绑定，供 <see cref="End"/> 装回。
    /// 使用 Hidden 而非 Visible：Hidden 一样会被测量和排布，足以让虚拟化面板初始化视口，
    /// 但绝不会被绘制，即使收尾失败也不会与当前页重叠。
    /// </summary>
    internal static BindingBase? Begin(FrameworkElement page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // 给绑定属性写本地值会直接顶掉绑定表达式，而不是暂时挂起，
        // 因此必须先把绑定取出来，收尾时原样装回去。
        var visibilityBinding = BindingOperations.GetBindingBase(page, UIElement.VisibilityProperty);
        page.Visibility = Visibility.Hidden;
        page.UpdateLayout();
        return visibilityBinding;
    }

    /// <summary>
    /// 装回 Visibility 绑定。返回是否恢复成功——失败意味着页面会永久可见并压住当前页，
    /// 因此失败时直接折叠页面兜底，由调用方记录故障。
    /// </summary>
    internal static bool End(FrameworkElement page, BindingBase? visibilityBinding)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (visibilityBinding is null)
        {
            // 原本就没有绑定：ClearValue 退回默认值即可，但默认值是 Visible，
            // 所以这里必须显式折叠，不能只清本地值。
            page.Visibility = Visibility.Collapsed;
            return true;
        }

        BindingOperations.SetBinding(page, UIElement.VisibilityProperty, visibilityBinding);
        if (BindingOperations.GetBindingExpressionBase(page, UIElement.VisibilityProperty) is not null)
            return true;

        page.Visibility = Visibility.Collapsed;
        return false;
    }
}
