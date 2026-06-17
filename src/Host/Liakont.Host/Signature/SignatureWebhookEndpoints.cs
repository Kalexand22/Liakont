namespace Liakont.Host.Signature;

using System.IO;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Endpoint de réception des webhooks de signature (ADR-0029 §2/§4). <c>POST
/// /webhooks/signature/{providerType}/{opaqueRef}</c>, anonyme (la vraie garde est le HMAC PAR TENANT, pas
/// l'OIDC). Séquence STRICTE (INV-YOUSIGN-3/4) : (1) lire le RAW body ; (2) router par handle OPAQUE via le
/// catalogue SYSTÈME (aucun lookup métier cross-tenant pré-scope) ; (3) ouvrir le scope tenant ; (4) charger
/// le compte + secret DU tenant ; (5) vérifier le HMAC (signature falsifiée → rejet AVANT tout traitement) ;
/// (6) persister l'événement authentifié dans l'inbox DURABLE AVANT de répondre 2xx (un crash après 2xx ne
/// perd pas l'événement). Le traitement lourd (download preuve + WORM) est asynchrone (drain).
/// </summary>
internal static class SignatureWebhookEndpoints
{
    /// <summary>Politique de rate limiting anti-flood par IP (le HMAC reste le vrai rempart).</summary>
    public const string RateLimiterPolicy = "signature-webhook";

    public static IEndpointRouteBuilder MapSignatureWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/signature/{providerType}/{opaqueRef}", HandleAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimiterPolicy);

        return app;
    }

    internal static async Task<IResult> HandleAsync(
        string providerType,
        string opaqueRef,
        HttpContext http,
        ISignatureWebhookRouteCatalog routes,
        ITenantScopeFactory scopeFactory,
        CancellationToken ct)
    {
        var rawBody = await ReadRawBodyAsync(http.Request, ct).ConfigureAwait(false);

        // Routage par handle OPAQUE sur le catalogue SYSTÈME, AVANT toute ouverture de scope (INV-YOUSIGN-3).
        // Une route inconnue, ou un type de provider qui ne correspond pas à la route, → 404 (jamais de fuite).
        var route = await routes.ResolveAsync(opaqueRef, ct).ConfigureAwait(false);
        if (route is null || !string.Equals(route.ProviderType, providerType, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        await using var scope = scopeFactory.Create(route.TenantId);

        // Compte + secret chargés DANS le scope du tenant (jamais pré-scope — INV-YOUSIGN-3).
        var accounts = scope.Services.GetRequiredService<ISignatureAccountStore>();
        var account = await accounts.GetActiveAccountAsync(route.CompanyId, providerType, ct).ConfigureAwait(false);
        if (account is null)
        {
            return Results.NotFound();
        }

        var registry = scope.Services.GetRequiredService<ISignatureProviderRegistry>();
        var provider = registry.Resolve(account);

        var context = new SignatureWebhookContext
        {
            RawBody = rawBody,
            Headers = ProjectHeaders(http.Request),
            TenantHandle = opaqueRef,
        };

        // Vérification HMAC + parsing DANS le plug-in (HMAC interne sur le RAW body — INV-YOUSIGN-2/3).
        var result = await provider.HandleWebhookAsync(context, ct).ConfigureAwait(false);

        switch (result.State)
        {
            case SignatureWebhookState.Rejected:
                // HMAC invalide : rejeté AVANT tout traitement (jamais persisté).
                return Results.Unauthorized();

            case SignatureWebhookState.CapabilityNotSupported:
                return Results.BadRequest("Ce fournisseur de signature ne gère pas les webhooks.");

            case SignatureWebhookState.Ignored:
                return Results.Ok();

            case SignatureWebhookState.Accepted:
                if (string.IsNullOrWhiteSpace(result.EventId) || string.IsNullOrWhiteSpace(result.ProviderReference))
                {
                    // Authentifié mais sans clé d'idempotence/corrélation exploitable : rien à persister.
                    return Results.Ok();
                }

                // Persistance DURABLE AVANT 2xx (idempotente sur (company_id, provider_type, event_id)).
                var inbox = scope.Services.GetRequiredService<ISignatureWebhookInbox>();
                await inbox.EnqueueAsync(
                    new SignatureWebhookInboxItem
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = route.CompanyId,
                        ProviderType = providerType,
                        EventId = result.EventId,
                        ProviderReference = result.ProviderReference,
                        RawBody = rawBody,
                        ReceivedAtUtc = DateTimeOffset.UtcNow,
                    },
                    ct).ConfigureAwait(false);

                return Results.Accepted();

            default:
                return Results.Ok();
        }
    }

    private static async Task<byte[]> ReadRawBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static Dictionary<string, string> ProjectHeaders(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        return headers;
    }
}
