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

using System.IO;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

/// <summary>
/// 探测启动器数据目录是否真的可写。
/// </summary>
/// <remarks>
/// 探测的不是"能否新建文件"，而是 <see cref="AtomicJsonFileWriter"/> 落盘用的那一套动作：
/// 新建临时文件，再用替换式重命名盖掉已有文件。部分网络共享允许新建却不支持替换重命名，
/// 只探新建会得到一个乐观的假结论。
/// 探测只回答"此刻是否可写"，不能证明后续写入一定成功——网络盘上的占用是间歇性的。
/// </remarks>
public sealed class LauncherDataDirectoryProbe : ILauncherDataDirectoryProbe
{
    /// <summary>
    /// 单次探测的上限。文件系统调用无法取消，断连的网络路径可能几十秒才返回，
    /// 与 Minecraft 目录探测取同一个 5 秒上限。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger<LauncherDataDirectoryProbe> logger;

    public LauncherDataDirectoryProbe(ILogger<LauncherDataDirectoryProbe>? logger = null)
    {
        this.logger = logger ?? NullLogger<LauncherDataDirectoryProbe>.Instance;
    }

    public Task<bool> IsWritableAsync(
        string? directoryPath,
        CancellationToken cancellationToken = default) =>
        IsWritableAsync(directoryPath, DefaultTimeout, cancellationToken);

    internal async Task<bool> IsWritableAsync(
        string? directoryPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            // 阻塞调用没法取消，只能放到线程池上等一个有上限的时间。
            return await Task.Run(() => TryWriteAndReplace(directoryPath), cancellationToken)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Timed out probing the launcher data directory. DataDirectory={DataDirectory} TimeoutMilliseconds={TimeoutMilliseconds}",
                directoryPath,
                timeout.TotalMilliseconds);
            return false;
        }
    }

    private static bool TryWriteAndReplace(string directoryPath)
    {
        var sourcePath = Path.Combine(directoryPath, $".write-probe-{Guid.NewGuid():N}.tmp");
        var destinationPath = Path.Combine(directoryPath, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directoryPath);
            WriteProbeFile(sourcePath);
            WriteProbeFile(destinationPath);
            File.Move(sourcePath, destinationPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(sourcePath);
            TryDelete(destinationPath);
        }
    }

    private static void WriteProbeFile(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.WriteByte(0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
