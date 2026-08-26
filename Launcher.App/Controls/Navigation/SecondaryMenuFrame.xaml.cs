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

namespace Launcher.App.Controls;

public partial class SecondaryMenuFrame : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SecondaryMenuFrame), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MenuContentProperty =
        DependencyProperty.Register(
            nameof(MenuContent),
            typeof(object),
            typeof(SecondaryMenuFrame),
            new PropertyMetadata(null, OnContentHostPropertyChanged));

    public static readonly DependencyProperty UseInternalScrollViewerProperty =
        DependencyProperty.Register(
            nameof(UseInternalScrollViewer),
            typeof(bool),
            typeof(SecondaryMenuFrame),
            new PropertyMetadata(true, OnContentHostPropertyChanged));

    public static readonly DependencyProperty FooterContentProperty =
        DependencyProperty.Register(
            nameof(FooterContent),
            typeof(object),
            typeof(SecondaryMenuFrame),
            new PropertyMetadata(null));

    public SecondaryMenuFrame()
    {
        InitializeComponent();
        UpdateContentHost();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public bool UseInternalScrollViewer
    {
        get => (bool)GetValue(UseInternalScrollViewerProperty);
        set => SetValue(UseInternalScrollViewerProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    private static void OnContentHostPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SecondaryMenuFrame)d).UpdateContentHost();
    }

    private void UpdateContentHost()
    {
        if (ScrollableContentHost is null || DirectContentHost is null)
            return;

        if (UseInternalScrollViewer)
        {
            DirectContentHost.Content = null;
            ScrollableContentHost.Content = MenuContent;
        }
        else
        {
            ScrollableContentHost.Content = null;
            DirectContentHost.Content = MenuContent;
        }
    }
}
