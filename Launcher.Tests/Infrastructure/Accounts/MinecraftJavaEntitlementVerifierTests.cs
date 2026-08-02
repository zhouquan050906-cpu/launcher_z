/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Net;
using System.Net.Http.Headers;
using Launcher.Application.Accounts;
using Launcher.Infrastructure.Accounts;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class MinecraftJavaEntitlementVerifierTests
{
    [Theory]
    [InlineData("product_minecraft")]
    public async Task RecognizedJavaEditionEntitlementIsAccepted(string entitlement)
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"items":[{"name":"{{entitlement}}"}]}""")
            };
        }));
        var verifier = new MinecraftJavaEntitlementVerifier(httpClient);

        await verifier.EnsureOwnedAsync("minecraft-access-token", CancellationToken.None);

        Assert.Equal(
            new Uri("https://api.minecraftservices.com/entitlements/mcstore"),
            capturedRequest?.RequestUri);
        Assert.Equal(
            new AuthenticationHeaderValue("Bearer", "minecraft-access-token"),
            capturedRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task EmptyEntitlementsRejectAccountBeforeItCanBePersisted()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[]}""")
            }));
        var verifier = new MinecraftJavaEntitlementVerifier(httpClient);

        var exception = await Assert.ThrowsAsync<MicrosoftAccountAuthenticationException>(
            () => verifier.EnsureOwnedAsync("minecraft-access-token", CancellationToken.None));

        Assert.Equal(LaunchAccountSessionFailureReason.GameOwnershipRequired, exception.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LaunchAccountSessionFailureReason.ReauthenticationRequired)]
    public async Task EntitlementServiceFailuresRetainActionableReason(
        HttpStatusCode statusCode,
        LaunchAccountSessionFailureReason expectedReason)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(statusCode)));
        var verifier = new MinecraftJavaEntitlementVerifier(httpClient);

        var exception = await Assert.ThrowsAsync<MicrosoftAccountAuthenticationException>(
            () => verifier.EnsureOwnedAsync("minecraft-access-token", CancellationToken.None));

        Assert.Equal(expectedReason, exception.Reason);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
