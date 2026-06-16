namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Xunit;

/// <summary>
/// Anti-SSRF de l'allowlist d'URL (ADR-0029 §6 ; INV-YOUSIGN-7) : seules les origines HTTPS exactes
/// sandbox/production sont autorisées ; un <c>http://</c>, un host arbitraire ou un port différent sont
/// REFUSÉS, et le handler de garde bloque tout appel hors liste AVANT qu'il ne parte (porteur du Bearer).
/// </summary>
public sealed class YousignUrlAllowlistTests
{
    [Fact]
    public void Resolves_https_base_uri_per_environment()
    {
        YousignUrlAllowlist.ResolveBaseUri(YousignEnvironment.Sandbox)
            .Should().Be(new Uri("https://api-sandbox.yousign.app/v3/"));
        YousignUrlAllowlist.ResolveBaseUri(YousignEnvironment.Production)
            .Should().Be(new Uri("https://api.yousign.app/v3/"));
    }

    [Theory]
    [InlineData("https://api-sandbox.yousign.app/v3/signature_requests", true)]
    [InlineData("https://api.yousign.app/v3/signature_requests/abc", true)]
    [InlineData("http://api-sandbox.yousign.app/v3/signature_requests", false)] // non-HTTPS
    [InlineData("https://evil.example.com/v3/signature_requests", false)] // host hors liste
    [InlineData("https://api-sandbox.yousign.app:8443/v3", false)] // port différent
    [InlineData("file:///etc/passwd", false)] // schéma non-HTTPS
    public void IsAllowed_only_accepts_exact_https_origins(string url, bool expected)
    {
        YousignUrlAllowlist.IsAllowed(new Uri(url, UriKind.Absolute)).Should().Be(expected);
    }

    [Fact]
    public void IsAllowed_rejects_null_and_relative_uris()
    {
        YousignUrlAllowlist.IsAllowed(null).Should().BeFalse();
        YousignUrlAllowlist.IsAllowed(new Uri("/v3/signature_requests", UriKind.Relative)).Should().BeFalse();
    }

    [Fact]
    public async Task Guard_handler_blocks_calls_to_non_allowlisted_uri()
    {
        var handler = new YousignSsrfGuardHandler { InnerHandler = new AlwaysOkHandler() };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/steal");
        var act = async () => await invoker.SendAsync(request, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*anti-SSRF*");
    }

    [Fact]
    public async Task Guard_handler_allows_calls_to_allowlisted_uri()
    {
        var handler = new YousignSsrfGuardHandler { InnerHandler = new AlwaysOkHandler() };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api-sandbox.yousign.app/v3/x");
        using var response = await invoker.SendAsync(request, default);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
