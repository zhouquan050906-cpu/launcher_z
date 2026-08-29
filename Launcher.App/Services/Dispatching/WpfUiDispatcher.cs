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

using System.Windows.Threading;

namespace Launcher.App.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public bool HasAccess => global::System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? true;

    public void Post(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    public void PostAfterTransition(Action action)
    {
        UiTransitionGate.RunWhenIdle(action);
    }

    public void Invoke(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();

        return dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task.Unwrap();
    }
}
