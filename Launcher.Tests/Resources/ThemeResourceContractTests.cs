/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ThemeResourceContractTests
{
    [Fact]
    public void DarkAndLightThemesExposeTheSameCoreResources()
    {
        var dark = LoadKeys("Dark.xaml");
        var light = LoadKeys("Light.xaml");

        Assert.Equal(dark.Order(), light.Order());
        Assert.Contains("Brush.Text.Primary", dark);
        Assert.Contains("Brush.Surface.Window", dark);
        Assert.Contains("Brush.Control.Border", dark);
        Assert.Contains("Color.Surface.Popup", dark);
        Assert.Contains("Color.Page.Background", dark);
    }

    [Fact]
    public void LightThemeUsesRequiredPageAndCardBackgroundColors()
    {
        var document = Load("Light.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var pageBackground = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Page.Background"));
        var cardSurface = Assert.Single(document.Descendants()
            .Where(element => element.Attribute(xaml + "Key")?.Value == "Color.Card.Surface"));

        Assert.Equal("#EEEEF0", pageBackground.Value);
        Assert.Equal("#B3FFFFFF", cardSurface.Value);
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void ThemeDefinesSharedCardSurfaceShadow(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shadow = Assert.Single(document.Descendants()
            .Where(element => element.Name.LocalName == "DropShadowEffect"
                && element.Attribute(xaml + "Key")?.Value == "Effect.Card.Surface"));

        Assert.Equal("17", shadow.Attribute("BlurRadius")?.Value);
        Assert.Equal("270", shadow.Attribute("Direction")?.Value);
        Assert.Equal("0.13", shadow.Attribute("Opacity")?.Value);
        Assert.Equal("0", shadow.Attribute("ShadowDepth")?.Value);
    }

    private static HashSet<string> LoadKeys(string fileName)
    {
        var document = Load(fileName);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Descendants()
            .Select(element => element.Attribute(xaml + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static XDocument Load(string fileName) =>
        XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Resources",
            "Themes",
            fileName));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
