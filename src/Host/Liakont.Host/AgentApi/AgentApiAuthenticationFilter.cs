namespace Liakont.Host.AgentApi;

using Liakont.Agent.Contracts.Transport;
using Liakont.Host.MultiTenancy;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Filtre d'authentification de l'API agent (F12 §3). Appliqué à TOUT le groupe <c>/api/agent/v1</c>
/// (heartbeat, configuration, et — à venir — l'ingestion des documents en PIV04). Il :
/// <list type="number">
///   <item>négocie la version du contrat (en-tête <c>X-Contract-Version</c>) → <c>426</c> si inconnue/trop ancienne ;</item>
///   <item>authentifie la clé API (en-tête <c>X-Agent-Key</c>) → <c>401</c> (invalide) / <c>403</c> (révoquée) ;</item>
///   <item>résout le tenant de l'agent et le pose dans le contexte tenant de la requête, puis publie
///         l'identité de l'agent pour les endpoints.</item>
/// </list>
/// Les services scopés sont résolus depuis <see cref="HttpContext.RequestServices"/> (le filtre
/// lui-même est instancié une fois par l'hôte). C'est la « résolution du tenant » que PIV04 réutilise.
/// </summary>
internal sealed class AgentApiAuthenticationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // 1. Négociation de version du contrat (F12 §3.3). Inconnue/absente/trop ancienne → 426.
        var contractVersion = http.Request.Headers[AgentApiHeaders.ContractVersion].ToString();
        if (!AgentContractVersionPolicy.IsSupported(contractVersion))
        {
            return Results.StatusCode(StatusCodes.Status426UpgradeRequired);
        }

        // 2. Authentification par clé API (F12 §3.1).
        var presentedKey = http.Request.Headers[AgentApiHeaders.AgentKey].ToString();
        var authenticator = http.RequestServices.GetRequiredService<IAgentAuthenticator>();
        var result = await authenticator.AuthenticateAsync(presentedKey, http.RequestAborted);

        switch (result.Outcome)
        {
            case AgentAuthenticationOutcome.Authenticated:
                var identity = result.Identity!;

                // 3. Pose le contexte tenant (seul le Host mute MutableTenantContext) : les opérations
                // tenant-scopées aval (ingestion PIV04) routent vers la base du bon tenant.
                var tenantContext = http.RequestServices.GetRequiredService<MutableTenantContext>();
                tenantContext.TenantId = identity.TenantId;
                AgentApiContext.SetIdentity(http, identity);

                return await next(context);

            case AgentAuthenticationOutcome.Revoked:
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            default:
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }
    }
}
