namespace Liakont.Host.AgentApi;

using Liakont.Agent.Contracts.Transport;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.Queries;
using Liakont.Modules.Pipeline.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoints de l'API agent → plateforme (contrat d'ingestion, F12 §3.2). Groupe
/// <c>/api/agent/v1</c>, distinct de l'API console <c>/api/v{version}</c> (OIDC) : l'agent
/// s'authentifie par clé API (en-tête <c>X-Agent-Key</c>) via <see cref="AgentApiAuthenticationFilter"/>,
/// jamais par OIDC. L'ingestion des documents (POST documents/batch, PDF) est livrée par PIV04 sur
/// ce même groupe (elle hérite du filtre d'authentification).
/// </summary>
internal static class AgentApiEndpoints
{
    /// <summary>Politique de rate limiting des endpoints LÉGERS (heartbeat/configuration) — anti-flood par IP.</summary>
    public const string RateLimiterPolicy = "agent-api";

    /// <summary>
    /// Politique de rate limiting de l'INGESTION (batch + PDF) : gros débit (drainage de backlog),
    /// dimensionnée plus largement que l'anti-flood pour ne jamais rejeter un drainage légitime (PIV04).
    /// </summary>
    public const string IngestionRateLimiterPolicy = "agent-api-ingestion";

    /// <summary>Taille maximale d'un lot (F12 §3.3) : au-delà → 413 Payload Too Large.</summary>
    public const int MaxBatchDocuments = 100;

    public static IEndpointRouteBuilder MapAgentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agent/v1");

        // L'agent ne s'authentifie pas par OIDC mais par clé API (filtre ci-dessous) ; AllowAnonymous
        // écarte le pipeline d'autorisation console. Le rate limiting est posé PAR ENDPOINT (les
        // endpoints d'ingestion utilisent une politique distincte, dimensionnée pour le débit — PIV04).
        group.AllowAnonymous();
        group.AddEndpointFilter<AgentApiAuthenticationFilter>();

        // POST /api/agent/v1/heartbeat — état de l'agent → réponse : heure serveur + configuration.
        group.MapPost("/heartbeat", async (
            HeartbeatRequestDto request,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            var identity = AgentApiContext.GetIdentity(http);

            // Corps validé à la frontière : un corps malformé (champ obligatoire absent → null)
            // est rejeté en 400, jamais propagé en violation NOT NULL (500) à l'insert.
            if (string.IsNullOrWhiteSpace(request.AgentVersion))
            {
                return Results.BadRequest("Le champ agentVersion est obligatoire.");
            }

            // La version de contrat PERSISTÉE est celle NÉGOCIÉE (en-tête déjà validé par le filtre),
            // pas celle du corps : les deux pourraient diverger, l'en-tête fait foi.
            var contractVersion = http.Request.Headers[AgentApiHeaders.ContractVersion].ToString();

            var response = await sender.Send(
                new RecordHeartbeatCommand
                {
                    AgentId = identity.AgentId,
                    ContractVersion = contractVersion,
                    AgentVersion = request.AgentVersion,
                    SentAtUtc = request.SentAtUtc,
                    LastSuccessfulSyncUtc = request.LastSuccessfulSyncUtc,
                },
                ct);
            return Results.Ok(response);
        }).RequireRateLimiting(RateLimiterPolicy);

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
        }).RequireRateLimiting(RateLimiterPolicy);

        // POST /api/agent/v1/documents/batch — push d'un lot de documents pivot (PIV04). Résultat
        // INDIVIDUEL par document (jamais de rejet global du lot pour un seul document invalide).
        group.MapPost("/documents/batch", async (
            PushBatchRequestDto? request,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            // Corps absent ou non conforme aux DTOs → 400 (jamais d'acceptation partielle d'un lot malformé).
            if (request is null)
            {
                return Results.BadRequest("Corps de requête absent ou non conforme au contrat.");
            }

            // Lot trop gros → 413 (limite imposée par l'ingestion, pas par le DTO — F12 §3.3).
            if (request.Documents.Count > MaxBatchDocuments)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var identity = AgentApiContext.GetIdentity(http);
            var contractVersion = http.Request.Headers[AgentApiHeaders.ContractVersion].ToString();

            var response = await sender.Send(
                new IngestDocumentBatchCommand
                {
                    AgentId = identity.AgentId,
                    TenantId = identity.TenantId,
                    ContractVersion = contractVersion,
                    Documents = request.Documents,
                    SourceTaxRegimes = request.SourceTaxRegimes,
                },
                ct);
            return Results.Ok(response);
        }).RequireRateLimiting(IngestionRateLimiterPolicy);

        // GET /api/agent/v1/documents/status — point de statut de prise en charge (ADR-0012, PIP01d).
        // Clé (sourceReference, payloadHash), lecture seule, tenant-scopé (le filtre a posé le contexte tenant).
        // CONTRAT : une clé inconnue sur cette route EXISTANTE répond 200 + Pending, JAMAIS 404 (404 réservé à
        // une route absente) — l'agent ne purge sa copie locale que sur un statut TERMINAL (Processed/Rejected).
        group.MapGet("/documents/status", async (
            string? sourceReference,
            string? payloadHash,
            HttpContext http,
            ISender sender,
            CancellationToken ct) =>
        {
            // Garantit que le filtre d'authentification a posé l'identité + le contexte tenant de la requête.
            _ = AgentApiContext.GetIdentity(http);

            if (string.IsNullOrWhiteSpace(sourceReference) || string.IsNullOrWhiteSpace(payloadHash))
            {
                return Results.BadRequest("Les paramètres sourceReference et payloadHash sont obligatoires.");
            }

            var status = await sender.Send(
                new GetDocumentIntakeStatusQuery { SourceReference = sourceReference, PayloadHash = payloadHash },
                ct);
            return Results.Ok(status);
        }).RequireRateLimiting(RateLimiterPolicy);

        // POST /api/agent/v1/documents/{sourceReference}/pdf — PDF RATTACHÉ à un document (par tenant).
        group.MapPost("/documents/{sourceReference}/pdf", async (
            string sourceReference,
            HttpContext http,
            IIngestedPdfStore pdfStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sourceReference))
            {
                return Results.BadRequest("La référence source est obligatoire.");
            }

            var identity = AgentApiContext.GetIdentity(http);
            await pdfStore.SaveLinkedPdfAsync(identity.TenantId, sourceReference, http.Request.Body, ct);
            return Results.Ok();
        }).RequireRateLimiting(IngestionRateLimiterPolicy);

        // POST /api/agent/v1/pdf-pool — PDF NON RATTACHÉ → pool de réconciliation du tenant (F06/TRK07).
        group.MapPost("/pdf-pool", async (
            HttpContext http,
            IIngestedPdfStore pdfStore,
            CancellationToken ct,
            string? fileName) =>
        {
            var identity = AgentApiContext.GetIdentity(http);
            await pdfStore.SavePooledPdfAsync(identity.TenantId, fileName ?? "document.pdf", http.Request.Body, ct);
            return Results.Ok();
        }).RequireRateLimiting(IngestionRateLimiterPolicy);

        return app;
    }
}
