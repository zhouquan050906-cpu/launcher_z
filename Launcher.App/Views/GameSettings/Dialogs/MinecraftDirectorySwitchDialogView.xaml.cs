/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Launcher.App.ViewModels.Settings;

namespace Launcher.App.Views.GameSettings.Dialogs;

public partial class MinecraftDirectorySwitchDialogView : UserControl
{
    private bool isRestoringSelection;

    public MinecraftDirectorySwitchDialogView()
    {
        InitializeComponent();
    }

    private void DirectoryListBox_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!IsSelectionBlocked()
            || sender is not ListBox listBox
            || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>(source, listBox);
        if (item is not null && !item.IsSelected)
            e.Handled = true;
    }

    private void DirectoryListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsSelectionBlocked())
            return;

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

    private void DirectoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRestoringSelection
            || !IsSelectionBlocked()
            || sender is not ListBox listBox
            || DataContext is not MinecraftDirectorySwitchDialogViewModel viewModel
            || ReferenceEquals(listBox.SelectedItem, viewModel.SelectedDirectory))
        {
            return;
        }

        isRestoringSelection = true;
        try
        {
            listBox.SelectedItem = viewModel.SelectedDirectory;
        }
        finally
        {
            isRestoringSelection = false;
        }
    }

    private bool IsSelectionBlocked() =>
        DataContext is MinecraftDirectorySwitchDialogViewModel
        {
            IsChangeBlockedByActiveTasks: true
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
