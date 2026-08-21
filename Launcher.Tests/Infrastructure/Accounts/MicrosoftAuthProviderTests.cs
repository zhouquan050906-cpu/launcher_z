/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CmlLib.Core.Auth.Microsoft;
using Launcher.Application.Accounts;
using Launcher.Infrastructure.Accounts;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MicrosoftAuthProviderTests
{
    [Theory]
    [InlineData("401: Unauthorized", LaunchAccountSessionFailureReason.ReauthenticationRequired)]
    [InlineData("403: Forbidden", LaunchAccountSessionFailureReason.AuthenticationApplicationNotAuthorized)]
    [InlineData("503: ServiceUnavailable", LaunchAccountSessionFailureReason.AuthenticationServerUnavailable)]
    public void JeAuthExceptionWithoutStatusCodeIsClassifiedFromMessage(
        string message,
        LaunchAccountSessionFailureReason expectedReason)
    {
        // CmlLib 在响应体不是预期 JSON 时抛出的异常只有消息，StatusCode 保持为 0。
        var jeException = new JEAuthException(message);
        Assert.Equal(0, jeException.StatusCode);

        var translated = MicrosoftAuthProvider.TranslateAuthenticationException(jeException);

        Assert.Equal(expectedReason, translated.Reason);
    }

    [Theory]
    [InlineData(401, LaunchAccountSessionFailureReason.ReauthenticationRequired)]
    [InlineData(403, LaunchAccountSessionFailureReason.AuthenticationApplicationNotAuthorized)]
    [InlineData(503, LaunchAccountSessionFailureReason.AuthenticationServerUnavailable)]
    public void JeAuthExceptionWithStatusCodeKeepsUsingTheReportedStatus(
        int statusCode,
        LaunchAccountSessionFailureReason expectedReason)
    {
        var jeException = new JEAuthException("UNAUTHORIZED", "UNAUTHORIZED", "rejected", statusCode);

        var translated = MicrosoftAuthProvider.TranslateAuthenticationException(jeException);

        Assert.Equal(expectedReason, translated.Reason);
    }

    [Theory]
    [InlineData("Minecraft authentication failed.")]
    [InlineData("401k plan")]
    [InlineData("99: Nonsense")]
    public void JeAuthExceptionWithoutRecoverableStatusFallsBackToInvalidResponse(string message)
    {
        var jeException = new JEAuthException(message);

        var translated = MicrosoftAuthProvider.TranslateAuthenticationException(jeException);

        Assert.Equal(LaunchAccountSessionFailureReason.InvalidAuthenticationResponse, translated.Reason);
    }
}
