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

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class LaunchCommandRunnerTests : TestTempDirectory
{
    [Fact]
    public async Task PreCanceledTokenDoesNotStartCommand()
    {
        Directory.CreateDirectory(TempRoot);
        var markerPath = Path.Combine(TempRoot, "started.txt");
        var runner = new LaunchCommandRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            $"echo started>\"{markerPath}\"",
            TempRoot,
            waitForExit: false,
            cancellation.Token));

        Assert.False(File.Exists(markerPath));
    }
}
