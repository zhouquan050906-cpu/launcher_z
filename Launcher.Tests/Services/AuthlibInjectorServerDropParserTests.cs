/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Windows;
using Launcher.App.Services;

namespace Launcher.Tests.Services;

public sealed class AuthlibInjectorServerDropParserTests
{
    private const string StandardLink =
        AuthlibInjectorServerDropParser.Prefix
        + "https%3A%2F%2Flittleskin.cn%2Fapi%2Fyggdrasil";

    public static TheoryData<string, string> ValidLinks => new()
    {
        {
            "authlib-injector:yggdrasil-server:https%3A%2F%2Flittleskin.cn%2Fapi%2Fyggdrasil",
            "https://littleskin.cn/api/yggdrasil"
        },
        {
            "AUTHLIB-INJECTOR:YGGDRASIL-SERVER:https%3A%2F%2Flittleskin.cn%2Fapi%2Fyggdrasil%2F",
            "https://littleskin.cn/api/yggdrasil/"
        },
        {
            "authlib-injector:yggdrasil-server:littleskin.cn%2Fapi%2Fyggdrasil",
            "https://littleskin.cn/api/yggdrasil"
        }
    };

    public static TheoryData<string> InvalidLinks => new()
    {
        AuthlibInjectorServerDropParser.Prefix,
        AuthlibInjectorServerDropParser.Prefix + "https%3A%2F%2Fexample.com%2F%ZZ",
        AuthlibInjectorServerDropParser.Prefix + new string('a', AuthlibInjectorServerDropParser.MaximumTextLength),
        AuthlibInjectorServerDropParser.Prefix + "http%3A%2F%2Fexample.com%2Fapi%2Fyggdrasil",
        AuthlibInjectorServerDropParser.Prefix + "ftp%3A%2F%2Fexample.com%2Fapi%2Fyggdrasil",
        AuthlibInjectorServerDropParser.Prefix + "%2Fapi%2Fyggdrasil",
        AuthlibInjectorServerDropParser.Prefix + "https%3A%2F%2F%2Fapi%2Fyggdrasil"
    };

    [Theory]
    [MemberData(nameof(ValidLinks))]
    public void ParseAcceptsSupportedServerLinks(string text, string expectedServer)
    {
        var result = AuthlibInjectorServerDropParser.Parse(text);

        Assert.Equal(AuthlibInjectorServerDropStatus.Valid, result.Status);
        Assert.Equal(expectedServer, result.AuthenticationServer);
    }

    [Fact]
    public void ParseDecodesServerAddressExactlyOnce()
    {
        var result = AuthlibInjectorServerDropParser.Parse(
            AuthlibInjectorServerDropParser.Prefix
            + "https%3A%2F%2Fexample.com%2Fapi%2F%252Fvalue");

        Assert.Equal(AuthlibInjectorServerDropStatus.Valid, result.Status);
        Assert.Equal("https://example.com/api/%2Fvalue", result.AuthenticationServer);
    }

    [Theory]
    [MemberData(nameof(InvalidLinks))]
    public void ParseClaimsProtocolTextWithInvalidServer(string text)
    {
        var result = AuthlibInjectorServerDropParser.Parse(text);

        Assert.Equal(AuthlibInjectorServerDropStatus.Invalid, result.Status);
        Assert.Null(result.AuthenticationServer);
    }

    [Fact]
    public void ParseIgnoresSurroundingWhitespaceWhenMeasuringLength()
    {
        var padding = new string(' ', AuthlibInjectorServerDropParser.MaximumTextLength);

        var result = AuthlibInjectorServerDropParser.Parse($"{padding}{StandardLink}{padding}");

        Assert.Equal(AuthlibInjectorServerDropStatus.Valid, result.Status);
        Assert.Equal("https://littleskin.cn/api/yggdrasil", result.AuthenticationServer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://littleskin.cn/api/yggdrasil")]
    [InlineData("ordinary text")]
    public void ParseDoesNotClaimUnrelatedText(string? text)
    {
        var result = AuthlibInjectorServerDropParser.Parse(text);

        Assert.Equal(AuthlibInjectorServerDropStatus.NotRecognized, result.Status);
        Assert.Null(result.AuthenticationServer);
    }

    [Fact]
    public void ParseReadsUnicodeTextBeforeOtherFormats()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, StandardLink, autoConvert: false);
        data.SetData(DataFormats.Text, "ordinary text", autoConvert: false);

        var result = AuthlibInjectorServerDropParser.Parse(data);

        Assert.Equal(AuthlibInjectorServerDropStatus.Valid, result.Status);
    }

    [Theory]
    [MemberData(nameof(TextDataFormats))]
    public void ParseAcceptsEachSupportedWpfTextFormat(string format)
    {
        var data = new DataObject();
        data.SetData(format, StandardLink, autoConvert: false);

        var result = AuthlibInjectorServerDropParser.Parse(data);

        Assert.Equal(AuthlibInjectorServerDropStatus.Valid, result.Status);
    }

    public static TheoryData<string> TextDataFormats => new()
    {
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.StringFormat
    };
}
