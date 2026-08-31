/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
using Launcher.Infrastructure.FileSystem;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Application.Settings;

/// <summary>
/// 端到端复现 App.OnStartup 的目录处理顺序（加载 → 首次运行初始化 → 发现登记 → 失效恢复），
/// 使用真实文件系统与真实服务，锁定"全新安装"和"旧版升级"两条路径的结果。
/// </summary>
/// <remarks>
/// JsonSettingsService 内部固定使用无参 LauncherPathProvider，无法注入，因此测试改为显式写入
/// 一份"刚创建"的 settings.json 并显式传入 wasCreated，以免默认目录/官方目录落到开发机的真实路径上。
/// WasCreated 标志本身的行为由 JsonSettingsMinecraftDirectoryTests 覆盖。
/// </remarks>
public sealed class MinecraftDirectoryStartupSequenceTests : TestTempDirectory
{
    [Fact]
    public async Task FreshInstallCreatesAndSelectsLauncherDefaultAndOnlyRegistersOfficialDirectory()
    {
        var environment = CreateEnvironment(createOfficialDirectory: true);
        await WriteSettingsAsync(environment, environment.DefaultDirectory);

        var startup = await RunStartupSequenceAsync(environment, wasCreated: true);

        // 启动器根目录下的 .minecraft 被真实创建出来，并且是当前使用的目录。
        Assert.True(Directory.Exists(environment.DefaultDirectory));
        Assert.Equal(environment.DefaultDirectory, startup.Settings.MinecraftDirectory);

        // 官方目录只是被登记进列表备选，不会抢占当前目录。
        Assert.Equal(
            [environment.DefaultDirectory, environment.OfficialDirectory],
            startup.Settings.MinecraftDirectories);

        // 两个目录的文件夹名都叫 .minecraft，官方目录必须用带来源含义的名字，否则列表里会重名。
        var displayNames = startup.Settings.MinecraftDirectoryDisplayNames;
        Assert.Equal(".minecraft", displayNames[environment.DefaultDirectory]);
        Assert.Equal(
            Launcher.App.Resources.Strings.Settings_OfficialMinecraftDirectoryDisplayName,
            displayNames[environment.OfficialDirectory]);
        Assert.NotEqual(displayNames[environment.DefaultDirectory], displayNames[environment.OfficialDirectory]);

        // 当前目录一开始就可用，因此不会触发恢复，也就不会弹"目录已失效"。
        Assert.Null(startup.Recovery);
    }

    [Fact]
    public async Task FreshInstallWithoutOfficialDirectoryRegistersOnlyTheLauncherDefault()
    {
        var environment = CreateEnvironment(createOfficialDirectory: false);
        await WriteSettingsAsync(environment, environment.DefaultDirectory);

        var startup = await RunStartupSequenceAsync(environment, wasCreated: true);

        Assert.True(Directory.Exists(environment.DefaultDirectory));
        Assert.Equal(environment.DefaultDirectory, startup.Settings.MinecraftDirectory);
        Assert.Equal(environment.DefaultDirectory, Assert.Single(startup.Settings.MinecraftDirectories));
        Assert.Null(startup.Recovery);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpgradeKeepsTheLegacyDirectorySelectedAndMigratesItIntoTheList(
        bool officialDirectoryExists)
    {
        var environment = CreateEnvironment(createOfficialDirectory: officialDirectoryExists);

        // 旧版 settings.json 只有 MinecraftDirectory 一个字段，且指向一个用户自定义目录。
        var legacyDirectory = MinecraftDirectoryPath.Normalize(Path.Combine(TempRoot, "legacy-mc"));
        Directory.CreateDirectory(legacyDirectory);
        await WriteSettingsAsync(environment, legacyDirectory);

        var startup = await RunStartupSequenceAsync(environment, wasCreated: false);

        // 旧目录被迁移进新的列表结构，并且仍然是当前使用的目录。
        Assert.Equal(legacyDirectory, startup.Settings.MinecraftDirectory);
        Assert.Contains(legacyDirectory, startup.Settings.MinecraftDirectories);
        Assert.Equal("legacy-mc", startup.Settings.MinecraftDirectoryDisplayNames[legacyDirectory]);

        // 旧目录可用，所以不触发恢复，不会被切走、也不会弹窗。
        Assert.Null(startup.Recovery);

        // 启动器默认目录此时并不存在，不该被凭空登记进列表。
        Assert.DoesNotContain(environment.DefaultDirectory, startup.Settings.MinecraftDirectories);
        Assert.Equal(
            officialDirectoryExists,
            startup.Settings.MinecraftDirectories.Contains(environment.OfficialDirectory));
    }

    [Fact]
    public async Task UpgradeFromLauncherDefaultKeepsItSelectedEvenWhenOfficialDirectoryExists()
    {
        var environment = CreateEnvironment(createOfficialDirectory: true);

        // 旧版用的就是启动器默认目录；开过一次旧版就会被建出来。
        Directory.CreateDirectory(environment.DefaultDirectory);
        await WriteSettingsAsync(environment, environment.DefaultDirectory);

        var startup = await RunStartupSequenceAsync(environment, wasCreated: false);

        Assert.Equal(environment.DefaultDirectory, startup.Settings.MinecraftDirectory);
        Assert.Equal(
            [environment.DefaultDirectory, environment.OfficialDirectory],
            startup.Settings.MinecraftDirectories);
        Assert.Null(startup.Recovery);
    }

    /// <summary>
    /// 复刻 App.OnStartup 中与 Minecraft 目录相关的调用顺序。
    /// </summary>
    private static async Task<StartupOutcome> RunStartupSequenceAsync(
        StartupEnvironment environment,
        bool wasCreated)
    {
        var management = new MinecraftDirectoryManagementService();
        var initialization = new MinecraftDirectoryStartupInitializationService(
            environment.FileSystem,
            management);
        var recoveryService = new MinecraftDirectoryStartupRecoveryService(
            environment.FileSystem,
            management);

        // ① 加载设置。
        var settings = await environment.SettingsService.LoadAsync();

        // ② 首次运行：先把启动器默认目录建出来并选中。
        if (wasCreated)
        {
            settings = await environment.SettingsService.UpdateAsync(
                latest => initialization.InitializeDefaultDirectory(latest, environment.DefaultDirectory));
        }

        // ③ 发现已存在的目录并登记（不改变当前选中目录）。
        var discovered = await environment.DiscoveryService.DiscoverExistingDirectoriesAsync();
        if (discovered.Any(discovery =>
                !settings.MinecraftDirectories.Contains(
                    discovery.DirectoryPath,
                    MinecraftDirectoryPath.Comparer)
                && !settings.ExcludedMinecraftDirectories.Contains(
                    discovery.DirectoryPath,
                    MinecraftDirectoryPath.Comparer)))
        {
            settings = await environment.SettingsService.UpdateAsync(
                latest => management.RegisterDiscoveredDirectories(
                    latest,
                    discovered,
                    ResolveDisplayName));
        }

        // ④ 当前目录不可用时才走恢复。上面每一步返回的设置都已归一化，无需再读一次盘。
        MinecraftDirectoryStartupRecoveryResult? recovery = null;
        if (!environment.FileSystem.DirectoryIsAccessible(settings.MinecraftDirectory))
        {
            settings = await environment.SettingsService.UpdateAsync(
                latest => recovery = recoveryService.Recover(latest, environment.DefaultDirectory));
        }

        return new StartupOutcome(settings, recovery);
    }

    /// <summary>与 App.ResolveDiscoveredMinecraftDirectoryDisplayName 保持一致。</summary>
    private static string? ResolveDisplayName(MinecraftDirectoryKind kind) =>
        kind is MinecraftDirectoryKind.Official
            ? Launcher.App.Resources.Strings.Settings_OfficialMinecraftDirectoryDisplayName
            : null;

    private static Task WriteSettingsAsync(StartupEnvironment environment, string minecraftDirectory) =>
        File.WriteAllTextAsync(
            Path.Combine(environment.DataDirectory, "settings.json"),
            JsonSerializer.Serialize(new { MinecraftDirectory = minecraftDirectory }));

    private StartupEnvironment CreateEnvironment(bool createOfficialDirectory)
    {
        var launcherBaseDirectory = Path.Combine(TempRoot, "launcher");
        var applicationDataDirectory = Path.Combine(TempRoot, "appdata");
        var dataDirectory = Path.Combine(TempRoot, "data");
        Directory.CreateDirectory(launcherBaseDirectory);
        Directory.CreateDirectory(applicationDataDirectory);
        Directory.CreateDirectory(dataDirectory);

        var pathProvider = new LauncherPathProvider(launcherBaseDirectory, applicationDataDirectory);
        if (createOfficialDirectory)
            Directory.CreateDirectory(pathProvider.OfficialMinecraftDirectory);

        var fileSystem = new MinecraftDirectoryFileSystem();
        return new StartupEnvironment(
            dataDirectory,
            MinecraftDirectoryPath.Normalize(pathProvider.DefaultMinecraftDirectory),
            MinecraftDirectoryPath.Normalize(pathProvider.OfficialMinecraftDirectory),
            fileSystem,
            new MinecraftDirectoryDiscoveryService(pathProvider, fileSystem),
            new JsonSettingsService(dataDirectory));
    }

    private sealed record StartupEnvironment(
        string DataDirectory,
        string DefaultDirectory,
        string OfficialDirectory,
        IMinecraftDirectoryFileSystem FileSystem,
        IMinecraftDirectoryDiscoveryService DiscoveryService,
        JsonSettingsService SettingsService);

    private sealed record StartupOutcome(
        LauncherSettings Settings,
        MinecraftDirectoryStartupRecoveryResult? Recovery);
}
