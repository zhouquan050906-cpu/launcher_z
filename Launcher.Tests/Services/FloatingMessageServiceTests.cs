/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class FloatingMessageServiceTests
{
    private const string ReleaseHint = "松开以导入";
    private const string UnsupportedHint = "不支持的文件";

    private static readonly object FileImport = new();
    private static readonly object AccountDrop = new();

    [Fact]
    public void DragHintsAreRequestedWithoutAutoHide()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);

        var request = Assert.Single(requests);
        Assert.Equal(ReleaseHint, request.Message);
        Assert.False(request.AutoHide);
    }

    [Fact]
    public void OrdinaryMessagesKeepAutoHiding()
    {
        var (service, requests) = CreateService();

        service.Show(ReleaseHint);

        var request = Assert.Single(requests);
        Assert.True(request.AutoHide);
    }

    [Fact]
    public void RepeatedDragHintDoesNotRestartTheOverlay()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.ShowDragHint(FileImport, ReleaseHint);
        service.ShowDragHint(FileImport, ReleaseHint);

        Assert.Single(requests);
    }

    [Fact]
    public void ChangedDragHintReplacesThePreviousOne()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.ShowDragHint(FileImport, UnsupportedHint);

        Assert.Equal([ReleaseHint, UnsupportedHint], requests.Select(request => request.Message));
        Assert.All(requests, request => Assert.False(request.AutoHide));
    }

    [Fact]
    public void ClearingRemovesTheDragHintExactlyOnce()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.ClearDragHint(FileImport);
        service.ClearDragHint(FileImport);

        Assert.Equal(2, requests.Count);
        Assert.Equal(string.Empty, requests[1].Message);
    }

    [Fact]
    public void ClearingWithoutAnActiveDragHintLeavesOtherMessagesAlone()
    {
        var (service, requests) = CreateService();

        service.Show(ReleaseHint);
        requests.Clear();
        service.ClearDragHint(FileImport);

        Assert.Empty(requests);
    }

    [Fact]
    public void EmptyDragHintIsTreatedAsAClear()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.ShowDragHint(FileImport, string.Empty);

        Assert.Equal(2, requests.Count);
        Assert.Equal(string.Empty, requests[1].Message);
    }

    [Fact]
    public void OneSourceCannotClearAnotherSourcesDragHint()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        requests.Clear();

        service.ClearDragHint(AccountDrop);

        Assert.Empty(requests);
    }

    // 一次 DragOver 会先流过未命中的第三方卡片处理器（它会清除自己的提示），
    // 再由文件导入处理器显示提示。若归属判断缺失，这里会退化成"清除→显示"反复触发，
    // 表现为用户按住不放时进场动画一直重播。
    [Fact]
    public void UnrelatedHandlerClearingEveryDragOverDoesNotReplayTheOverlay()
    {
        var (service, requests) = CreateService();

        for (var dragOver = 0; dragOver < 20; dragOver++)
        {
            service.ClearDragHint(AccountDrop);
            service.ShowDragHint(FileImport, ReleaseHint);
        }

        var request = Assert.Single(requests);
        Assert.Equal(ReleaseHint, request.Message);
        Assert.False(request.AutoHide);
    }

    [Fact]
    public void EndingTheDragClearsWhicheverSourceOwnsTheHint()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(AccountDrop, ReleaseHint);
        requests.Clear();

        service.ClearDragHint();

        var request = Assert.Single(requests);
        Assert.Equal(string.Empty, request.Message);
    }

    // 这是"提示消失后再拖入一遍也不显示"的核心回归：去重状态必须随浮层被顶掉而失效。
    [Fact]
    public void DragHintShowsAgainAfterAnOrdinaryMessageReplacedIt()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.Show("下载完成");
        requests.Clear();

        service.ShowDragHint(FileImport, ReleaseHint);

        var request = Assert.Single(requests);
        Assert.Equal(ReleaseHint, request.Message);
        Assert.False(request.AutoHide);
    }

    [Fact]
    public void DragHintShowsAgainAfterTheDragEnded()
    {
        var (service, requests) = CreateService();

        service.ShowDragHint(FileImport, ReleaseHint);
        service.ClearDragHint(FileImport);
        requests.Clear();

        service.ShowDragHint(FileImport, ReleaseHint);

        Assert.Single(requests);
    }

    private static (IFloatingMessageService Service, List<FloatingMessageRequest> Requests) CreateService()
    {
        var service = new FloatingMessageService();
        var requests = new List<FloatingMessageRequest>();
        service.MessageRequested += requests.Add;
        return (service, requests);
    }
}
