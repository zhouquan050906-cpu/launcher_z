/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalContentIconChangedEventArgs(
    string fullPath,
    string iconSource) : EventArgs
{
    public string FullPath { get; } = fullPath;

    public string IconSource { get; } = iconSource;
}
