/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class PageContentPrewarmTests
{
    /// <summary>
    /// 预热必须让页面进入可测量状态，否则虚拟化面板不会初始化视口，预热就没有意义。
    /// 同时不得被绘制，避免与当前页重叠。
    /// </summary>
    [Fact]
    public void BeginMakesThePageMeasurableWithoutRenderingIt()
    {
        RunOnStaThread(() =>
        {
            var (page, _, window) = CreateBoundPage();
            try
            {
                Assert.Equal(Visibility.Collapsed, page.Visibility);
                Assert.Equal(0d, page.ActualWidth);

                PageContentPrewarm.Begin(page);

                Assert.Equal(Visibility.Hidden, page.Visibility);
                // Collapsed 会短路测量；只有真的被测量过，ActualWidth 才不为零。
                Assert.True(page.ActualWidth > 0d, $"ActualWidth={page.ActualWidth}");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 这是本类型存在的理由：给绑定属性写本地值会顶掉绑定表达式，
    /// 而 Visibility 的默认值恰好是 Visible——恢复不当会让页面永久显示并压住当前页。
    /// </summary>
    [Fact]
    public void EndRestoresALiveVisibilityBinding()
    {
        RunOnStaThread(() =>
        {
            var (page, source, window) = CreateBoundPage();
            try
            {
                var binding = PageContentPrewarm.Begin(page);

                Assert.True(PageContentPrewarm.End(page, binding));

                Assert.Equal(Visibility.Collapsed, page.Visibility);
                Assert.NotNull(
                    BindingOperations.GetBindingExpressionBase(page, UIElement.VisibilityProperty));

                // 光是"绑定对象还在"不够，必须证明它还会跟着源走。
                source.IsCurrent = true;
                Assert.Equal(Visibility.Visible, page.Visibility);
                source.IsCurrent = false;
                Assert.Equal(Visibility.Collapsed, page.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 没有绑定时不能只清本地值：Visibility 的默认值是 Visible，清了反而会显示出来。
    /// </summary>
    [Fact]
    public void EndCollapsesThePageWhenThereWasNoBinding()
    {
        RunOnStaThread(() =>
        {
            var page = new Border { Width = 120d, Height = 80d, Visibility = Visibility.Collapsed };
            var window = CreateHostWindow(page);
            try
            {
                window.Show();
                var binding = PageContentPrewarm.Begin(page);
                Assert.Null(binding);

                Assert.True(PageContentPrewarm.End(page, binding));

                Assert.Equal(Visibility.Collapsed, page.Visibility);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Border Page, PageSource Source, Window Window) CreateBoundPage()
    {
        var source = new PageSource();
        var page = new Border { Width = 120d, Height = 80d };
        page.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(PageSource.IsCurrent))
            {
                Source = source,
                Converter = new BooleanToVisibilityConverter()
            });
        var window = CreateHostWindow(page);
        window.Show();
        return (page, source, window);
    }

    private static Window CreateHostWindow(UIElement content)
    {
        var host = new Grid();
        host.Children.Add(content);
        return new Window
        {
            Width = 320d,
            Height = 200d,
            Left = -10000d,
            Top = -10000d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = host
        };
    }

    private sealed class PageSource : INotifyPropertyChanged
    {
        private bool isCurrent;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsCurrent
        {
            get => isCurrent;
            set
            {
                if (isCurrent == value)
                    return;
                isCurrent = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }
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
