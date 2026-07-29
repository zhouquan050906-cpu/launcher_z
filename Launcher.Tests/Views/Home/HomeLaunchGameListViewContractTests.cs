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

using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Xml.Linq;
using Launcher.App.Controls;
using Launcher.App.Converters;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Views.Home;

public sealed class HomeLaunchGameListViewContractTests : TestTempDirectory
{
    [Fact]
    public void MenuShadowLayerDoesNotContainScrollableContent()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot().FullName,
            "Launcher.App",
            "Views",
            "Home",
            "HomeLaunchGameListView.xaml"));
        var menuContainer = FindNamedElement(document, "HomeLaunchMenuPanelShadow");
        var shadowLayer = FindNamedElement(document, "HomeLaunchMenuShadowLayer");
        var menuPanel = FindNamedElement(document, "HomeLaunchMenuPanel");
        var instanceList = FindNamedElement(document, "HomeLaunchInstanceListBox");

        Assert.Equal("Grid", menuContainer.Name.LocalName);
        Assert.DoesNotContain(
            menuContainer.Elements(),
            element => element.Name.LocalName.EndsWith(".Effect", StringComparison.Ordinal));
        Assert.Equal("False", shadowLayer.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal("BitmapCache", shadowLayer.Attribute("CacheMode")?.Value);
        Assert.Contains(
            shadowLayer.Elements(),
            element => element.Name.LocalName == "Border.Effect");
        Assert.Same(menuContainer, shadowLayer.Parent);
        Assert.Same(menuContainer, menuPanel.Parent);
        Assert.DoesNotContain(shadowLayer, instanceList.Ancestors());
    }

    [Fact]
    public void DirectIconSourceBindingsUseImmediateFileLoader()
    {
        var unsafeBindings = Directory
            .EnumerateFiles(Path.Combine(FindRepositoryRoot().FullName, "Launcher.App"), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName == "Source")
                .Where(attribute => attribute.Value.Contains("Binding", StringComparison.Ordinal))
                .Where(attribute => attribute.Value.Contains("IconSource", StringComparison.Ordinal))
                .Where(attribute => !attribute.Value.Contains("ResolvedIconSource", StringComparison.Ordinal))
                .Where(attribute => !attribute.Value.Contains("ResolvedTitleIconSource", StringComparison.Ordinal))
                .Where(attribute => !attribute.Value.Contains("IconSourceImageConverter", StringComparison.Ordinal))
                .Select(attribute => $"{path}: {attribute.Value}"))
            .ToArray();

        Assert.Empty(unsafeBindings);
    }

    [Fact]
    public void LocalInstanceIconDoesNotPreventContainingDirectoryMove()
    {
        var sourceDirectory = Path.Combine(TempRoot, "instance");
        var destinationDirectory = Path.Combine(TempRoot, "renamed-instance");
        Directory.CreateDirectory(sourceDirectory);

        var iconPath = Path.Combine(sourceDirectory, "resource-project-icon.png");
        File.Copy(
            Path.Combine(FindRepositoryRoot().FullName, "Launcher.App", "Assets", "Icons", "block", "grass_block.png"),
            iconPath);

        RunOnStaThread(() =>
        {
            var converter = new IconSourceImageConverter();
            var image = Assert.IsAssignableFrom<ImageSource>(converter.Convert(
                new Uri(iconPath).AbsoluteUri,
                typeof(ImageSource),
                null!,
                CultureInfo.InvariantCulture));

            Directory.Move(sourceDirectory, destinationDirectory);

            Assert.True(File.Exists(Path.Combine(destinationDirectory, "resource-project-icon.png")));
            GC.KeepAlive(image);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.GetFiles("Launcher.sln").Length == 0)
            root = root.Parent ?? throw new DirectoryNotFoundException("Could not locate repository root.");
        return root;
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document
            .Descendants()
            .Single(element => element
                .Attributes()
                .Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
    }
}
