namespace Liakont.Host.AgentApi;

using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoints de l'API agent → plateforme (contrat d'ingestion, F12 §3.2). Groupe
/// <c>/api/agent/v1</c>, distinct de l'API console <c>/api/v{version}</c> (OIDC) : l'agent
/// s'authentifie par clé API (en-tête <c>X-Agent-Key</c>) via <see cref="AgentApiAuthenticationFilter"/>,
/// jamais par OIDC. L'ingestion des documents (POST documents/batch, PDF) est livrée par PIV04 sur
/// ce même groupe (elle hérite du filtre d'authentification et du rate limiting).
/// </summary>
internal static class AgentApiEndpoints
{
    /// <summary>Nom de la politique de rate limiting protégeant l'API agent (brute force par IP).</summary>
    public const string RateLimiterPolicy = "agent-api";

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent/v1");

        // L'agent ne s'authentifie pas par OIDC mais par clé API (filtre ci-dessous) ; AllowAnonymous
        // écarte le pipeline d'autorisation console. Rate limiting (brute force par IP) sur tout le groupe.
        group.AllowAnonymous();
        group.RequireRateLimiting(RateLimiterPolicy);
        group.AddEndpointFilter<AgentApiAuthenticationFilter>();

        // POST /api/agent/v1/heartbeat — état de l'agent → réponse : heure serveur + configuration.
        group.MapPost("/heartbeat", async (
            HeartbeatRequestDto request,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var identity = AgentApiContext.GetIdentity(http);
            var response = await sender.Send(
                new RecordHeartbeatCommand { AgentId = identity.AgentId, Heartbeat = request },
                ct);
            return Results.Ok(response);
        });

        // GET /api/agent/v1/configuration — configuration courante (pour le démarrage de l'agent).
        group.MapGet("/configuration", async (
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var identity = AgentApiContext.GetIdentity(http);
            var configuration = await sender.Send(
                new GetAgentConfigurationQuery { TenantId = identity.TenantId },
                ct);
            return Results.Ok(configuration);
        });

        return app;
    }
}
