/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Home;
using Launcher.Application.Services;

namespace Launcher.Tests.ViewModels.Home;

public sealed class LaunchStatusDialogIntegrityFailureTests
{
    [Theory]
    [InlineData(GameFileRepairFailureReason.Missing)]
    [InlineData(GameFileRepairFailureReason.Corrupted)]
    [InlineData(GameFileRepairFailureReason.MetadataIncomplete)]
    [InlineData(GameFileRepairFailureReason.DownloadFailed)]
    [InlineData(GameFileRepairFailureReason.ProcessorRegenerationFailed)]
    [InlineData(GameFileRepairFailureReason.PublicationFailed)]
    [InlineData(GameFileRepairFailureReason.FinalLaunchPlanInvalid)]
    public void IntegrityFailureUsesDedicatedLocalizedContent(GameFileRepairFailureReason reason)
    {
        const string affectedPath = @"assets\indexes\32.json";
        var viewModel = CreateViewModel();

        viewModel.Show(CreateReport(reason, affectedPath, autoRepairEnabled: false));

        Assert.True(viewModel.HasAnalysis);
        Assert.Equal(Strings.Dialog_LaunchAnalysisGameFileIntegrityTitle, viewModel.AnalysisReasonTitle);
        Assert.Equal(GetExpectedDetail(reason, affectedPath), viewModel.AnalysisReasonDetail);
        Assert.Equal(GetExpectedRecommendation(reason, autoRepairEnabled: false), viewModel.AnalysisRecommendation);
        Assert.DoesNotContain(Strings.Dialog_LaunchStatusStartupFailedMessage, viewModel.Message);
    }

    [Theory]
    [InlineData(GameFileRepairFailureReason.Missing)]
    [InlineData(GameFileRepairFailureReason.Corrupted)]
    public void FailedAutoRepairDoesNotRecommendEnablingAutoRepair(GameFileRepairFailureReason reason)
    {
        var viewModel = CreateViewModel();

        viewModel.Show(CreateReport(reason, @"assets\indexes\32.json", autoRepairEnabled: true));

        Assert.Equal(
            Strings.Dialog_LaunchAnalysisGameFileIntegrityRepairFailedRecommendation,
            viewModel.AnalysisRecommendation);
        Assert.NotEqual(
            Strings.Dialog_LaunchAnalysisGameFileIntegrityEnableAutoRepairRecommendation,
            viewModel.AnalysisRecommendation);
    }

    private static LaunchStatusDialogViewModel CreateViewModel() => new(
        Stub<IInstanceFolderService>(),
        Stub<IFilePickerService>(),
        Stub<ILaunchDiagnosticExportService>(),
        Stub<IStatusService>());

    private static LaunchFailureReport CreateReport(
        GameFileRepairFailureReason reason,
        string affectedPath,
        bool autoRepairEnabled) => new(
        LaunchFailureKind.StartupFailed,
        "Test Instance",
        "1.20.1",
        ExitCode: null,
        DiagnosticPath: null,
        DiagnosticDirectory: null,
        Analysis: new LaunchFailureAnalysis(
            LaunchFailureCategory.GameFileIntegrity,
            "Game file integrity check failed",
            reason.ToString(),
            "Follow the recovery guidance shown by the launcher.",
            GameFileFailureReason: reason,
            AffectedPath: affectedPath,
            AutoRepairEnabled: autoRepairEnabled));

    private static string GetExpectedDetail(GameFileRepairFailureReason reason, string affectedPath) => reason switch
    {
        GameFileRepairFailureReason.Missing => string.Format(
            Strings.Dialog_LaunchAnalysisGameFileIntegrityMissingDetailFormat,
            affectedPath),
        GameFileRepairFailureReason.Corrupted => string.Format(
            Strings.Dialog_LaunchAnalysisGameFileIntegrityCorruptedDetailFormat,
            affectedPath),
        GameFileRepairFailureReason.MetadataIncomplete => Strings.Dialog_LaunchAnalysisGameFileIntegrityMetadataIncompleteDetail,
        GameFileRepairFailureReason.DownloadFailed => Strings.Dialog_LaunchAnalysisGameFileIntegrityDownloadFailedDetail,
        GameFileRepairFailureReason.ProcessorRegenerationFailed => Strings.Dialog_LaunchAnalysisGameFileIntegrityProcessorRegenerationFailedDetail,
        GameFileRepairFailureReason.PublicationFailed => Strings.Dialog_LaunchAnalysisGameFileIntegrityPublicationFailedDetail,
        GameFileRepairFailureReason.FinalLaunchPlanInvalid => string.Format(
            Strings.Dialog_LaunchAnalysisGameFileIntegrityFinalLaunchPlanInvalidDetailFormat,
            affectedPath),
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static string GetExpectedRecommendation(
        GameFileRepairFailureReason reason,
        bool autoRepairEnabled) => reason switch
    {
        GameFileRepairFailureReason.Missing or GameFileRepairFailureReason.Corrupted when !autoRepairEnabled =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityEnableAutoRepairRecommendation,
        GameFileRepairFailureReason.Missing or GameFileRepairFailureReason.Corrupted =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityRepairFailedRecommendation,
        GameFileRepairFailureReason.MetadataIncomplete =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityMetadataIncompleteRecommendation,
        GameFileRepairFailureReason.DownloadFailed =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityDownloadFailedRecommendation,
        GameFileRepairFailureReason.ProcessorRegenerationFailed =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityProcessorRegenerationFailedRecommendation,
        GameFileRepairFailureReason.PublicationFailed =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityPublicationFailedRecommendation,
        GameFileRepairFailureReason.FinalLaunchPlanInvalid =>
            Strings.Dialog_LaunchAnalysisGameFileIntegrityFinalLaunchPlanInvalidRecommendation,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
    };

    private static T Stub<T>() where T : class =>
        DispatchProxy.Create<T, DefaultInterfaceProxy>();

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType ?? typeof(void);
            if (returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var valueType = returnType.GetGenericArguments()[0];
                var value = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(valueType)
                    .Invoke(null, [value]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
