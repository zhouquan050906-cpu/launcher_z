/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class SelfBringIntoViewSuppressionTests
{
    [Fact]
    public void EnabledBehaviorHandlesOnlyTheHostRequest()
    {
        RunOnStaThread(() =>
        {
            var host = new ContentControl();
            var child = new Button();
            host.Content = child;
            SelfBringIntoViewSuppression.SetIsEnabled(host, true);
            bool? hostRequestHandled = null;
            bool? childRequestHandled = null;
            host.AddHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((_, e) =>
                {
                    if (ReferenceEquals(e.OriginalSource, host))
                        hostRequestHandled = e.Handled;
                    else if (ReferenceEquals(e.OriginalSource, child))
                        childRequestHandled = e.Handled;
                }),
                true);

            host.BringIntoView();
            child.BringIntoView();

            Assert.True(hostRequestHandled);
            Assert.False(childRequestHandled);
        });
    }

    [Fact]
    public void DisabledBehaviorStopsHandlingTheHostRequest()
    {
        RunOnStaThread(() =>
        {
            var host = new ContentControl();
            SelfBringIntoViewSuppression.SetIsEnabled(host, true);
            SelfBringIntoViewSuppression.SetIsEnabled(host, false);
            bool? requestHandled = null;
            host.AddHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                new RequestBringIntoViewEventHandler((_, e) => requestHandled = e.Handled),
                true);

            host.BringIntoView();

            Assert.False(requestHandled);
        });
    }

    [Theory]
    [InlineData("Settings", "SettingsPageView.xaml", "{Binding CurrentSectionViewModel}")]
    [InlineData("GameSettings", "GameSettingsDetailsView.xaml", "{Binding ScrollSectionViewModel}")]
    public void DynamicScrollableContentHostsEnableSuppression(
        string viewDirectory,
        string fileName,
        string contentBinding)
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "Launcher.App",
            "Views",
            viewDirectory,
            fileName));
        var host = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "ContentControl"
            && element.Attribute("Content")?.Value == contentBinding));

        Assert.Equal(
            "True",
            host.Attributes().Single(attribute =>
                attribute.Name.LocalName == "SelfBringIntoViewSuppression.IsEnabled").Value);
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
