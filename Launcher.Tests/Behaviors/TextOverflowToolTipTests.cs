/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.App.Behaviors;

namespace Launcher.Tests.Behaviors;

public sealed class TextOverflowToolTipTests
{
    [Theory]
    [InlineData("官方启动器目录", 500, false)]
    [InlineData(@"C:\Users\Example\AppData\Roaming\.minecraft", 500, false)]
    [InlineData(@"C:\Users\Example\Documents\launcher\Launcher.App\bin\Debug\net8.0-windows\.minecraft", 200, true)]
    public void OnlyClippedTextNeedsAToolTip(string text, double width, bool expected)
    {
        RunOnStaThread(() =>
        {
            var textBlock = CreateTextBlock(text);
            Arrange(textBlock, width);

            Assert.Equal(expected, TextOverflowToolTip.HasOverflow(textBlock));
        });
    }

    [Fact]
    public void RechecksAfterResizeAndRecycledContentChanges()
    {
        RunOnStaThread(() =>
        {
            var textBlock = CreateTextBlock(@"C:\Users\Example\Documents\Minecraft\Instances\ExampleInstance");
            Arrange(textBlock, 120);
            Assert.True(TextOverflowToolTip.HasOverflow(textBlock));

            Arrange(textBlock, 900);
            Assert.False(TextOverflowToolTip.HasOverflow(textBlock));

            Arrange(textBlock, 120);
            Assert.True(TextOverflowToolTip.HasOverflow(textBlock));

            textBlock.Text = ".minecraft";
            Arrange(textBlock, 120);
            Assert.False(TextOverflowToolTip.HasOverflow(textBlock));
        });
    }

    [Fact]
    public void FullyWrappedTextDoesNotNeedAToolTip()
    {
        RunOnStaThread(() =>
        {
            var textBlock = CreateTextBlock("A longer directory description that wraps across multiple lines.");
            textBlock.TextWrapping = TextWrapping.Wrap;
            Arrange(textBlock, 160);

            Assert.False(TextOverflowToolTip.HasOverflow(textBlock));
        });
    }

    private static TextBlock CreateTextBlock(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(4)
        };
        TextOptions.SetTextFormattingMode(textBlock, TextFormattingMode.Display);
        return textBlock;
    }

    private static void Arrange(TextBlock textBlock, double width)
    {
        textBlock.Measure(new Size(width, double.PositiveInfinity));
        textBlock.Arrange(new Rect(0, 0, width, textBlock.DesiredSize.Height));
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
