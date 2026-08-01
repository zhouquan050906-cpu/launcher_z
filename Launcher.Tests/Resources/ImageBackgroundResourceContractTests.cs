/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ImageBackgroundResourceContractTests
{
    [Fact]
    public void ImageLayersSeparateAlwaysOnTintFromOptionalControlBlur()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        var imageBlur = LoadTheme("Backgrounds", "ImageBlur.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var layer in new[] { image, imageBlur })
        {
            Assert.Empty(layer.Descendants().Where(element =>
                element.Name.LocalName == "ResourceDictionary"
                && element.Attribute("Source") is not null));
        }

        var blurKeys = new[]
        {
            "Is.SecondaryMenu.BackdropBlur.Enabled",
            "Is.Surface.BackdropBlur.Enabled"
        };
        foreach (var key in blurKeys)
        {
            Assert.DoesNotContain(image.Descendants(), element =>
                element.Attribute(xaml + "Key")?.Value == key);
            var value = Assert.Single(imageBlur.Descendants().Where(element =>
                element.Name.LocalName == "Boolean"
                && element.Attribute(xaml + "Key")?.Value == key));
            Assert.Equal("True", value.Value);
        }

        var imageTint = FindResource(
            image,
            xaml,
            "Boolean",
            "Is.ImageBackground.ControlTint.Enabled");
        Assert.Equal("True", imageTint.Value);

        var shared = LoadTheme("Shared.xaml");
        foreach (var key in blurKeys.Append("Is.ImageBackground.ControlTint.Enabled"))
        {
            var value = Assert.Single(shared.Descendants().Where(element =>
                element.Name.LocalName == "Boolean"
                && element.Attribute(xaml + "Key")?.Value == key));
            Assert.Equal("False", value.Value);
        }
    }

    [Fact]
    public void ImageLayerPreservesNonHomeDimmingAndWindowBorderValues()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dimOpacity = FindResource(image, xaml, "Double", "Opacity.LauncherBackground.Image.NonHomeDim");
        var windowBorder = FindResource(image, xaml, "SolidColorBrush", "Brush.Surface.WindowBorder");
        var windowBorderThickness = FindResource(image, xaml, "Thickness", "Thickness.Surface.WindowBorder");

        Assert.DoesNotContain(image.Descendants(), element =>
            element.Attribute(xaml + "Key")?.Value == "Brush.LauncherBackground.Image.DimOverlay");
        Assert.Equal("0.40", dimOpacity.Value);
        Assert.Equal("Transparent", windowBorder.Attribute("Color")?.Value);
        Assert.Equal("0", windowBorderThickness.Value);
    }

    [Fact]
    public void ImageLayerRemovesSecondaryButtonContrastOverSharedSurfaceTint()
    {
        var image = LoadTheme("Backgrounds", "Image.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var buttonTint = FindResource(
            image,
            xaml,
            "SolidColorBrush",
            "Brush.Button.Secondary.Background");

        Assert.Equal(
            "Transparent",
            buttonTint.Attribute("Color")?.Value);
    }

    [Fact]
    public void ClosedComboBoxUsesTheSharedSurfaceBackdrop()
    {
        var inputs = LoadAppXaml("Styles", "ControlStyles.Inputs.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var toggleStyle = FindStyle(inputs, xaml, "LauncherComboBoxToggleButtonStyle");
        var root = Assert.Single(toggleStyle.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute(xaml + "Name")?.Value == "Root"));

        Assert.Equal(
            "True",
            root.Attributes().Single(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsApplied").Value);
        Assert.Equal(
            "{DynamicResource Is.Surface.BackdropBlur.Enabled}",
            root.Attributes().Single(attribute =>
                attribute.Name.LocalName == "BackdropBlurHost.IsBlurEnabled").Value);
    }


    private static readonly string[] HomeForegroundKeys =
    [
        "Brush.Home.Name.Foreground",
        "Brush.Home.GameSettings.Foreground",
        "Brush.Home.GameSettings.ForegroundHover",
        "Brush.Home.GameSettings.ForegroundPressed"
    ];

    private static XElement FindResource(
        XDocument document,
        XNamespace xaml,
        string elementName,
        string key) => Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == elementName
            && element.Attribute(xaml + "Key")?.Value == key));

    private static XElement FindStyle(XDocument document, XNamespace xaml, string key) =>
        FindResource(document, xaml, "Style", key);

    private static XDocument LoadTheme(params string[] pathParts) =>
        LoadAppXaml(["Resources", "Themes", .. pathParts]);

    private static XDocument LoadAppXaml(params string[] pathParts) =>
        XDocument.Load(Path.Combine([FindRepositoryRoot().FullName, "Launcher.App", .. pathParts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
