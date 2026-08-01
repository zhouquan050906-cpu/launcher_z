/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Views.Shell;

public sealed class MainWindowBackgroundContractTests
{
    [Fact]
    public void WindowFrameUsesTheBackgroundModeSpecificBorderBrush()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        var windowFrame = Assert.Single(document.Root!.Elements().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("BorderBrush")?.Value ==
                "{DynamicResource Brush.Surface.WindowBorder}"));

        Assert.Equal(
            "{DynamicResource Brush.Surface.WindowBorder}",
            windowFrame.Attribute("BorderBrush")?.Value);
        Assert.Equal(
            "{DynamicResource Thickness.Surface.WindowBorder}",
            windowFrame.Attribute("BorderThickness")?.Value);
    }

    [Fact]
    public void BackgroundImageFillsWholeWindowAndOnlyHidesPageBackdropWhenActive()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));

        var imageSource = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ImageBackdropSource"
            && element.Attribute("ImageSource")?.Value == "{Binding LauncherBackground.ImageSource}"));
        Assert.Equal(
            "{DynamicResource Brush.LauncherBackground.Image.DimOverlay}",
            imageSource.Attribute("OverlayBrush")?.Value);

        var pageBackdrop = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Border"
            && element.Attribute("Background")?.Value == "{DynamicResource Brush.Page.Background}"));
        var activeTrigger = Assert.Single(pageBackdrop.Descendants().Where(element =>
            element.Name.LocalName == "DataTrigger"
            && element.Attribute("Binding")?.Value == "{Binding LauncherBackground.IsActive}"
            && element.Attribute("Value")?.Value == "True"));
        var opacitySetter = Assert.Single(activeTrigger.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Opacity"));
        Assert.Equal("0", opacitySetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void ControlBlurSamplesTheBackgroundWithoutAnAlwaysRenderedWholeWindowBlurSurface()
    {
        var repositoryRoot = FindRepositoryRoot().FullName;
        var windowDocument = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "Views",
            "Shell",
            "MainWindow.xaml"));
        var effectDocument = XDocument.Load(Path.Combine(
            repositoryRoot,
            "Launcher.App",
            "Styles",
            "ControlStyles.Effects.xaml"));

        Assert.DoesNotContain(windowDocument.Descendants(), element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "LauncherPreblurredBackdropSource");
        var sourceElement = Assert.Single(windowDocument.Descendants().Where(element =>
            element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "LauncherBackgroundVisualSource"));
        Assert.Equal("ImageBackdropSource", sourceElement.Name.LocalName);

        var baseStyle = Assert.Single(effectDocument.Root!.Elements().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value
                == "BackdropBlurBorderStyle"));
        var sourceModeSetter = Assert.Single(baseStyle.Elements().Where(element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "IsSourcePreblurred"));
        Assert.Equal("False", sourceModeSetter.Attribute("Value")?.Value);

        var localBlurCache = Assert.Single(baseStyle.Descendants().Where(element =>
            element.Name.LocalName == "BitmapCache"));
        Assert.Equal("0.2", localBlurCache.Attribute("RenderAtScale")?.Value);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
