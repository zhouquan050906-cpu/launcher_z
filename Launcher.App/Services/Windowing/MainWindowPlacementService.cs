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
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.Services;

internal readonly record struct MainWindowPlacementSnapshot(
    double Width,
    double Height,
    bool WasMaximized);

public sealed class MainWindowPlacementService(ISettingsService settingsService)
{
    internal void Restore(Window window, LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);
        Restore(window, settings, SystemParameters.WorkArea);
    }

    internal static void Restore(Window window, LauncherSettings settings, Rect workArea)
    {
        window.Width = NormalizeRestoredDimension(
            settings.MainWindowWidth,
            window.Width,
            window.MinWidth,
            workArea.Width);
        window.Height = NormalizeRestoredDimension(
            settings.MainWindowHeight,
            window.Height,
            window.MinHeight,
            workArea.Height);
        window.WindowState = settings.MainWindowWasMaximized
            ? WindowState.Maximized
            : WindowState.Normal;
    }

    internal MainWindowPlacementSnapshot Capture(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return CreateSnapshot(
            window.WindowState,
            new Size(window.ActualWidth, window.ActualHeight),
            window.RestoreBounds,
            new Size(window.Width, window.Height));
    }

    internal async Task SaveAsync(
        MainWindowPlacementSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(
                settings =>
                {
                    settings.MainWindowWidth = snapshot.Width;
                    settings.MainWindowHeight = snapshot.Height;
                    settings.MainWindowWasMaximized = snapshot.WasMaximized;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static MainWindowPlacementSnapshot CreateSnapshot(
        WindowState windowState,
        Size actualSize,
        Rect restoreBounds,
        Size fallbackSize)
    {
        var width = windowState is WindowState.Normal
            ? actualSize.Width
            : restoreBounds.Width;
        var height = windowState is WindowState.Normal
            ? actualSize.Height
            : restoreBounds.Height;

        return new MainWindowPlacementSnapshot(
            NormalizeCapturedDimension(width, fallbackSize.Width, LauncherDefaults.DefaultMainWindowWidth),
            NormalizeCapturedDimension(height, fallbackSize.Height, LauncherDefaults.DefaultMainWindowHeight),
            windowState is WindowState.Maximized);
    }

    private static double NormalizeRestoredDimension(
        double value,
        double fallbackValue,
        double minimumValue,
        double availableValue)
    {
        var normalizedMinimum = IsValidDimension(minimumValue) ? minimumValue : 0d;
        var normalizedFallback = IsValidDimension(fallbackValue)
            ? Math.Max(fallbackValue, normalizedMinimum)
            : normalizedMinimum;
        var normalizedValue = IsValidDimension(value) ? value : normalizedFallback;
        var maximumValue = IsValidDimension(availableValue)
            ? Math.Max(availableValue, normalizedMinimum)
            : double.PositiveInfinity;
        return Math.Clamp(normalizedValue, normalizedMinimum, maximumValue);
    }

    private static double NormalizeCapturedDimension(double value, double fallbackValue, double defaultValue)
    {
        if (IsValidDimension(value))
            return value;

        return IsValidDimension(fallbackValue) ? fallbackValue : defaultValue;
    }

    private static bool IsValidDimension(double value) => double.IsFinite(value) && value > 0d;
}
