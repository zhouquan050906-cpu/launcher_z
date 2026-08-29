/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal readonly record struct JavaVersionNumber(
    int Major,
    int Minor = 0,
    int Patch = 0,
    int Build = 0) : IComparable<JavaVersionNumber>
{
    public int CompareTo(JavaVersionNumber other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
            return comparison;

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
            return comparison;

        comparison = Patch.CompareTo(other.Patch);
        return comparison != 0 ? comparison : Build.CompareTo(other.Build);
    }

    public int CompareCompatibilityTo(JavaVersionNumber other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
            return comparison;

        comparison = Minor.CompareTo(other.Minor);
        return comparison != 0 ? comparison : Patch.CompareTo(other.Patch);
    }

    public static bool TryParse(string? value, out JavaVersionNumber version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().Trim('"');
        var build = 0;
        var buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0)
        {
            var buildText = normalized[(buildSeparator + 1)..];
            _ = int.TryParse(
                new string(buildText.TakeWhile(char.IsDigit).ToArray()),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out build);
            normalized = normalized[..buildSeparator];
        }

        var qualifierSeparator = normalized.IndexOf('-');
        if (qualifierSeparator >= 0)
            normalized = normalized[..qualifierSeparator];

        if (normalized.StartsWith("1.", StringComparison.Ordinal))
        {
            var updateSeparator = normalized.IndexOf('_');
            var update = 0;
            if (updateSeparator >= 0)
            {
                var updateText = normalized[(updateSeparator + 1)..];
                if (!int.TryParse(
                        new string(updateText.TakeWhile(char.IsDigit).ToArray()),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out update))
                {
                    return false;
                }

                normalized = normalized[..updateSeparator];
            }

            var legacyParts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (legacyParts.Length < 2
                || !int.TryParse(legacyParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var legacyMajor))
            {
                return false;
            }

            version = new JavaVersionNumber(legacyMajor, 0, update, build);
            return true;
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return false;
        }

        var minor = 0;
        var patch = 0;
        if (parts.Length >= 2
            && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            return false;
        }

        if (parts.Length >= 3
            && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new JavaVersionNumber(major, minor, patch, build);
        return true;
    }

    public override string ToString() => Build > 0
        ? $"{Major}.{Minor}.{Patch}+{Build}"
        : $"{Major}.{Minor}.{Patch}";
}

internal readonly record struct JavaVersionBound(JavaVersionNumber Version, bool Inclusive);

internal sealed record JavaRuntimeCompatibilityRequirement(
    int? RecommendedMajorVersion,
    JavaVersionBound? Minimum,
    JavaVersionBound? Maximum)
{
    public bool IsConflicting
    {
        get
        {
            if (Minimum is not JavaVersionBound minimum || Maximum is not JavaVersionBound maximum)
                return false;

            var comparison = minimum.Version.CompareCompatibilityTo(maximum.Version);
            return comparison > 0
                   || comparison == 0 && (!minimum.Inclusive || !maximum.Inclusive);
        }
    }

    public bool HasKnownRequirement => RecommendedMajorVersion is not null || Minimum is not null || Maximum is not null;

    public JavaRuntimeCompatibilityRequirement IntersectMinimum(JavaVersionNumber version, bool inclusive = true)
    {
        var candidate = new JavaVersionBound(version, inclusive);
        if (Minimum is not JavaVersionBound current)
            return this with { Minimum = candidate };

        var comparison = candidate.Version.CompareCompatibilityTo(current.Version);
        if (comparison > 0)
            return this with { Minimum = candidate };

        if (comparison < 0)
            return this;

        return this with { Minimum = new JavaVersionBound(current.Version, current.Inclusive && candidate.Inclusive) };
    }

    public JavaRuntimeCompatibilityRequirement IntersectMaximum(JavaVersionNumber version, bool inclusive)
    {
        var candidate = new JavaVersionBound(version, inclusive);
        if (Maximum is not JavaVersionBound current)
            return this with { Maximum = candidate };

        var comparison = candidate.Version.CompareCompatibilityTo(current.Version);
        if (comparison < 0)
            return this with { Maximum = candidate };

        if (comparison > 0)
            return this;

        return this with { Maximum = new JavaVersionBound(current.Version, current.Inclusive && candidate.Inclusive) };
    }

    public bool IsCompatible(JavaRuntimeInfo runtime)
    {
        if (!HasKnownRequirement)
            return true;

        if (IsConflicting || runtime.MajorVersion is not int majorVersion)
            return false;

        if (JavaVersionNumber.TryParse(runtime.Version, out var parsedVersion))
            return IsCompatible(parsedVersion);

        return IsCompatibleWithMajorOnly(majorVersion);
    }

    public string GetIncompatibilityReason(JavaRuntimeInfo runtime)
    {
        if (IsConflicting)
            return "requirement-conflict";

        if (runtime.MajorVersion is not int majorVersion)
            return "unknown-major-version";

        if (!JavaVersionNumber.TryParse(runtime.Version, out var parsedVersion))
            return IsCompatibleWithMajorOnly(majorVersion) ? "compatible" : "unverifiable-or-outside-range";

        if (Minimum is JavaVersionBound minimum)
        {
            var comparison = parsedVersion.CompareCompatibilityTo(minimum.Version);
            if (comparison < 0 || comparison == 0 && !minimum.Inclusive)
                return "below-minimum";
        }

        if (Maximum is JavaVersionBound maximum)
        {
            var comparison = parsedVersion.CompareCompatibilityTo(maximum.Version);
            if (comparison > 0 || comparison == 0 && !maximum.Inclusive)
                return "above-maximum";
        }

        return "compatible";
    }

    public override string ToString()
    {
        var minimum = Minimum is JavaVersionBound min
            ? $"{(min.Inclusive ? '[' : '(')}{min.Version}"
            : "(-inf";
        var maximum = Maximum is JavaVersionBound max
            ? $"{max.Version}{(max.Inclusive ? ']' : ')')}"
            : "+inf)";
        return $"{minimum}, {maximum}; recommended={RecommendedMajorVersion?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    }

    private bool IsCompatible(JavaVersionNumber version)
    {
        if (Minimum is JavaVersionBound minimum)
        {
            var comparison = version.CompareCompatibilityTo(minimum.Version);
            if (comparison < 0 || comparison == 0 && !minimum.Inclusive)
                return false;
        }

        if (Maximum is JavaVersionBound maximum)
        {
            var comparison = version.CompareCompatibilityTo(maximum.Version);
            if (comparison > 0 || comparison == 0 && !maximum.Inclusive)
                return false;
        }

        return true;
    }

    private bool IsCompatibleWithMajorOnly(int majorVersion)
    {
        if (Minimum is JavaVersionBound minimum)
        {
            if (majorVersion < minimum.Version.Major)
                return false;

            if (majorVersion == minimum.Version.Major
                && (minimum.Version.Minor != 0 || minimum.Version.Patch != 0 || !minimum.Inclusive))
            {
                return false;
            }
        }

        if (Maximum is JavaVersionBound maximum)
        {
            if (majorVersion > maximum.Version.Major)
                return false;

            if (majorVersion == maximum.Version.Major)
            {
                if (maximum.Version.Minor != 0 || maximum.Version.Patch != 0)
                    return false;

                if (!maximum.Inclusive)
                    return false;
            }
        }

        return true;
    }
}

internal static class JavaRuntimeCompatibilityResolver
{
    private static readonly MinecraftVersionNumber LegacyForgeMinimum = new(1, 6, 1);
    private static readonly MinecraftVersionNumber LegacyForgeMaximum = new(1, 7, 2);

    public static JavaRuntimeCompatibilityRequirement Resolve(
        string? minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        int? metadataMajorVersion)
    {
        var recommendedMajorVersion = metadataMajorVersion
            ?? JavaRuntimeSelectionService.GuessRequiredMajorVersion(minecraftVersion);

        if (metadataMajorVersion is null
            && loader is LoaderKind.Forge
            && IsMinecraftBetween(minecraftVersion, LegacyForgeMinimum, LegacyForgeMaximum))
        {
            recommendedMajorVersion = 7;
        }

        var requirement = new JavaRuntimeCompatibilityRequirement(
            recommendedMajorVersion,
            recommendedMajorVersion is int required
                ? new JavaVersionBound(new JavaVersionNumber(required), true)
                : null,
            null);

        return loader switch
        {
            LoaderKind.Forge => ApplyForgeConstraints(requirement, minecraftVersion, loaderVersion),
            LoaderKind.NeoForge => ApplyNeoForgeConstraints(requirement, minecraftVersion, loaderVersion),
            _ => requirement
        };
    }

    private static JavaRuntimeCompatibilityRequirement ApplyForgeConstraints(
        JavaRuntimeCompatibilityRequirement requirement,
        string? minecraftVersion,
        string? forgeVersion)
    {
        if (IsMinecraftBetween(minecraftVersion, LegacyForgeMinimum, LegacyForgeMaximum))
            return requirement.IntersectMaximum(new JavaVersionNumber(8), inclusive: false);

        if (MinecraftVersionNumber.TryParse(minecraftVersion, out var gameVersion))
        {
            if (gameVersion.Major == 1 && gameVersion.Minor <= 12)
                return requirement.IntersectMaximum(new JavaVersionNumber(9), inclusive: false);

            if (gameVersion.Major == 1 && gameVersion.Minor is 13 or 14)
                return requirement.IntersectMaximum(new JavaVersionNumber(11), inclusive: false);

            if (gameVersion.Major == 1 && gameVersion.Minor == 15)
                return requirement.IntersectMaximum(new JavaVersionNumber(16), inclusive: false);
        }

        if (!LoaderVersionNumber.TryParse(forgeVersion, out var parsedForgeVersion))
            return requirement;

        if (parsedForgeVersion.IsBetweenInclusive("34.0.0", "36.2.25"))
            return requirement.IntersectMaximum(new JavaVersionNumber(8, 0, 320), inclusive: true);

        if (parsedForgeVersion.IsBetweenInclusive("36.2.26", "36.999999.999999"))
            return requirement.IntersectMaximum(new JavaVersionNumber(24), inclusive: false);

        if (parsedForgeVersion.IsBetweenInclusive("37.0.0", "37.0.79"))
            return requirement.IntersectMaximum(new JavaVersionNumber(17), inclusive: false);

        if (parsedForgeVersion.IsBetweenInclusive("45.0.21", "45.0.65"))
            return requirement.IntersectMaximum(new JavaVersionNumber(20), inclusive: false);

        if (parsedForgeVersion.IsBetweenInclusive("45.0.66", "47.4.8"))
            return requirement.IntersectMaximum(new JavaVersionNumber(22), inclusive: false);

        return requirement;
    }

    private static JavaRuntimeCompatibilityRequirement ApplyNeoForgeConstraints(
        JavaRuntimeCompatibilityRequirement requirement,
        string? minecraftVersion,
        string? neoForgeVersion)
    {
        if (MinecraftVersionNumber.TryParse(minecraftVersion, out var gameVersion)
            && gameVersion == new MinecraftVersionNumber(1, 20, 1))
        {
            return requirement.IntersectMaximum(new JavaVersionNumber(22), inclusive: false);
        }

        if (!string.IsNullOrWhiteSpace(neoForgeVersion)
            && !neoForgeVersion.Contains("25w14craftmine", StringComparison.OrdinalIgnoreCase)
            && LoaderVersionNumber.TryParse(neoForgeVersion, out var parsedNeoForgeVersion)
            && parsedNeoForgeVersion.CompareTo(LoaderVersionNumber.Parse("20.2.62-beta")) <= 0)
        {
            return requirement.IntersectMaximum(new JavaVersionNumber(22), inclusive: false);
        }

        return requirement;
    }

    private static bool IsMinecraftBetween(
        string? minecraftVersion,
        MinecraftVersionNumber minimum,
        MinecraftVersionNumber maximum) =>
        MinecraftVersionNumber.TryParse(minecraftVersion, out var parsed)
        && parsed.IsBetweenInclusive(minimum, maximum);

    private readonly record struct LoaderVersionNumber(int Major, int Minor, int Patch, string? Qualifier)
        : IComparable<LoaderVersionNumber>
    {
        public int CompareTo(LoaderVersionNumber other)
        {
            var comparison = Major.CompareTo(other.Major);
            if (comparison != 0)
                return comparison;

            comparison = Minor.CompareTo(other.Minor);
            if (comparison != 0)
                return comparison;

            comparison = Patch.CompareTo(other.Patch);
            if (comparison != 0)
                return comparison;

            if (Qualifier is null)
                return other.Qualifier is null ? 0 : 1;

            return other.Qualifier is null
                ? -1
                : StringComparer.OrdinalIgnoreCase.Compare(Qualifier, other.Qualifier);
        }

        public bool IsBetweenInclusive(string minimum, string maximum) =>
            CompareTo(Parse(minimum)) >= 0 && CompareTo(Parse(maximum)) <= 0;

        public static LoaderVersionNumber Parse(string value) =>
            TryParse(value, out var version)
                ? version
                : throw new FormatException($"Invalid loader version: {value}");

        public static bool TryParse(string? value, out LoaderVersionNumber version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            var qualifierSeparator = normalized.IndexOf('-');
            var qualifier = qualifierSeparator >= 0 ? normalized[(qualifierSeparator + 1)..] : null;
            var numericPart = qualifierSeparator >= 0 ? normalized[..qualifierSeparator] : normalized;
            var parts = numericPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 3)
                return false;

            var numbers = new int[3];
            for (var index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                    return false;
            }

            version = new LoaderVersionNumber(numbers[0], numbers[1], numbers[2], qualifier);
            return true;
        }
    }
}
