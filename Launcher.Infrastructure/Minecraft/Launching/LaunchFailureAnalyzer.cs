/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.RegularExpressions;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal static partial class LaunchFailureAnalyzer
{
    private const int MaxEvidenceCount = 24;
    private const int MaxEvidenceLineLength = 800;
    private const int MaxEvidenceTotalLength = 6000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex FabricDependencyPattern = new(
        @"Mod\s+'(?<mod>[^']+)'\s*(?:\([^)]+\))?\s*(?<modVersion>\S+)?\s+requires\s+(?<required>.+?)\s+of\s+(?:mod\s+)?(?:'(?<dependencyQuoted>[^']+)'(?:\s+\([^)]+\))?|(?<dependencyBare>[A-Za-z0-9_.-]+)),\s+(?:(?<missing>which\s+is\s+missing)|but\s+only\s+the\s+wrong\s+version\s+is\s+present:\s*(?<current>.+?))!?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ForgeFailureLinePattern = new(
        @"^(?:Failure\s+message:\s*)?(?:Mod\s+)?(?<mod>[A-Za-z0-9_.-]+)\s+(?:requires|(?:only\s+)?supports)\s+(?<dependency>[A-Za-z0-9_.-]+)\s+(?<required>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ForgeCurrentLinePattern = new(
        @"^Currently,\s*(?<dependency>[A-Za-z0-9_.-]+)\s+is\s+(?<current>[^|]+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex JavaRequirementPattern = new(
        @"Mod\s+'(?<mod>[^']+)'[^\r\n]*?requires\s+version\s+(?<required>\d+)\s+or\s+later\s+of\s+(?:'[^'\r\n]+'\s*)?\(java\),\s+but\s+only\s+the\s+wrong\s+version\s+is\s+present:\s*(?<current>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex JavaReplacementPattern = new(
        @"Replace\s+'[^']*'\s+\(java\)\s+(?<current>\d+)\s+with\s+version\s+(?<required>\d+)\s+or\s+later",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex SimpleMissingDependencyPattern = new(
        @"Mod\s+'(?<mod>[^']+)'[^\r\n]*?(?:requires|depends on)[^\r\n]*?'(?<dependency>[^']+)'[^\r\n]*?(?:missing|not installed|not found)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex MissingClasspathPattern = new(
        @"Class path entries reference missing files:\s*(?<path>.+?)(?:\s+-\s+the game|\r|\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex DependencyKindSeparatorPattern = new(
        @"[\s_-]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex RequirementVersionPrefixPattern = new(
        @"^version\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex AnyVersionPattern = new(
        @"^any\s+(.+?)\s+version$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex StackFrameTailPattern = new(
        @"^\.\.\.\s+\d+\s+more$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    public static LaunchFailureAnalysis? Analyze(
        LaunchDiagnosticContext context,
        string stdout,
        string stderr,
        string latestLogTail,
        IReadOnlyList<string> crashPreviews)
    {
        var crashPreview = string.Join(
            Environment.NewLine,
            crashPreviews.Take(2));

        var currentProcessText = string.Join(
            Environment.NewLine,
            stdout,
            stderr,
            crashPreview);

        var currentProcessAnalysis = AnalyzeText(context, currentProcessText);
        var latestLogAnalysis = AnalyzeText(context, latestLogTail);
        return SelectBestAnalysis(currentProcessAnalysis, latestLogAnalysis);
    }

    public static LaunchFailureAnalysis? AnalyzeException(
        LaunchDiagnosticContext context,
        Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not InstanceRepairException { FileFailure: { } failure } repairException)
                continue;
            if (failure.Reason is GameFileRepairFailureReason.None or GameFileRepairFailureReason.Canceled)
                return null;

            return new LaunchFailureAnalysis(
                LaunchFailureCategory.GameFileIntegrity,
                "Game file integrity check failed",
                failure.Reason.ToString(),
                "Follow the recovery guidance shown by the launcher.",
                GameFileFailureReason: failure.Reason,
                AffectedPath: GetSafeAffectedPath(context.MinecraftDirectory, failure.TargetPath),
                AutoRepairEnabled: repairException.AutoRepairEnabled);
        }

        return null;
    }

    private static string? GetSafeAffectedPath(string minecraftDirectory, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return null;

        try
        {
            if (!Path.IsPathRooted(targetPath))
                return NormalizeDisplayPath(targetPath);

            var root = Path.GetFullPath(minecraftDirectory);
            var fullTarget = Path.GetFullPath(targetPath);
            var relative = Path.GetRelativePath(root, fullTarget);
            if (!Path.IsPathRooted(relative)
                && !string.Equals(relative, "..", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return NormalizeDisplayPath(relative);
            }

            return Path.GetFileName(Path.TrimEndingDirectorySeparator(fullTarget));
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            try
            {
                return Path.GetFileName(Path.TrimEndingDirectorySeparator(targetPath));
            }
            catch
            {
                return null;
            }
        }
    }

    private static string NormalizeDisplayPath(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static LaunchFailureAnalysis? AnalyzeText(LaunchDiagnosticContext context, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return TryAnalyze(() => AnalyzeMissingClientJar(context, text))
               ?? TryAnalyze(() => AnalyzeMissingMinecraftGameProvider(context, text))
               ?? TryAnalyze(() => AnalyzeJavaVersionMismatch(context, text))
               ?? TryAnalyze(() => AnalyzeModCompatibility(context, text))
               ?? TryAnalyze(() => AnalyzeMissingFiles(context, text))
               ?? TryAnalyze(() => AnalyzeOutOfMemory(context, text));
    }

    private static LaunchFailureAnalysis? SelectBestAnalysis(
        LaunchFailureAnalysis? currentProcessAnalysis,
        LaunchFailureAnalysis? latestLogAnalysis)
    {
        if (currentProcessAnalysis is null)
            return latestLogAnalysis;
        if (latestLogAnalysis is null)
            return currentProcessAnalysis;

        if (currentProcessAnalysis.Category == latestLogAnalysis.Category)
        {
            var preferred = IsGenericAnalysis(currentProcessAnalysis)
                && !IsGenericAnalysis(latestLogAnalysis)
                ? latestLogAnalysis
                : currentProcessAnalysis;
            var secondary = ReferenceEquals(preferred, currentProcessAnalysis)
                ? latestLogAnalysis
                : currentProcessAnalysis;
            return MergeAnalyses(preferred, secondary);
        }

        return IsGenericAnalysis(currentProcessAnalysis) && !IsGenericAnalysis(latestLogAnalysis)
            ? latestLogAnalysis
            : currentProcessAnalysis;
    }

    private static LaunchFailureAnalysis MergeAnalyses(
        LaunchFailureAnalysis preferred,
        LaunchFailureAnalysis secondary)
    {
        var details = preferred.Details
            .Concat(secondary.Details)
            .DistinctBy(GetDetailKey)
            .ToArray();
        var evidence = new List<LaunchFailureEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalLength = 0;
        foreach (var item in preferred.Evidence.Concat(secondary.Evidence))
        {
            if (!seen.Add($"{item.Kind}:{item.Text}"))
                continue;
            if (evidence.Count >= MaxEvidenceCount || totalLength + item.Text.Length > MaxEvidenceTotalLength)
                continue;

            evidence.Add(item);
            totalLength += item.Text.Length;
        }

        return preferred with
        {
            ModName = preferred.ModName ?? secondary.ModName,
            DependencyName = preferred.DependencyName ?? secondary.DependencyName,
            Details = details,
            Evidence = evidence
        };
    }

    private static bool IsGenericAnalysis(LaunchFailureAnalysis analysis)
    {
        if (analysis.Details.Count > 0)
            return false;
        if (analysis.Evidence.Count == 0)
            return true;

        return analysis.Category == LaunchFailureCategory.ModVersionIncompatible
               && analysis.Evidence.All(evidence => IsGenericCompatibilityMarker(evidence.Text));
    }

    private static bool IsGenericCompatibilityMarker(string text)
    {
        var normalized = TrimBullet(text).TrimEnd('!', '.').Trim();
        return normalized.EndsWith("incompatible mods found", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("incompatible mod set", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("mod resolution failed", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("mod loading has failed", StringComparison.OrdinalIgnoreCase);
    }

    private static LaunchFailureAnalysis? TryAnalyze(Func<LaunchFailureAnalysis?> analyzer)
    {
        try
        {
            return analyzer();
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
