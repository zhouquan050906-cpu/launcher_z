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

namespace Launcher.App.Diagnostics;

/// <summary>
/// 从实际观测到的合成帧间隔推断显示器刷新周期，供掉帧判定使用。
/// WPF 不公开刷新率，但渲染不可能快于显示器，因此取观测下界即可在几帧内收敛。
/// </summary>
internal static class DisplayFrameIntervalEstimator
{
    internal const double DefaultIntervalMs = 1000d / 60d;

    // 下界防止偶发的重复帧时间戳把估计值压到不真实的水平，上界覆盖 30Hz 低刷新场景。
    private const double MinimumIntervalMs = 1000d / 250d;
    private const double MaximumIntervalMs = 1000d / 24d;

    private static double currentIntervalMs = DefaultIntervalMs;

    internal static double CurrentIntervalMs => Volatile.Read(ref currentIntervalMs);

    internal static void Observe(double intervalMs)
    {
        if (double.IsNaN(intervalMs) || intervalMs < MinimumIntervalMs || intervalMs > MaximumIntervalMs)
            return;

        // 只向下收敛：一次卡顿会拉高间隔，但不会让估计值失真。
        if (intervalMs < Volatile.Read(ref currentIntervalMs))
            Volatile.Write(ref currentIntervalMs, intervalMs);
    }

    internal static void ResetForTesting()
    {
        Volatile.Write(ref currentIntervalMs, DefaultIntervalMs);
    }
}
