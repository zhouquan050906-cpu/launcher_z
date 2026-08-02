/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class MinecraftAutoJoinArgumentsTests
{
    [Theory]
    [InlineData("play.example.com:25565", "play.example.com", 25565, false)]
    [InlineData("[2001:db8::1]:65535", "2001:db8::1", 65535, true)]
    public void ParserAcceptsSupportedAddressForms(
        string value,
        string expectedHost,
        int expectedPort,
        bool expectedIpv6)
    {
        var status = AutoJoinServerAddressParser.Parse(value, out var address);

        Assert.Equal(AutoJoinServerAddressParseStatus.Valid, status);
        Assert.NotNull(address);
        Assert.Equal(expectedHost, address.Host);
        Assert.Equal(expectedPort, address.Port);
        Assert.Equal(expectedIpv6, address.IsIpv6);
    }

    [Theory]
    [InlineData("play.example.com:65536")]
    public void ParserRejectsInvalidAddress(string value)
    {
        var status = AutoJoinServerAddressParser.Parse(value, out var address);

        Assert.Equal(AutoJoinServerAddressParseStatus.Invalid, status);
        Assert.Null(address);
    }

    [Theory]
    [InlineData(null)]
    public void ParserTreatsEmptyAddressAsDisabled(string? value)
    {
        var status = AutoJoinServerAddressParser.Parse(value, out var address);

        Assert.Equal(AutoJoinServerAddressParseStatus.Disabled, status);
        Assert.Null(address);
    }

    [Theory]
    [InlineData("1.19.4", false)]
    [InlineData("1.20", true)]
    public void QuickPlaySupportUsesMinecraftVersionThreshold(string version, bool expected)
    {
        Assert.Equal(expected, MinecraftQuickPlaySupport.IsSupported(version));
    }

    [Fact]
    public void BuilderCreatesQuickPlayArgumentForSupportedVersion()
    {
        var result = MinecraftAutoJoinArgumentBuilder.Create(
            "[2001:db8::1]:25565",
            "1.20");

        Assert.Equal(MinecraftAutoJoinArgumentStatus.Created, result.Status);
        Assert.True(result.UsesQuickPlay);
        Assert.Equal(
            ["--quickPlayMultiplayer", "[2001:db8::1]:25565"],
            result.Argument!.Values);
    }

    [Fact]
    public void BuilderCreatesLegacyArgumentsForUnsupportedOrUnknownVersion()
    {
        var result = MinecraftAutoJoinArgumentBuilder.Create(
            "play.example.com:25565",
            "1.19.4");

        Assert.Equal(MinecraftAutoJoinArgumentStatus.Created, result.Status);
        Assert.False(result.UsesQuickPlay);
        Assert.Equal(
            ["--server", "play.example.com", "--port", "25565"],
            result.Argument!.Values);
    }

    [Theory]
    [InlineData("")]
    public void BuilderDoesNotCreateArgumentForDisabledOrInvalidAddress(string address)
    {
        var result = MinecraftAutoJoinArgumentBuilder.Create(address, "1.20");

        Assert.Null(result.Argument);
        Assert.False(result.UsesQuickPlay);
    }
}
