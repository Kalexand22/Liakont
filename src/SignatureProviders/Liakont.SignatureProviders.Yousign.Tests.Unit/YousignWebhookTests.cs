namespace Liakont.SignatureProviders.Yousign.Tests.Unit;

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Liakont.Modules.Signature.Contracts;
using Xunit;

/// <summary>
/// Vérification HMAC du webhook Yousign (ADR-0029 §3 ; INV-YOUSIGN-3) : un HMAC valide sur le RAW body →
/// Accepted (avec référence + event id pour l'idempotence) ; une signature absente/falsifiée → Rejected AVANT
/// tout traitement. Le HMAC est calculé EN INTERNE (jamais WebhookSignature.Compute vendored).
/// </summary>
public sealed class YousignWebhookTests
{
    private const string Secret = "webhook-secret";

    private const string Body =
        """{"event_name":"signature_request.done","data":{"signature_request":{"id":"sig-77","status":"done"}}}""";

    private static YousignSignatureProvider Provider() =>
        new(
            new HttpClient(new StubHttpMessageHandler()) { BaseAddress = YousignUrlAllowlist.ResolveBaseUri(YousignEnvironment.Sandbox) },
            new YousignAccountConfig(YousignEnvironment.Sandbox, "api-key", Secret));

    private static string ComputeSignature(string body, string secret)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return YousignDefaults.WebhookSignaturePrefix + Convert.ToHexStringLower(hash);
    }

    private static SignatureWebhookContext Context(string body, string? signature)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (signature is not null)
        {
            headers[YousignDefaults.WebhookSignatureHeader] = signature;
        }

        return new SignatureWebhookContext
        {
            RawBody = Encoding.UTF8.GetBytes(body),
            Headers = headers,
            TenantHandle = "opaque-ref",
        };
    }

    [Fact]
    public async Task Valid_hmac_is_accepted_with_reference_and_event_id()
    {
        var result = await Provider().HandleWebhookAsync(Context(Body, ComputeSignature(Body, Secret)));

        result.State.Should().Be(SignatureWebhookState.Accepted);
        result.ProviderReference.Should().Be("sig-77");
        result.EventId.Should().NotBeNullOrWhiteSpace("l'identifiant d'événement porte la clé d'idempotence");
    }

    [Fact]
    public async Task Forged_hmac_is_rejected_before_processing()
    {
        var forged = YousignDefaults.WebhookSignaturePrefix + Convert.ToHexStringLower(new byte[32]);
        var result = await Provider().HandleWebhookAsync(Context(Body, forged));

        result.State.Should().Be(SignatureWebhookState.Rejected);
        result.ProviderReference.Should().BeNull("une signature falsifiée n'est jamais parsée ni traitée");
    }

    [Fact]
    public async Task Hmac_for_a_different_body_is_rejected()
    {
        // Signature calculée sur un AUTRE corps (rejeu/altération) → rejet.
        var signatureForOtherBody = ComputeSignature("""{"event_name":"x"}""", Secret);
        var result = await Provider().HandleWebhookAsync(Context(Body, signatureForOtherBody));

        result.State.Should().Be(SignatureWebhookState.Rejected);
    }

    [Fact]
    public async Task Missing_signature_header_is_rejected()
    {
        var result = await Provider().HandleWebhookAsync(Context(Body, signature: null));

        result.State.Should().Be(SignatureWebhookState.Rejected);
    }

    [Fact]
    public async Task Same_event_resolves_a_stable_idempotency_id()
    {
        var first = await Provider().HandleWebhookAsync(Context(Body, ComputeSignature(Body, Secret)));
        var second = await Provider().HandleWebhookAsync(Context(Body, ComputeSignature(Body, Secret)));

        first.EventId.Should().Be(second.EventId, "le même événement produit la même clé d'idempotence");
    }
}
