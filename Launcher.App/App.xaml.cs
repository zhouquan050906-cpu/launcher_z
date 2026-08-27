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

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Launcher.Application.Accounts;
using Launcher.Application.DependencyInjection;
using Launcher.Application.Services;
using Launcher.App.Diagnostics;
using Launcher.App.Logging;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.DependencyInjection;
using Launcher.Infrastructure.Persistence;
using Launcher.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Launcher.App;

public partial class App : System.Windows.Application
{
    private readonly LauncherBootstrapPreferences bootstrapPreferences;
    private ServiceProvider? serviceProvider;
    private bool isUpdateApplyMode;

    static App()
    {
        EventManager.RegisterClassHandler(
            typeof(Control),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(SuppressControlFocusVisual));
    }

    public App()
    {
        bootstrapPreferences = new LauncherBootstrapPreferences(
            LauncherDefaults.DefaultLauncherLanguage,
            EnableDiagnosticLogging: false);
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (LauncherUpdateApplyOptions.Parse(args) is null
            && LauncherUpdateRecoveryOptions.Parse(args) is null)
        {
            bootstrapPreferences = new JsonSettingsService().LoadLauncherBootstrapPreferences();
            ApplyLauncherCulture(bootstrapPreferences.LauncherLanguage);
        }
    }

    private static void SuppressControlFocusVisual(object sender, RoutedEventArgs e)
    {
        if (sender is Control { FocusVisualStyle: not null } control)
            control.FocusVisualStyle = null;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var updateApplyOptions = LauncherUpdateApplyOptions.Parse(e.Args);
        if (updateApplyOptions is not null)
        {
            isUpdateApplyMode = true;
            var exitCode = new LauncherUpdateApplyRunner().Run(updateApplyOptions);
            Shutdown(exitCode);
            return;
        }

        var updateRecoveryOptions = LauncherUpdateRecoveryOptions.Parse(e.Args);
        if (updateRecoveryOptions is not null)
        {
            isUpdateApplyMode = true;
            var exitCode = new LauncherUpdateApplyRunner().RunRecovery(updateRecoveryOptions);
            Shutdown(exitCode);
            return;
        }

        var logLevelController = new LauncherLogLevelController(
            bootstrapPreferences.EnableDiagnosticLogging);
        Log.Logger = LauncherLogConfiguration.CreateLogger(
            logLevelController.LevelSwitch,
            logLevelController.MicrosoftLevelSwitch);
        RegisterUnhandledExceptionLogging();

        try
        {
            Log.Information("Launcher startup started. ArgumentCount={ArgumentCount}", e.Args.Length);
            if (LauncherUpdateStartupCoordinator.TryStartPendingRecovery(
                    e.Args,
                    Environment.ProcessPath,
                    Environment.ProcessId))
            {
                Log.Information("Pending launcher update recovery process started.");
                Shutdown(0);
                return;
            }

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false);
            });
            services.AddSingleton<ILauncherLogLevelController>(logLevelController);
            services.AddLauncherApplication();
            services.AddLauncherInfrastructure();
            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<IFloatingMessageService, FloatingMessageService>();
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IFilePickerService, FilePickerService>();
            services.AddSingleton<IInstanceFolderService, InstanceFolderService>();
            services.AddSingleton<ILauncherBackgroundImageLoader, LauncherBackgroundImageLoader>();
            services.AddSingleton<IExternalLinkService, ExternalLinkService>();
            services.AddSingleton<IApplicationExitService, ApplicationExitService>();
            services.AddSingleton<IInfoReferenceProjectCatalog, EmbeddedInfoReferenceProjectCatalog>();
            services.AddSingleton<IMicrosoftLoginBrowserPageProvider, MicrosoftLoginBrowserPageProvider>();
            services.AddSingleton<IAccountDialogService, AccountDialogService>();
            services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
            services.AddSingleton(_ => new UiThreadStallMonitor(Dispatcher));
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IHomePageViewModelFactory, HomePageViewModelFactory>();
            services.AddSingleton<LauncherSessionCoordinator>();
            services.AddSingleton<LauncherStateSyncService>();
            services.AddSingleton<LauncherShutdownService>();
            services.AddSingleton<MainWindowPlacementService>();
            services.AddSingleton<LaunchStatusDialogViewModel>();
            services.AddSingleton<UserAgreementDialogViewModel>();
            services.AddSingleton<MinecraftDirectoryStartupRecoveryDialogViewModel>();
            services.AddSingleton<TerracottaAgreementDialogViewModel>();
            services.AddSingleton<LauncherBackgroundViewModel>();
            services.AddSingleton<AccountListViewModel>();
            services.AddSingleton<AccountDialogViewModel>();
            services.AddSingleton<AccountAppearanceViewModel>();
            services.AddSingleton<AccountOfflineUuidViewModel>();
            services.AddSingleton<AccountPurchaseViewModel>();
            services.AddSingleton<AccountSkinModelDialogViewModel>();
            services.AddSingleton<AccountPageViewModel>();
            services.AddSingleton<DownloadTasksPageViewModel>();
            services.AddSingleton<DownloadLocalImportDialogViewModel>();
            services.AddSingleton<DownloadPageViewModel>();
            services.AddSingleton<GameSettingsEditDialogViewModel>();
            services.AddSingleton<GameSettingsDetailsViewModel>();
            services.AddSingleton<GameSettingsInstanceListViewModel>();
            services.AddSingleton<GameSettingsDialogsViewModel>();
            services.AddSingleton<GameSettingsPageViewModel>();
            services.AddSingleton<MultiplayerPageViewModel>();
            services.AddSingleton<ResourcesPageViewModel>();
            services.AddSingleton<SettingsPageViewModel>();
            services.AddSingleton<InstanceManagementViewModel>();
            services.AddSingleton<LoaderSelectionViewModel>();
            services.AddSingleton<LocalModsViewModel>();
            services.AddSingleton<LocalSavesViewModel>();
            services.AddSingleton<LocalResourcePacksViewModel>();
            services.AddSingleton<LocalShaderPacksViewModel>();
            services.AddSingleton<ModrinthSearchViewModel>();
            services.AddSingleton<GameManagementViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            serviceProvider = services.BuildServiceProvider();
            Log.Debug("Service provider built.");

            var updateCacheCleaner = serviceProvider.GetRequiredService<LauncherUpdateCacheCleaner>();
            updateCacheCleaner.CleanupStaleCache(Environment.ProcessPath);

            var startupSettingsLoad = await serviceProvider
                .GetRequiredService<ISettingsService>()
                .LoadWithMetadataAsync();
            var startupSettings = startupSettingsLoad.Settings;
            logLevelController.SetDiagnosticLoggingEnabled(startupSettings.EnableDiagnosticLogging);
            ApplyLauncherCulture(startupSettings.LauncherLanguage);
            Log.Debug("Launcher culture initialized. Language={Language}", CultureInfo.CurrentUICulture.Name);
            if (startupSettingsLoad.WasCreated)
                startupSettings = await InitializeDefaultMinecraftDirectoryOnFirstRunAsync();
            // 上面每一步要么原样返回传入的设置，要么返回 UpdateAsync 落盘后的最新副本，
            // 两者都已归一化，因此不需要再额外读一次 settings.json。
            startupSettings = await RegisterDiscoveredMinecraftDirectoriesOnStartupAsync(startupSettings);
            var (recoveredSettings, minecraftDirectoryStartupRecovery) =
                await RecoverInvalidMinecraftDirectoryOnStartupAsync(startupSettings);
            startupSettings = recoveredSettings;
            base.OnStartup(e);

            await CleanupModpackWorkspacesOnStartupAsync();
            await CleanupResourceProjectWorkspacesOnStartupAsync();

            await RecoverPendingInstanceBackupsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceBackupService>(),
                startupSettings.MinecraftDirectory);
            await RecoverPendingInstanceRenamesOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceRenameRecoveryService>());

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            await mainViewModel.PrimeAsync(startupSettings, minecraftDirectoryStartupRecovery);
            var themeService = serviceProvider.GetRequiredService<IThemeService>();
            themeService.ApplyPreference(
                mainViewModel.Settings.Theme,
                mainViewModel.Settings.ThemeFollowSystem,
                mainViewModel.Settings.LauncherBackgroundOpacityPercent);
            themeService.ApplyAccent(mainViewModel.Settings.AccentColor);
            themeService.ApplyBackgroundEffect(
                mainViewModel.Settings.LauncherBackgroundEffect,
                mainViewModel.Settings.EnableImageBackgroundControlBlur);
            var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            serviceProvider.GetRequiredService<MainWindowPlacementService>()
                .Restore(mainWindow, mainViewModel.Settings);
            mainWindow.Show();
            UiPerformanceLog.LogRenderEnvironment(TryReadSystemMemorySnapshot(serviceProvider), mainWindow);
            serviceProvider.GetRequiredService<UiThreadStallMonitor>().Start();
            Log.Information(
                "Launcher startup completed. DurationMs={DurationMs} Language={Language} DiagnosticLogging={DiagnosticLogging}",
                System.Diagnostics.Stopwatch.GetElapsedTime(startupStartedAt).TotalMilliseconds,
                CultureInfo.CurrentUICulture.Name,
                logLevelController.IsDiagnosticLoggingEnabled);
            _ = CleanupPendingInstanceDeletionsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceDeletionCleanupService>());
            _ = CleanupPendingInstanceInstallsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceInstallCleanupService>());
            _ = CleanupModpackSandboxesOnStartupAsync(
                serviceProvider.GetRequiredService<IModpackSandboxCleanupService>());
            try
            {
                if (LauncherUpdateStartupCoordinator.TryConfirmStartup(
                        e.Args,
                        Environment.ProcessPath,
                        out var confirmedUpdaterPath))
                {
                    Log.Information("Launcher update startup confirmed.");
                    if (confirmedUpdaterPath is not null)
                        _ = CleanupConfirmedUpdateCacheAsync(updateCacheCleaner, confirmedUpdaterPath);
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to confirm launcher update startup.");
            }
            _ = CheckForLauncherUpdatesAfterAgreementAsync(mainViewModel);
        }
        catch (MinecraftDirectoryStartupRecoveryException exception)
        {
            Log.Fatal(exception, "Minecraft directory startup recovery failed.");
            MessageBox.Show(
                string.Format(
                    global::Launcher.App.Resources.Strings.Dialog_MinecraftDirectoryStartupRecoveryFailedMessageFormat,
                    exception.DirectoryPath),
                global::Launcher.App.Resources.Strings.Dialog_MinecraftDirectoryStartupRecoveryFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Launcher startup failed.");
            Shutdown(-1);
        }
    }

    private async Task<LauncherSettings> RegisterDiscoveredMinecraftDirectoriesOnStartupAsync(
        LauncherSettings startupSettings)
    {
        if (serviceProvider is null)
            return startupSettings;

        try
        {
            var discoveryService = serviceProvider.GetRequiredService<IMinecraftDirectoryDiscoveryService>();
            var managementService = serviceProvider.GetRequiredService<MinecraftDirectoryManagementService>();
            var discoveredDirectories = discoveryService.DiscoverExistingDirectories();
            if (!discoveredDirectories.Any(discovery =>
                    !startupSettings.MinecraftDirectories.Contains(
                        discovery.DirectoryPath,
                        MinecraftDirectoryPath.Comparer)
                    && !startupSettings.ExcludedMinecraftDirectories.Contains(
                        discovery.DirectoryPath,
                        MinecraftDirectoryPath.Comparer)))
            {
                return startupSettings;
            }

            var updatedSettings = await serviceProvider.GetRequiredService<ISettingsService>().UpdateAsync(
                settings => managementService.RegisterDiscoveredDirectories(
                    settings,
                    discoveredDirectories,
                    ResolveDiscoveredMinecraftDirectoryDisplayName));
            Log.Information(
                "Minecraft directories discovered during startup. DiscoveredCount={DiscoveredCount} RegisteredCount={RegisteredCount} CurrentMinecraftDirectory={CurrentMinecraftDirectory}",
                discoveredDirectories.Count,
                updatedSettings.MinecraftDirectories.Count,
                updatedSettings.MinecraftDirectory);
            return updatedSettings;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to discover and register Minecraft directories during startup.");
            return startupSettings;
        }
    }

    /// <summary>
    /// 官方启动器目录和启动器自带目录的文件夹名都是 .minecraft，只用目录名会在列表里重名，
    /// 因此自动发现官方目录时改用带来源含义的名称。返回 null 表示沿用目录名。
    /// </summary>
    /// <remarks>
    /// 设计如此：显示名在首次登记时写入 settings.json 并固定下来。
    /// 因此已登记过官方目录的老用户不会自动改名，之后切换界面语言名称也不跟随——
    /// 显示名被视为可由用户自行重命名的数据，而非每次渲染时解析的标签。
    /// </remarks>
    private static string? ResolveDiscoveredMinecraftDirectoryDisplayName(MinecraftDirectoryKind kind) =>
        kind is MinecraftDirectoryKind.Official
            ? global::Launcher.App.Resources.Strings.Settings_OfficialMinecraftDirectoryDisplayName
            : null;

    private async Task<LauncherSettings> InitializeDefaultMinecraftDirectoryOnFirstRunAsync()
    {
        if (serviceProvider is null)
            throw new InvalidOperationException("The launcher service provider is unavailable.");

        var pathProvider = serviceProvider.GetRequiredService<Launcher.Infrastructure.LauncherPathProvider>();
        var initializationService = serviceProvider
            .GetRequiredService<MinecraftDirectoryStartupInitializationService>();
        try
        {
            var initializedDirectory = string.Empty;
            var updatedSettings = await serviceProvider.GetRequiredService<ISettingsService>().UpdateAsync(
                settings => initializedDirectory = initializationService.InitializeDefaultDirectory(
                    settings,
                    pathProvider.DefaultMinecraftDirectory));
            Log.Information(
                "Default Minecraft directory initialized for the first launcher run. MinecraftDirectory={MinecraftDirectory}",
                initializedDirectory);
            return updatedSettings;
        }
        catch (MinecraftDirectoryStartupRecoveryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                pathProvider.DefaultMinecraftDirectory,
                "The initial Minecraft directory could not be initialized.",
                exception);
        }
    }

    private async Task<(LauncherSettings Settings, MinecraftDirectoryStartupRecoveryResult? Recovery)>
        RecoverInvalidMinecraftDirectoryOnStartupAsync(LauncherSettings startupSettings)
    {
        if (serviceProvider is null)
            return (startupSettings, null);

        var fileSystem = serviceProvider.GetRequiredService<IMinecraftDirectoryFileSystem>();
        if (fileSystem.DirectoryIsAccessible(startupSettings.MinecraftDirectory))
            return (startupSettings, null);

        var pathProvider = serviceProvider.GetRequiredService<Launcher.Infrastructure.LauncherPathProvider>();
        var recoveryService = serviceProvider.GetRequiredService<MinecraftDirectoryStartupRecoveryService>();
        MinecraftDirectoryStartupRecoveryResult? recovery = null;
        try
        {
            var updatedSettings = await serviceProvider.GetRequiredService<ISettingsService>().UpdateAsync(
                settings => recovery = recoveryService.Recover(
                    settings,
                    pathProvider.DefaultMinecraftDirectory));
            if (!fileSystem.DirectoryIsAccessible(updatedSettings.MinecraftDirectory))
            {
                throw new MinecraftDirectoryStartupRecoveryException(
                    pathProvider.DefaultMinecraftDirectory,
                    "The recovered Minecraft directory is not accessible.");
            }

            if (recovery is not null)
            {
                Log.Warning(
                    "Invalid Minecraft directory recovered during startup. InvalidMinecraftDirectory={InvalidMinecraftDirectory} SelectedMinecraftDirectory={SelectedMinecraftDirectory} UsedDefaultDirectory={UsedDefaultDirectory} CreatedDefaultDirectory={CreatedDefaultDirectory}",
                    recovery.InvalidDirectory,
                    recovery.SelectedDirectory,
                    recovery.UsedDefaultDirectory,
                    recovery.CreatedDefaultDirectory);
            }
            else
            {
                // 目录只是缺失并已就地补建，未发生切换，因此不上报给用户，只留下排查痕迹。
                Log.Information(
                    "Missing Minecraft directory recreated in place during startup. MinecraftDirectory={MinecraftDirectory}",
                    updatedSettings.MinecraftDirectory);
            }

            return (updatedSettings, recovery);
        }
        catch (MinecraftDirectoryStartupRecoveryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MinecraftDirectoryStartupRecoveryException(
                recovery?.SelectedDirectory ?? pathProvider.DefaultMinecraftDirectory,
                "The recovered Minecraft directory could not be saved.",
                exception);
        }
    }

    /// <summary>
    /// 渲染环境快照中的内存信息只用于诊断；读取失败时省略该字段，不影响启动。
    /// </summary>
    private static SystemMemorySnapshot? TryReadSystemMemorySnapshot(IServiceProvider serviceProvider)
    {
        try
        {
            return serviceProvider.GetRequiredService<ISystemMemoryService>().GetSnapshot();
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Failed to read the system memory snapshot for the render environment log.");
            return null;
        }
    }

    private static async Task CleanupConfirmedUpdateCacheAsync(
        LauncherUpdateCacheCleaner cacheCleaner,
        string updaterPath)
    {
        try
        {
            await cacheCleaner.CleanupConfirmedUpdateAsync(updaterPath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Confirmed launcher update cache cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupPendingInstanceDeletionsOnStartupAsync(
        IInstanceDeletionCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance deletion cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupPendingInstanceInstallsOnStartupAsync(
        IInstanceInstallCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance installation cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task CleanupModpackSandboxesOnStartupAsync(
        IModpackSandboxCleanupService cleanupService)
    {
        try
        {
            await cleanupService.CleanupStaleAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Modpack loader sandbox cleanup failed; startup cleanup will retry later.");
        }
    }

    private static async Task RecoverPendingInstanceRenamesOnStartupAsync(
        IInstanceRenameRecoveryService recoveryService)
    {
        try
        {
            await recoveryService.RecoverPendingAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance rename recovery failed before instance scanning.");
        }
    }

    private static async Task RecoverPendingInstanceBackupsOnStartupAsync(
        IInstanceBackupService backupService,
        string minecraftDirectory)
    {
        try
        {
            await backupService.RecoverPendingRestoresAsync(minecraftDirectory).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pending instance backup recovery failed before instance scanning.");
        }
    }

    private static async Task CheckForLauncherUpdatesAfterAgreementAsync(MainViewModel mainViewModel)
    {
        try
        {
            if (!await mainViewModel.WaitForUserAgreementDecisionAsync())
                return;

            await mainViewModel.SettingsPage.Info.CheckUpdatesOnStartupAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unexpected failure while running startup launcher update check.");
        }
    }

    private static void ApplyLauncherCulture(string? language)
    {
        var normalizedLanguage = LauncherLanguages.Normalize(language);
        var culture = CultureInfo.GetCultureInfo(normalizedLanguage);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (isUpdateApplyMode)
        {
            base.OnExit(e);
            return;
        }

        try
        {
            Log.Information("Launcher exit started. ExitCode={ExitCode}", e.ApplicationExitCode);
            serviceProvider?.Dispose();
            LauncherLogConfiguration.PruneOldLogFiles(
                LauncherLogConfiguration.ResolveLogDirectory(),
                DateTimeOffset.Now);
            Log.Information("Launcher exit completed.");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private async Task CleanupModpackWorkspacesOnStartupAsync()
    {
        if (serviceProvider is null)
            return;

        try
        {
            await serviceProvider.GetRequiredService<IModpackWorkspaceCleanupService>()
                .CleanupAllAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to clean modpack workspace cache on startup.");
        }
    }

    private async Task CleanupResourceProjectWorkspacesOnStartupAsync()
    {
        if (serviceProvider is null)
            return;

        try
        {
            await serviceProvider.GetRequiredService<IResourceProjectInstallationService>()
                .CleanupStaleWorkspacesAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to clean resource project installation workspaces on startup.");
        }
    }

    private void RegisterUnhandledExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled dispatcher exception.");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Log.Fatal(exception, "Unhandled app domain exception. IsTerminating={IsTerminating}", args.IsTerminating);
            else
                Log.Fatal("Unhandled app domain exception object. IsTerminating={IsTerminating}", args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
        };
    }
}
