/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Xml.Linq;

namespace Launcher.Tests.Views.GameSettings;

public sealed class GameSettingsDetailsViewBehaviorTests
{
    [Fact]
    public void LocalContentViewsRemainExplicitRetainedChildrenWithRecyclingVirtualization()
    {
        RunOnStaThread(() =>
        {
            var repositoryRoot = FindRepositoryRoot();
            var detailsDocument = XDocument.Load(Path.Combine(
                repositoryRoot,
                "Launcher.App",
                "Views",
                "GameSettings",
                "GameSettingsDetailsView.xaml"));
            var expectedViews = new[]
            {
                ("InstanceModManagementSettingsView", "ModManagement", "InstanceModManagementSettingsViewModel"),
                ("InstanceSaveManagementSettingsView", "SaveManagement", "InstanceSaveManagementSettingsViewModel"),
                ("InstanceResourcePackManagementSettingsView", "ResourcePackManagement", "InstanceResourcePackManagementSettingsViewModel"),
                ("InstanceShaderPackManagementSettingsView", "ShaderPackManagement", "InstanceShaderPackManagementSettingsViewModel")
            };

            foreach (var (viewName, dataContext, viewModelName) in expectedViews)
            {
                var retainedView = Assert.Single(
                    detailsDocument.Descendants().Where(element => element.Name.LocalName == viewName));
                Assert.Equal(
                    $"{{Binding {dataContext}}}",
                    retainedView.Attributes().Single(attribute => attribute.Name.LocalName == "DataContext").Value);
                Assert.DoesNotContain(
                    detailsDocument.Descendants().Where(element => element.Name.LocalName == "DataTemplate"),
                    element => element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "DataType"
                        && attribute.Value.Contains(viewModelName, StringComparison.Ordinal)));

                var viewDocument = XDocument.Load(Path.Combine(
                    repositoryRoot,
                    "Launcher.App",
                    "Views",
                    "GameSettings",
                    $"{viewName}.xaml"));
                var listBox = Assert.Single(
                    viewDocument.Descendants().Where(element => element.Name.LocalName == "ListBox"));
                Assert.Equal(
                    "True",
                    listBox.Attributes().Single(attribute =>
                        attribute.Name.LocalName == "VirtualizingPanel.IsVirtualizing").Value);
                Assert.Equal(
                    "Recycling",
                    listBox.Attributes().Single(attribute =>
                        attribute.Name.LocalName == "VirtualizingPanel.VirtualizationMode").Value);
                Assert.Equal(
                    "{Binding ListEntranceAnimationToken}",
                    listBox.Attributes().Single(attribute =>
                        attribute.Name.LocalName == "VirtualizedListItemStateBehavior.ScrollResetToken").Value);
            }
        });
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
}
