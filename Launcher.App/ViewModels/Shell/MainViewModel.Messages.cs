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

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Resources;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Shell;

public sealed partial class MainViewModel
{
private void ShowFloatingMessage(FloatingMessageRequest request)
    {
        // 新消息取消上一轮隐藏计时器，让快速连续提示从最后一次显示重新计时。
        if (!uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(() => ShowFloatingMessage(request));
            return;
        }

        floatingMessageHideCancellation?.Cancel();
        floatingMessageHideCancellation?.Dispose();
        floatingMessageHideCancellation = null;

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            IsFloatingMessageOpen = false;
            FloatingMessage = string.Empty;
            return;
        }

        FloatingMessage = request.Message;
        IsFloatingMessageOpen = true;

        // 拖放提示必须覆盖整个拖动过程，不能计时消失；它由拖放生命周期显式清除。
        if (!request.AutoHide)
            return;

        floatingMessageHideCancellation = new CancellationTokenSource();
        ObserveShellTask(
            HideFloatingMessageAfterDelayAsync(floatingMessageHideCancellation.Token),
            "hide the floating message");
    }

    private async Task HideFloatingMessageAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FloatingMessageDuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        uiDispatcher.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            IsFloatingMessageOpen = false;
        });
    }

    private void ObserveShellTask(Task task, string operation)
    {
        // WPF 事件不能直接等待 Task，统一观察以避免导航事件产生未观察异常。
        _ = ObserveShellTaskAsync(task, operation);
    }

    private async Task ObserveShellTaskAsync(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to {Operation}.", operation);
        }
    }
}
