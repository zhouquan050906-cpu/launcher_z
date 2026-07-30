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

using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class LocalResourceCategoryEnrichmentCoordinatorTests
{
    [Fact]
    public void PreferredRemoteIconReplacesLocalFallbackOnlyOnce()
    {
        const string path = "instance/resourcepacks/example.zip";
        const string localIcon = "file:///cache/local-pack.png";
        const string remoteIcon = "file:///cache/remote-project.png";
        var item = new LocalResourcePack
        {
            FullPath = path,
            IconSource = localIcon
        };
        var metadata = new Dictionary<string, LocalResourceEnrichmentResult>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = new([], remoteIcon)
        };
        var dispatcher = new QueueingUiDispatcher();
        var changedCount = 0;
        using var coordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalResourcePack>(
            new FixedMetadataService(metadata, metadata),
            ResourceProjectKind.ResourcePack,
            resourcePack => resourcePack.FullPath,
            resourcePack => resourcePack.Categories,
            static (resourcePack, categories) => resourcePack.Categories = categories,
            () => [item],
            () => changedCount++,
            dispatcher,
            NullLogger.Instance,
            iconSourceSelector: resourcePack => resourcePack.IconSource,
            iconSourceSetter: static (resourcePack, iconSource) => resourcePack.IconSource = iconSource,
            preferResolvedIconSource: true);

        coordinator.Queue([item]);
        dispatcher.RunAll();

        Assert.Equal(remoteIcon, item.IconSource);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void DefaultIconPolicyDoesNotReplaceExistingShaderIcon()
    {
        const string path = "instance/shaderpacks/example.zip";
        const string localIcon = "file:///cache/existing-shader.png";
        var item = new LocalShaderPack
        {
            FullPath = path,
            IconSource = localIcon
        };
        var metadata = new Dictionary<string, LocalResourceEnrichmentResult>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = new([], "file:///cache/remote-shader.png")
        };
        var dispatcher = new QueueingUiDispatcher();
        var changedCount = 0;
        using var coordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalShaderPack>(
            new FixedMetadataService(metadata, metadata),
            ResourceProjectKind.ShaderPack,
            shaderPack => shaderPack.FullPath,
            shaderPack => shaderPack.Categories,
            static (shaderPack, categories) => shaderPack.Categories = categories,
            () => [item],
            () => changedCount++,
            dispatcher,
            NullLogger.Instance,
            iconSourceSelector: shaderPack => shaderPack.IconSource,
            iconSourceSetter: static (shaderPack, iconSource) => shaderPack.IconSource = iconSource);

        coordinator.Queue([item]);
        dispatcher.RunAll();

        Assert.Equal(localIcon, item.IconSource);
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void MissingRemoteIconKeepsResourcePackLocalFallback()
    {
        const string path = "instance/resourcepacks/example.zip";
        const string localIcon = "file:///cache/local-pack.png";
        var item = new LocalResourcePack
        {
            FullPath = path,
            IconSource = localIcon
        };
        var metadata = new Dictionary<string, LocalResourceEnrichmentResult>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = new([], null)
        };
        var dispatcher = new QueueingUiDispatcher();
        var changedCount = 0;
        using var coordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalResourcePack>(
            new FixedMetadataService(metadata, metadata),
            ResourceProjectKind.ResourcePack,
            resourcePack => resourcePack.FullPath,
            resourcePack => resourcePack.Categories,
            static (resourcePack, categories) => resourcePack.Categories = categories,
            () => [item],
            () => changedCount++,
            dispatcher,
            NullLogger.Instance,
            iconSourceSelector: resourcePack => resourcePack.IconSource,
            iconSourceSetter: static (resourcePack, iconSource) => resourcePack.IconSource = iconSource,
            preferResolvedIconSource: true);

        coordinator.Queue([item]);
        dispatcher.RunAll();

        Assert.Equal(localIcon, item.IconSource);
        Assert.Equal(0, changedCount);
    }

    private sealed class FixedMetadataService(
        IReadOnlyDictionary<string, LocalResourceEnrichmentResult> cached,
        IReadOnlyDictionary<string, LocalResourceEnrichmentResult> resolved)
        : ILocalResourceCategoryEnrichmentService
    {
        public Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveCachedMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(cached);

        public Task<IReadOnlyDictionary<string, LocalResourceEnrichmentResult>> ResolveMetadataAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(resolved);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCachedCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>());

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>> ResolveCategoriesAsync(
            IReadOnlyList<LocalResourceCategoryCandidate> resources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<ResourceProjectCategory>>>(
                new Dictionary<string, IReadOnlyList<ResourceProjectCategory>>());
    }

    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> actions = new();

        public bool HasAccess => true;

        public void Post(Action action) => actions.Enqueue(action);

        public void Invoke(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();

        public void RunAll()
        {
            while (actions.TryDequeue(out var action))
                action();
        }
    }
}
