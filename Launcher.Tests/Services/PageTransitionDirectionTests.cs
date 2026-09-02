/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Launcher.App.Models;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

/// <summary>
/// 切页动画的方向来自页面在侧边栏里的上下关系：目标在下方就自下而上进入，
/// 在上方就自上而下。方向由 <see cref="NavigationCatalog.PageOrder"/> 决定，
/// 而它必须和侧边栏按钮的实际排列一致——曾经漏掉联机页，结果它进出都是同一个方向。
/// </summary>
[Collection(TransitionRenderingTestCollection.Name)]
public sealed class PageTransitionDirectionTests
{
    /// <summary>
    /// 侧边栏的排布：ItemsControl 铺开主导航项占据上部，下载任务按钮 DockPanel.Dock=Bottom
    /// 停在菜单底部（切换按钮更靠下，但它不是页面）。方向表必须原样复现这个顺序。
    /// </summary>
    [Fact]
    public void PageOrderMatchesTheSidebarLayout()
    {
        string[] expected =
        [
            .. NavigationCatalog.CreatePrimaryItems().Select(item => item.Page),
            NavigationCatalog.CreateDownloadTasksItem().Page
        ];

        Assert.Equal(expected, NavigationCatalog.PageOrder);
    }

    [Theory]
    // 联机页在主页下方、下载页上方：这两个方向此前都错成了默认的自下而上。
    [InlineData("Home", "Multiplayer", true)]
    [InlineData("Multiplayer", "Home", false)]
    [InlineData("Multiplayer", "Download", true)]
    [InlineData("Download", "Multiplayer", false)]
    // 下载任务页停在菜单最底部，从任何页面进去都该自下而上。
    [InlineData("Settings", "Install", true)]
    [InlineData("Install", "Settings", false)]
    [InlineData("Account", "Settings", true)]
    [InlineData("Settings", "Account", false)]
    public void TransitionEntersFromTheSideTheTargetSitsOn(string from, string to, bool entersFromBelow)
    {
        RunOnStaThread(() =>
        {
            UiTransitionGate.ResetForTesting(Dispatcher.CurrentDispatcher);
            var target = new Border { Width = 320d, Height = 200d };
            var host = new Grid();
            host.Children.Add(target);
            var service = new PageTransitionService(
                Dispatcher.CurrentDispatcher,
                _ => target,
                from,
                null,
                TransitionRenderCacheScope.TryAcquire,
                new SilentCompositionFrameSource());

            service.MoveTo(to);

            // 起始位移是动画的起点：正值表示页面先落在下方再向上归位。
            var startOffset = Assert.IsType<TranslateTransform>(target.RenderTransform).Y;
            var expected = entersFromBelow
                ? PageTransitionService.TransitionOffset
                : -PageTransitionService.TransitionOffset;
            Assert.Equal(expected, startOffset);

            service.SyncTo(to);
            UiTransitionGate.ResetForTesting();
        });
    }

    /// <summary>
    /// 账户详情按账户在左侧列表里的上下位置定方向，而列表会随增删改序。
    /// 顺序必须每次过渡现取：拿构造时的快照，方向会停在建视图那一刻。
    /// </summary>
    [Fact]
    public void DynamicOrderIsReadAgainOnEveryTransition()
    {
        RunOnStaThread(() =>
        {
            UiTransitionGate.ResetForTesting(Dispatcher.CurrentDispatcher);
            var target = new Border { Width = 320d, Height = 200d };
            var host = new Grid();
            host.Children.Add(target);
            List<string> order = ["a", "b"];
            var service = PageTransitionService.CreateWithDynamicOrder(
                Dispatcher.CurrentDispatcher,
                _ => target,
                "a",
                () => order);

            // b 在 a 下面，自下而上。
            service.MoveTo("b");
            Assert.Equal(
                PageTransitionService.TransitionOffset,
                Assert.IsType<TranslateTransform>(target.RenderTransform).Y);

            service.MoveTo("a");

            // b 被挪到了 a 上面：同一个 a→b，方向要跟着翻过来。
            order = ["b", "a"];
            service.MoveTo("b");
            Assert.Equal(
                -PageTransitionService.TransitionOffset,
                Assert.IsType<TranslateTransform>(target.RenderTransform).Y);

            service.SyncTo("b");
            UiTransitionGate.ResetForTesting();
        });
    }

    /// <summary>一帧都不来的合成源：让过渡停在起始位移上，方便直接读方向。</summary>
    private sealed class SilentCompositionFrameSource : ICompositionFrameSource
    {
        public void Subscribe(EventHandler handler)
        {
        }

        public void Unsubscribe(EventHandler handler)
        {
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
