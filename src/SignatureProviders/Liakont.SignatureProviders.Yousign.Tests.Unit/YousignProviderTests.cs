namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Comportement HTTP du provider Yousign (ADR-0029 §1/§4) : gating par capacités (QES/OnSite → NotSupported),
/// appel de création sur l'URL allowlistée, retry sur 429 (puis succès), rejet 4xx, téléchargement de preuve.
/// </summary>
public sealed class YousignProviderTests
{
    private static SignatureRequest Request(SignatureLevel level, SignatureMode mode = SignatureMode.Remote) => new()
    {
        CompanyId = Guid.NewGuid().ToString(),
        DocumentId = Guid.NewGuid().ToString(),
        RequestedLevel = level,
        RequestedMode = mode,
    };

    private static YousignSignatureProvider Provider(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = YousignUrlAllowlist.ResolveBaseUri(YousignEnvironment.Sandbox) },
            new YousignAccountConfig(YousignEnvironment.Sandbox, "api-key", "webhook-secret"),
            YousignRetryPolicy.NoDelay(),
            jitterSource: () => 0d);

    [Fact]
    public async Task RequestSignature_with_unsupported_level_returns_NotSupported_without_calling_http()
    {
        var handler = new StubHttpMessageHandler();
        var result = await Provider(handler).RequestSignatureAsync(Request(SignatureLevel.QES));

        result.State.Should().Be(SignatureRequestState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(SignatureCapability.QualifiedLevel);
        handler.CalledUris.Should().BeEmpty("le gating par capacités précède tout appel réseau");
    }

    [Fact]
    public async Task RequestSignature_with_unsupported_mode_returns_NotSupported()
    {
        var handler = new StubHttpMessageHandler();
        var result = await Provider(handler).RequestSignatureAsync(Request(SignatureLevel.SES, SignatureMode.OnSite));

        result.State.Should().Be(SignatureRequestState.CapabilityNotSupported);
        result.CapabilityNotSupported!.Capability.Should().Be(SignatureCapability.OnSiteSignature);
    }

    [Fact]
    public async Task RequestSignature_submits_to_allowlisted_url_and_returns_provider_reference()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Created, """{"id":"sig-123","status":"draft"}""");

        var result = await Provider(handler).RequestSignatureAsync(Request(SignatureLevel.SES));

        result.State.Should().Be(SignatureRequestState.Submitted);
        result.ProviderReference.Should().Be("sig-123");
        handler.CalledUris.Should().ContainSingle()
            .Which!.ToString().Should().Be("https://api-sandbox.yousign.app/v3/signature_requests");
    }

    [Fact]
    public async Task RequestSignature_retries_on_429_then_succeeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests);
        handler.Enqueue(HttpStatusCode.Created, """{"id":"sig-9","status":"draft"}""");

        var result = await Provider(handler).RequestSignatureAsync(Request(SignatureLevel.SES));

        result.State.Should().Be(SignatureRequestState.Submitted);
        result.ProviderReference.Should().Be("sig-9");
        handler.CalledUris.Should().HaveCount(2, "un 429 doit être ré-essayé");
    }

    [Fact]
    public async Task RequestSignature_returns_Rejected_on_business_4xx()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.UnprocessableEntity, """{"detail":"invalid"}""");

        var result = await Provider(handler).RequestSignatureAsync(Request(SignatureLevel.SES));

        result.State.Should().Be(SignatureRequestState.Rejected);
    }

    [Fact]
    public async Task DownloadProof_returns_content_on_success()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueBytes(HttpStatusCode.OK, [1, 2, 3, 4], "application/pdf");

        var proof = await Provider(handler).DownloadProofAsync("sig-123");

        proof.Content.Should().NotBeNull();
        proof.Content!.Should().Equal(1, 2, 3, 4);
        proof.ContentType.Should().Be("application/pdf");
    }
}
