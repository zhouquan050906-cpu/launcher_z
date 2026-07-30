/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.Services;

internal sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    private readonly Action<T> callback = callback ?? throw new ArgumentNullException(nameof(callback));

    public void Report(T value) => callback(value);
}
