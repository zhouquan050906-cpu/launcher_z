/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Xml.Linq;

namespace Launcher.Tests.Resources;

public sealed class ControlStyleContractTests
{
    [Fact]
    public void ReadOnlyDisplayFieldsUseTheSharedMinimumHeightAndKeepWrapping()
    {
        var document = LoadStyle("ControlStyles.Page.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = FindStyle(document, xaml, "ReadOnlyFieldSurfaceStyle");
        var textStyle = FindStyle(document, xaml, "ReadOnlyFieldTextStyle");

        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "MinHeight"
            && element.Attribute("Value")?.Value == "{StaticResource LauncherCompactControlHeight}");
        Assert.DoesNotContain(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Height");
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "Padding"
            && element.Attribute("Value")?.Value == "14,4");
        Assert.Contains(textStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TextWrapping"
            && element.Attribute("Value")?.Value == "Wrap");
        Assert.DoesNotContain(textStyle.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "TextTrimming");
    }

    [Fact]
    public void StandardButtonsInputsAndClosedComboBoxesShareCompactHeight()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var inputs = LoadStyle("ControlStyles.Inputs.xaml");
        var dialogs = LoadStyle("ControlStyles.Dialogs.xaml");
        const string sharedHeight = "{StaticResource LauncherCompactControlHeight}";

        foreach (var key in new[]
                 {
                     "DialogTextBoxStyle",
                     "DialogPasswordBoxStyle",
                     "LauncherComboBoxStyle"
                 })
        {
            Assert.Contains(FindStyle(inputs, xaml, key).Elements(), element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Height"
                && element.Attribute("Value")?.Value == sharedHeight);
        }

        Assert.Contains(
            FindStyle(inputs, xaml, "DialogWrappingTextBoxStyle").Elements(),
            element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "MinHeight"
                && element.Attribute("Value")?.Value == sharedHeight);
        Assert.Contains(
            FindStyle(dialogs, xaml, "DialogButtonStyle").Elements(),
            element => element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Height"
                && element.Attribute("Value")?.Value == sharedHeight);
    }

    [Fact]
    public void StandardDisplayFieldsButtonsAndInputsShareNeutralBorder()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var page = LoadStyle("ControlStyles.Page.xaml");
        var inputs = LoadStyle("ControlStyles.Inputs.xaml");
        var dialogs = LoadStyle("ControlStyles.Dialogs.xaml");
        const string sharedBorder = "{DynamicResource Brush.Input.TextBox.Border}";

        AssertSharedBorder(FindStyle(page, xaml, "ReadOnlyFieldSurfaceStyle"), sharedBorder);
        AssertSharedBorder(FindStyle(inputs, xaml, "DialogTextBoxStyle"), sharedBorder);
        AssertSharedBorder(FindStyle(inputs, xaml, "DialogPasswordBoxStyle"), sharedBorder);
        AssertSharedBorder(FindStyle(inputs, xaml, "LauncherComboBoxStyle"), sharedBorder);
        AssertSharedBorder(FindStyle(dialogs, xaml, "DialogButtonStyle"), sharedBorder);

        foreach (var key in new[]
                 {
                     "DialogButtonStyle",
                     "PrimaryDialogButtonStyle",
                     "DangerDialogButtonStyle"
                 })
        {
            var root = Assert.Single(FindStyle(dialogs, xaml, key).Descendants().Where(element =>
                element.Name.LocalName == "Border"
                && element.Attribute(xaml + "Name")?.Value == "Root"));

            Assert.Equal("{TemplateBinding BorderBrush}", root.Attribute("BorderBrush")?.Value);
            Assert.Equal("{TemplateBinding BorderThickness}", root.Attribute("BorderThickness")?.Value);
        }
    }

    [Fact]
    public void ResourcesProjectFilterButtonInheritsTheSharedCompactHeight()
    {
        var document = LoadAppXaml("Views", "Resources", "ResourcesPageView.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var template = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "DataTemplate"
            && element.Attribute(xaml + "Key")?.Value == "ResourcesProjectFilterButtonTemplate"));
        var button = Assert.Single(template.Descendants().Where(element =>
            element.Name.LocalName == "Button"));

        Assert.Null(button.Attribute("Height"));
        Assert.Equal(
            "{StaticResource LauncherDialogButtonStyle}",
            button.Attribute("Style")?.Value);
    }

    private static void AssertSharedBorder(XElement style, string expectedBrush)
    {
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "BorderBrush"
            && element.Attribute("Value")?.Value == expectedBrush);
        Assert.Contains(style.Elements(), element =>
            element.Name.LocalName == "Setter"
            && element.Attribute("Property")?.Value == "BorderThickness"
            && element.Attribute("Value")?.Value == "1");
    }

    private static XElement FindStyle(
        XDocument document,
        XNamespace xaml,
        string key) => Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "Style"
            && element.Attribute(xaml + "Key")?.Value == key));

    private static XDocument LoadStyle(string fileName) =>
        LoadAppXaml("Styles", fileName);

    private static XDocument LoadAppXaml(params string[] pathParts) =>
        XDocument.Load(Path.Combine(
            [FindRepositoryRoot().FullName, "Launcher.App", .. pathParts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }
}
