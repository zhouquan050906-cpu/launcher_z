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
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.FileSystem;

public sealed class LocalShaderPackService : ILocalShaderPackService
{
    private const string SupportedArchiveExtension = ".zip";
    private readonly ILogger<LocalShaderPackService> logger;
    private readonly IUserFileDeletionService userFileDeletionService;
    private readonly SemaphoreSlim snapshotLock = new(1, 1);
    private readonly BoundedLocalContentSnapshotCache<LocalContentFileIdentity, LocalShaderPack> snapshots = new();

    public LocalShaderPackService(
        ILogger<LocalShaderPackService>? logger = null,
        IUserFileDeletionService? userFileDeletionService = null)
    {
        this.logger = logger ?? NullLogger<LocalShaderPackService>.Instance;
        this.userFileDeletionService = userFileDeletionService ?? new UserFileDeletionService();
    }

    public async Task<IReadOnlyList<LocalShaderPack>> GetShaderPacksAsync(
        GameInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await snapshotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<IReadOnlyList<LocalShaderPack>>(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var shaderPacksDirectory = GetShaderPacksDirectory(instance);
                    if (!Directory.Exists(shaderPacksDirectory))
                    {
                        snapshots.Remove(shaderPacksDirectory);
                        return [];
                    }

                    var inventory = Directory.EnumerateFiles(
                            shaderPacksDirectory,
                            $"*{SupportedArchiveExtension}",
                            SearchOption.TopDirectoryOnly)
                        .Select(CreateIdentity)
                        .OrderByDescending(item => item.CreationTimeUtcTicks)
                        .ThenBy(item => Path.GetFileNameWithoutExtension(item.FullPath), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (inventory.Length == 0)
                    {
                        snapshots.Remove(shaderPacksDirectory);
                        logger.LogDebug(
                            "Local shader pack inventory checked. InstanceId={InstanceId} Count=0",
                            instance.Id);
                        return [];
                    }

                    var snapshot = snapshots.GetOrCreate(shaderPacksDirectory);
                    var shaderPacks = snapshot.Reconcile(
                        inventory,
                        static item => item.FullPath,
                        ToLocalShaderPack);
                    logger.LogDebug(
                        "Local shader pack inventory checked. InstanceId={InstanceId} Count={ShaderPackCount}",
                        instance.Id,
                        shaderPacks.Count);
                    return shaderPacks;
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            snapshotLock.Release();
        }
    }

    public Task<LocalShaderPackImportResult> ImportAsync(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        return Task.Run(
            () => ImportCore(instance, archivePath, cancellationToken),
            cancellationToken);
    }

    public Task DeleteAsync(LocalShaderPack shaderPack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPack);

        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(shaderPack.FullPath))
                {
                    logger.LogDebug(
                        "Skipping local shader pack delete because file does not exist. Path={Path}",
                        shaderPack.FullPath);
                    return;
                }

                userFileDeletionService.DeleteFile(shaderPack.FullPath);
                logger.LogInformation("Local shader pack deleted. Name={Name}", shaderPack.Name);
                logger.LogDebug("Deleted local shader pack path. Path={Path}", shaderPack.FullPath);
            },
            cancellationToken);
    }

    public Task DeleteAsync(IEnumerable<LocalShaderPack> shaderPacks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shaderPacks);

        return Task.Run(
            async () =>
            {
                foreach (var shaderPack in shaderPacks.DistinctBy(shaderPack => shaderPack.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DeleteAsync(shaderPack, cancellationToken);
                }
            },
            cancellationToken);
    }

    private LocalShaderPackImportResult ImportCore(
        GameInstance instance,
        string archivePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(normalizedArchivePath))
        {
            logger.LogDebug(
                "Skipping local shader pack import because archive does not exist. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.FileNotFound);
        }

        if (!normalizedArchivePath.EndsWith(SupportedArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Skipping local shader pack import because archive type is unsupported. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnsupportedArchive);
        }

        logger.LogDebug(
            "Importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
            instance.Id,
            normalizedArchivePath);

        try
        {
            var shaderPacksDirectory = GetShaderPacksDirectory(instance);
            Directory.CreateDirectory(shaderPacksDirectory);

            var targetPath = ResolveUniqueFilePath(shaderPacksDirectory, Path.GetFileName(normalizedArchivePath));
            File.Copy(normalizedArchivePath, targetPath, overwrite: false);

            var importedShaderPack = ToLocalShaderPack(CreateIdentity(targetPath));
            logger.LogDebug(
                "Local shader pack archive imported. InstanceId={InstanceId} ArchivePath={ArchivePath} ShaderPackPath={ShaderPackPath}",
                instance.Id,
                normalizedArchivePath,
                targetPath);
            return LocalShaderPackImportResult.Success(importedShaderPack);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because a file operation failed. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Failed to import local shader pack archive because access was denied. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected failure while importing local shader pack archive. InstanceId={InstanceId} ArchivePath={ArchivePath}",
                instance.Id,
                normalizedArchivePath);
            return LocalShaderPackImportResult.Failure(LocalShaderPackImportFailureReason.UnexpectedError);
        }
    }

    private static LocalContentFileIdentity CreateIdentity(string path)
    {
        var file = new FileInfo(path);
        return new LocalContentFileIdentity(
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            file.CreationTimeUtc.Ticks);
    }

    private static LocalShaderPack ToLocalShaderPack(LocalContentFileIdentity identity)
    {
        return new LocalShaderPack
        {
            Name = Path.GetFileNameWithoutExtension(identity.FullPath),
            FileName = Path.GetFileName(identity.FullPath),
            FullPath = identity.FullPath,
            CreatedAt = new DateTimeOffset(identity.CreationTimeUtcTicks, TimeSpan.Zero)
        };
    }

    private static string ResolveUniqueFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var index = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            index++;
        }

        return candidate;
    }

    private static string GetShaderPacksDirectory(GameInstance instance)
    {
        return Path.Combine(instance.InstanceDirectory, "shaderpacks");
    }
}
