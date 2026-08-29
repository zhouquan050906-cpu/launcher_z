/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// Minecraft 正式版版本号，例如 <c>1.7.10</c>。
/// </summary>
/// <remarks>
/// 预发布和候选版形如 <c>1.11-pre1</c>、<c>1.20 Pre-Release 1</c>，版本号都在第一段里，
/// 后缀不影响主次修订版本，因此解析时直接丢弃。快照版本号（<c>16w32a</c>）不是这个格式，
/// 需要 <see cref="MinecraftSnapshotNumber"/>。
/// </remarks>
internal readonly record struct MinecraftVersionNumber(
    int Major,
    int Minor = 0,
    int Patch = 0) : IComparable<MinecraftVersionNumber>
{
    public int CompareTo(MinecraftVersionNumber other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
            return comparison;

        comparison = Minor.CompareTo(other.Minor);
        return comparison != 0 ? comparison : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(MinecraftVersionNumber left, MinecraftVersionNumber right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(MinecraftVersionNumber left, MinecraftVersionNumber right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(MinecraftVersionNumber left, MinecraftVersionNumber right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(MinecraftVersionNumber left, MinecraftVersionNumber right) =>
        left.CompareTo(right) >= 0;

    public bool IsBetweenInclusive(MinecraftVersionNumber minimum, MinecraftVersionNumber maximum) =>
        CompareTo(minimum) >= 0 && CompareTo(maximum) <= 0;

    public static bool TryParse(string? value, out MinecraftVersionNumber version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var numericPart = value.Split(['-', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = numericPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !VersionNumberText.TryParseNumber(parts[0], out var major)
            || !VersionNumberText.TryParseNumber(parts[1], out var minor))
        {
            return false;
        }

        var patch = 0;
        if (parts.Length >= 3)
            _ = VersionNumberText.TryParseNumber(parts[2], out patch);

        version = new MinecraftVersionNumber(major, minor, patch);
        return true;
    }
}

/// <summary>
/// Minecraft 快照版本号，例如 <c>16w32a</c>：两位年份、<c>w</c>、两位周数、一位修订字母。
/// </summary>
internal readonly record struct MinecraftSnapshotNumber(int Year, int Week)
    : IComparable<MinecraftSnapshotNumber>
{
    private const int VersionLength = 6;

    public int CompareTo(MinecraftSnapshotNumber other)
    {
        var comparison = Year.CompareTo(other.Year);
        return comparison != 0 ? comparison : Week.CompareTo(other.Week);
    }

    public static bool operator <(MinecraftSnapshotNumber left, MinecraftSnapshotNumber right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(MinecraftSnapshotNumber left, MinecraftSnapshotNumber right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(MinecraftSnapshotNumber left, MinecraftSnapshotNumber right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(MinecraftSnapshotNumber left, MinecraftSnapshotNumber right) =>
        left.CompareTo(right) >= 0;

    public static bool TryParse(string? value, out MinecraftSnapshotNumber snapshot)
    {
        snapshot = default;

        var normalized = value?.Trim();
        if (normalized is not { Length: VersionLength }
            || (normalized[2] is not 'w' and not 'W')
            || !char.IsAsciiLetter(normalized[5]))
        {
            return false;
        }

        if (!VersionNumberText.TryParseNumber(normalized.AsSpan(0, 2), out var year)
            || !VersionNumberText.TryParseNumber(normalized.AsSpan(3, 2), out var week))
        {
            return false;
        }

        snapshot = new MinecraftSnapshotNumber(year, week);
        return true;
    }
}

file static class VersionNumberText
{
    public static bool TryParseNumber(ReadOnlySpan<char> value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
}
