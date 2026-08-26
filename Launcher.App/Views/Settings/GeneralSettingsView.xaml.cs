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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Launcher.App.ViewModels.Settings;

namespace Launcher.App.Views.Settings;

public partial class GeneralSettingsView : UserControl
{
    private bool isRestoringMinecraftDirectorySelection;

    public GeneralSettingsView()
    {
        InitializeComponent();
    }

    private void MinecraftDirectoryListBox_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!IsMinecraftDirectorySelectionBlocked()
            || sender is not ListBox listBox
            || e.OriginalSource is not DependencyObject source
            || FindAncestor<ButtonBase>(source, listBox) is not null)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(source, listBox);
        if (item is not null && !item.IsSelected)
            e.Handled = true;
    }

    private void MinecraftDirectoryListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsMinecraftDirectorySelectionBlocked()
            || sender is not ListBox listBox
            || e.OriginalSource is not DependencyObject source
            || FindAncestor<ButtonBase>(source, listBox) is not null)
        {
            return;
        }

        if (e.Key is Key.Up
            or Key.Down
            or Key.Left
            or Key.Right
            or Key.Home
            or Key.End
            or Key.PageUp
            or Key.PageDown
            or Key.Space)
        {
            e.Handled = true;
        }
    }

    private void MinecraftDirectoryListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (isRestoringMinecraftDirectorySelection
            || !IsMinecraftDirectorySelectionBlocked()
            || sender is not ListBox listBox
            || DataContext is not GeneralSettingsViewModel viewModel
            || ReferenceEquals(listBox.SelectedItem, viewModel.SelectedMinecraftDirectory))
        {
            return;
        }

        isRestoringMinecraftDirectorySelection = true;
        try
        {
            listBox.SelectedItem = viewModel.SelectedMinecraftDirectory;
        }
        finally
        {
            isRestoringMinecraftDirectorySelection = false;
        }
    }

    private bool IsMinecraftDirectorySelectionBlocked() =>
        DataContext is GeneralSettingsViewModel
        {
            IsMinecraftDirectoryChangeBlocked: true
        };

    private static T? FindAncestor<T>(DependencyObject source, DependencyObject stop)
        where T : DependencyObject
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, stop);
             current = GetParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current) =>
        current is Visual or Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
}
