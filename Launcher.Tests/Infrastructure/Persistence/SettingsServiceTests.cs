/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Application.Services;
using Launcher.Infrastructure.Persistence;

namespace Launcher.Tests.Infrastructure.Persistence;

public sealed class SettingsServiceTests : TestTempDirectory
{
    [Fact]
    public async Task BootstrapPreferencesReadLanguageAndDiagnosticLoggingWithoutRewritingSettings()
    {
        Directory.CreateDirectory(TempRoot);
        var settingsPath = Path.Combine(TempRoot, "settings.json");
        var originalJson = """{"LauncherLanguage":"ja-JP","EnableDiagnosticLogging":true,"FutureSetting":true}""";
        await File.WriteAllTextAsync(settingsPath, originalJson);

        var preferences = new JsonSettingsService(TempRoot).LoadLauncherBootstrapPreferences();

        Assert.Equal("ja-JP", preferences.LauncherLanguage);
        Assert.True(preferences.EnableDiagnosticLogging);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(settingsPath));
    }

    [Fact]
    public async Task ConcurrentAtomicUpdatesPreserveAllIndependentFields()
    {
        var firstProcess = new JsonSettingsService(TempRoot);
        var secondProcess = new JsonSettingsService(TempRoot);
        _ = await firstProcess.LoadAsync();

        await Task.WhenAll(
            firstProcess.UpdateAsync(settings => settings.LauncherBackgroundEffect = LauncherBackgroundEffects.None),
            secondProcess.UpdateAsync(settings => settings.HasAcceptedUserAgreement = true));

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();
        Assert.Equal(LauncherBackgroundEffects.None, loaded.LauncherBackgroundEffect);
        Assert.True(loaded.HasAcceptedUserAgreement);
    }

    [Fact]
    public async Task InvalidWindowDimensionsAreNormalizedWhenSaved()
    {
        var service = new JsonSettingsService(TempRoot);
        var settings = await service.LoadAsync();
        settings.MainWindowWidth = double.NaN;
        settings.MainWindowHeight = 100d;

        await service.SaveAsync(settings);

        var loaded = await new JsonSettingsService(TempRoot).LoadAsync();
        Assert.Equal(LauncherDefaults.DefaultMainWindowWidth, loaded.MainWindowWidth);
        Assert.Equal(LauncherDefaults.MinimumMainWindowHeight, loaded.MainWindowHeight);
    }

}
