/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class SmoothScrollBehaviorTests
{
    [Fact]
    public void DefaultWheelAnimationDurationIs130Milliseconds()
    {
        var defaultValue = SmoothScrollBehavior
            .WheelAnimationDurationMillisecondsProperty
            .DefaultMetadata
            .DefaultValue;

        Assert.Equal(130d, Assert.IsType<double>(defaultValue));
    }

    [Fact]
    public void EveryXamlOverrideUses130Milliseconds()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "Launcher.App");
        var overrides = Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(FindDurationOverrides)
            .ToArray();

        Assert.NotEmpty(overrides);
        Assert.All(overrides, item => Assert.Equal("130", item.Value));
    }

    private static IEnumerable<(string File, string Value)> FindDurationOverrides(string filePath)
    {
        var document = XDocument.Load(filePath);

        foreach (var attribute in document.Descendants().Attributes()
                     .Where(attribute => attribute.Name.LocalName == "WheelAnimationDurationMilliseconds"))
        {
            yield return (filePath, attribute.Value);
        }

        foreach (var setter in document.Descendants().Where(element => element.Name.LocalName == "Setter"))
        {
            var property = setter.Attribute("Property")?.Value;
            if (property?.EndsWith("WheelAnimationDurationMilliseconds", StringComparison.Ordinal) is not true)
                continue;

            var value = setter.Attribute("Value")?.Value;
            if (value is not null)
                yield return (filePath, value);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Launcher.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

}
