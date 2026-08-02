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

namespace Launcher.App.Behaviors;

/// <summary>
/// Prevents a dynamic content host from scrolling its parent when focus falls back to the host.
/// Descendant bring-into-view requests continue through the normal routed event path.
/// </summary>
public static class SelfBringIntoViewSuppression
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SelfBringIntoViewSuppression),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.RequestBringIntoView += Element_OnRequestBringIntoView;
            return;
        }

        element.RequestBringIntoView -= Element_OnRequestBringIntoView;
    }

    private static void Element_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender))
            e.Handled = true;
    }
}
