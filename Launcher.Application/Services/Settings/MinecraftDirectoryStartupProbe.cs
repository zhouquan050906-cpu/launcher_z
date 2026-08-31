/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Microsoft.Extensions.Logging;

namespace Launcher.Application.Services;

/// <summary>
/// 启动序列专用的目录可用性探测：异步、带上限、可并行。
/// </summary>
/// <remarks>
/// 目录探测是阻塞的文件系统调用，断连的网络路径或休眠中的移动盘可能几十秒才返回，
/// 而启动恢复要把已登记的目录逐个探一遍。这一切都发生在主窗口出现之前，
/// 既不能占着 UI 线程，也不能让单个失效目录把启动无限期拖住。
/// 超时按"不可用"处理：恢复流程会换一个能用的目录并弹出既有的提示对话框，
/// 用户随时可以在设置里切回去，不会丢任何文件。
/// </remarks>
public static class MinecraftDirectoryStartupProbe
{
    /// <summary>
    /// 单次探测的上限。能用的目录在毫秒级就有结果，首次连接的网络盘也在两三秒内；
    /// 取 5 秒既不会误伤慢速网络盘，又能把最坏情况压到用户可以接受的范围。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static Task<bool> IsAccessibleAsync(
        IMinecraftDirectoryFileSystem fileSystem,
        string directoryPath,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return RunBoundedAsync(
            () => fileSystem.DirectoryIsAccessible(directoryPath),
            directoryPath,
            nameof(IMinecraftDirectoryFileSystem.DirectoryIsAccessible),
            timeout,
            logger,
            cancellationToken);
    }

    public static Task<bool> ExistsAsync(
        IMinecraftDirectoryFileSystem fileSystem,
        string directoryPath,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return RunBoundedAsync(
            () => fileSystem.DirectoryExists(directoryPath),
            directoryPath,
            nameof(IMinecraftDirectoryFileSystem.DirectoryExists),
            timeout,
            logger,
            cancellationToken);
    }

    private static async Task<bool> RunBoundedAsync(
        Func<bool> probe,
        string directoryPath,
        string operation,
        TimeSpan? timeout,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        var resolvedTimeout = timeout ?? DefaultTimeout;
        try
        {
            // 阻塞调用没法取消，只能放到线程池上等一个有上限的时间。超时后那个线程会继续
            // 跑到操作系统返回为止，但启动流程已经不再等它，UI 线程也从未被占用。
            return await Task.Run(probe, cancellationToken)
                .WaitAsync(resolvedTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger?.LogWarning(
                "Timed out probing a Minecraft directory during startup. Operation={Operation} Directory={MinecraftDirectory} TimeoutMilliseconds={TimeoutMilliseconds}",
                operation,
                directoryPath,
                resolvedTimeout.TotalMilliseconds);
            return false;
        }
    }

    /// <summary>
    /// 并行探测一组目录并返回快照。并行而非串行：总耗时取决于最慢的一个，
    /// 而不是所有失效目录的超时之和。
    /// </summary>
    public static async Task<MinecraftDirectoryAvailabilitySnapshot> ProbeAsync(
        IMinecraftDirectoryFileSystem fileSystem,
        IEnumerable<string> directoryPaths,
        TimeSpan? timeout = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(directoryPaths);

        // 按归一化后的写法建索引：恢复逻辑查的也是归一化路径，否则同一个目录会因为
        // 大小写或结尾分隔符对不上而查不到快照，白探一遍。归一化不了的条目直接跳过，
        // 让它回落到实时探测。
        var probedPaths = directoryPaths
            .Select(TryNormalize)
            .Where(directoryPath => directoryPath is not null)
            .Select(directoryPath => directoryPath!)
            .Distinct(MinecraftDirectoryPath.Comparer)
            .ToArray();
        var results = await Task.WhenAll(probedPaths.Select(async directoryPath =>
            (
                DirectoryPath: directoryPath,
                IsAccessible: await IsAccessibleAsync(
                        fileSystem,
                        directoryPath,
                        timeout,
                        logger,
                        cancellationToken)
                    .ConfigureAwait(false)
            ))).ConfigureAwait(false);

        return new MinecraftDirectoryAvailabilitySnapshot(
            fileSystem,
            results.ToDictionary(
                result => result.DirectoryPath,
                result => result.IsAccessible,
                MinecraftDirectoryPath.Comparer));
    }

    private static string? TryNormalize(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return null;

        try
        {
            return MinecraftDirectoryPath.Normalize(directoryPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// 预先探好的可用性快照，交给同步的启动恢复逻辑使用，让它不必自己去做阻塞探测。
/// </summary>
/// <remarks>
/// 只有 <see cref="DirectoryIsAccessible"/> 读快照，且仅限探测过的路径；
/// 其余调用一律回落到真实文件系统——它们要么问的是别的问题（目录存不存在），
/// 要么必须看到最新状态（刚补建出来的目录）。
///
/// 已知残留：回落的这几个调用没有上限。它们只作用在启动器自带的默认目录上
/// （<c>AppContext.BaseDirectory</c> 下的 .minecraft），而且"建目录"和"建完确认可用"
/// 按定义就必须实时执行，读快照会得到过期答案。要给它们加上限得让 Recover 变成异步，
/// 而 Recover 跑在 <c>ISettingsService.UpdateAsync(Action&lt;LauncherSettings&gt;)</c> 的同步回调里，
/// 那需要改动整个设置服务的签名。已登记目录列表的遍历是有上限的（都在快照里），
/// 因此剩下的风险仅限于"把启动器装在断连的网络共享上"这一种情形。
/// </remarks>
public sealed class MinecraftDirectoryAvailabilitySnapshot : IMinecraftDirectoryFileSystem
{
    private readonly IMinecraftDirectoryFileSystem fileSystem;
    private readonly Dictionary<string, bool> probedDirectories;

    internal MinecraftDirectoryAvailabilitySnapshot(
        IMinecraftDirectoryFileSystem fileSystem,
        Dictionary<string, bool> probedDirectories)
    {
        this.fileSystem = fileSystem;
        this.probedDirectories = probedDirectories;
    }

    public bool DirectoryExists(string directoryPath) => fileSystem.DirectoryExists(directoryPath);

    public bool DirectoryIsAccessible(string directoryPath) =>
        probedDirectories.TryGetValue(directoryPath, out var isAccessible)
            ? isAccessible
            : fileSystem.DirectoryIsAccessible(directoryPath);

    public string EnsureDirectoryExists(string directoryPath)
    {
        var ensuredDirectory = fileSystem.EnsureDirectoryExists(directoryPath);
        // 补建之后快照就过期了：探测时它还不存在，继续读快照会把刚建好的目录判成不可用，
        // 让启动误判为"恢复失败"。丢掉这条记录，后续查询回落到真实文件系统。
        probedDirectories.Remove(directoryPath);
        probedDirectories.Remove(ensuredDirectory);
        return ensuredDirectory;
    }
}
