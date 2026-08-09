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

using System.Diagnostics;
using System.IO;
using System.Text;
using Launcher.Application;
using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 汇集启动异常、进程输出、日志与崩溃报告，生成经过脱敏和限长处理的诊断文件。
/// </summary>
internal static partial class LaunchDiagnosticsWriter
{
    // 诊断文件面向排障而非完整归档，所有外部文本进入前都必须脱敏并限制体积。
    private const int MaxDiagnosticLogFiles = 50;
    private const int MaxMatchedErrorLineLength = 800;
    private const string DiagnosticFilePattern = "launch-diagnostics-*.log";
    private const string CapturedOutputFilePattern = "launch-output-*.log";

    public static async Task<LaunchDiagnosticResult> WriteQuickExitDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        int exitCode,
        TimeSpan runtime,
        DateTimeOffset createdAt,
        ProcessStartInfo? startInfo,
        IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates,
        string stdout,
        string stderr,
        bool capturedOutputTruncated,
        CancellationToken cancellationToken)
    {
        // 快速退出通常没有异常对象，主要依靠退出码、最新日志和崩溃文件推断原因。
        var latestLogPath = diagnosticCandidates
            .FirstOrDefault(candidate => candidate.Type == LaunchDiagnosticType.MinecraftLatestLog)
            ?.Path;
        var latestLogTail = await BoundedDiagnosticFileReader
            .ReadTailAsync(latestLogPath, cancellationToken)
            .ConfigureAwait(false);
        var crashFiles = diagnosticCandidates
            .Where(candidate => candidate.Type is LaunchDiagnosticType.MinecraftCrashReport
                or LaunchDiagnosticType.JvmCrashReport)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var crashPreviews = await ReadCrashPreviewsAsync(crashFiles, cancellationToken).ConfigureAwait(false);
        var matchedErrorLines = FindMatchedErrorLines(stdout, stderr, latestLogTail, crashPreviews);
        var failureSummary = matchedErrorLines.FirstOrDefault() ?? $"Minecraft exited with code {exitCode}.";
        var analysis = LaunchFailureAnalyzer.Analyze(
            context,
            stdout,
            stderr,
            latestLogTail,
            crashPreviews.Select(preview => preview.Text).ToArray());

        var diagnosticWrite = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            diagnosticCandidates,
            cancellationToken,
            builder =>
            {
                builder.AppendLine($"ExitCode: {exitCode}");
                builder.AppendLine($"Runtime: {runtime}");
                builder.AppendLine($"CapturedOutputTruncated: {capturedOutputTruncated}");
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendFileSection(builder, "NewCrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashPreviews, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdOut", RedactSensitiveText(LimitTail(stdout, 120), context.SensitiveValues));
                AppendTextSection(builder, "StdErr", RedactSensitiveText(LimitTail(stderr, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(
            diagnosticWrite.Path,
            analysis,
            LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues),
            diagnosticWrite.Candidates);
    }

    public static async Task<LaunchDiagnosticResult> WriteExceptionDiagnosticAsync(
        LaunchDiagnosticContext context,
        string failureKind,
        string failureSummary,
        Exception exception,
        ProcessStartInfo? startInfo,
        DateTimeOffset createdAt,
        IReadOnlyList<LaunchDiagnosticReference> diagnosticCandidates,
        CancellationToken cancellationToken)
    {
        // 异常链和下载诊断是结构化证据，仍与进程日志合并到同一份用户可分享文件中。
        var latestLogPath = diagnosticCandidates
            .FirstOrDefault(candidate => candidate.Type == LaunchDiagnosticType.MinecraftLatestLog)
            ?.Path;
        var latestLogTail = await BoundedDiagnosticFileReader
            .ReadTailAsync(latestLogPath, cancellationToken)
            .ConfigureAwait(false);
        var crashFiles = diagnosticCandidates
            .Where(candidate => candidate.Type is LaunchDiagnosticType.MinecraftCrashReport
                or LaunchDiagnosticType.JvmCrashReport)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var crashPreviews = await ReadCrashPreviewsAsync(crashFiles, cancellationToken).ConfigureAwait(false);
        var matchedErrorLines = FindMatchedErrorLines(string.Empty, string.Empty, latestLogTail, crashPreviews);
        var exceptionText = FormatExceptionChain(exception);
        var analysis = LaunchFailureAnalyzer.AnalyzeException(context, exception)
            ?? LaunchFailureAnalyzer.Analyze(
                context,
                string.Empty,
                exceptionText,
                latestLogTail,
                crashPreviews.Select(preview => preview.Text).ToArray());

        var diagnosticWrite = await WriteDiagnosticAsync(
            context,
            failureKind,
            failureSummary,
            analysis,
            createdAt,
            diagnosticCandidates,
            cancellationToken,
            builder =>
            {
                AppendProcessSection(builder, startInfo, context.SensitiveValues);
                AppendDownloadSection(builder, FindDownloadDiagnostic(exception), context.SensitiveValues);
                AppendTextSection(builder, "ExceptionChain", RedactSensitiveText(exceptionText, context.SensitiveValues));
                AppendFileSection(builder, "CrashFiles", crashFiles);
                AppendCrashPreviewSection(builder, crashPreviews, context.SensitiveValues);
                AppendTextSection(builder, "MatchedErrorLines", RedactSensitiveLines(matchedErrorLines, context.SensitiveValues));
                AppendTextSection(builder, "LatestLogTail", RedactSensitiveText(LimitTail(latestLogTail, 120), context.SensitiveValues));
            });

        return new LaunchDiagnosticResult(
            diagnosticWrite.Path,
            analysis,
            LaunchDiagnosticRedactor.Redact(failureSummary, context.SensitiveValues),
            diagnosticWrite.Candidates);
    }
}
