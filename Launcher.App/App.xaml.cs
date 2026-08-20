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

            var startupSettings = await serviceProvider.GetRequiredService<ISettingsService>().LoadAsync();
            logLevelController.SetDiagnosticLoggingEnabled(startupSettings.EnableDiagnosticLogging);
            ApplyLauncherCulture(startupSettings.LauncherLanguage);
            Log.Debug("Launcher culture initialized. Language={Language}", CultureInfo.CurrentUICulture.Name);
            base.OnStartup(e);

            await CleanupModpackWorkspacesOnStartupAsync();
            await CleanupResourceProjectWorkspacesOnStartupAsync();

            await RecoverPendingInstanceBackupsOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceBackupService>(),
                startupSettings.MinecraftDirectory);
            await RecoverPendingInstanceRenamesOnStartupAsync(
                serviceProvider.GetRequiredService<IInstanceRenameRecoveryService>());

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            await mainViewModel.PrimeAsync(startupSettings);
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
        catch (Exception exception)
        {
            Log.Fatal(exception, "Launcher startup failed.");
            Shutdown(-1);
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
