namespace Liakont.Modules.Ingestion.Web;

using System;
using System.Globalization;
using System.Threading;
using Liakont.Modules.Ingestion.Contracts.Commands;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Liakont.Modules.Ingestion.Contracts.Queries;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Common.Abstractions.Audit;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Endpoints de GESTION DU PARC D'AGENTS du tenant pour la console (API05), montés sous
/// <c>/api/v1/agents</c> par le Host. Exposent le cycle de vie post-provisioning (F10 « Gestion des
/// agents » / F12 §4.2) via les handlers EXISTANTS de PIV05 — RIEN n'est dupliqué ni réimplémenté ici :
/// l'endpoint valide l'entrée, dispatch la commande/requête MediatR et journalise l'action.
/// <para>
/// SÉCURITÉ (CLAUDE.md n°3 / n°10) :
/// <list type="bullet">
///   <item>la clé API n'est restituée qu'à l'ÉMISSION (enregistrement / rotation), UNE seule fois
///         (<see cref="AgentKeyIssuedDto.FullKey"/>) — la liste n'expose JAMAIS de clé (ni le clair ni
///         l'empreinte), seulement le préfixe public ;</item>
///   <item>la rotation invalide l'ancienne clé IMMÉDIATEMENT (le domaine remplace l'empreinte) — AUCUNE
///         fenêtre de recouvrement / de grâce : accepter une clé rotée affaiblirait l'authentification
///         de l'agent (F12 §4.2) ;</item>
///   <item>toute l'API exige <c>liakont.settings</c> (gestion de secrets) — un utilisateur « actions »
///         ou « lecture seule » ne gère pas les agents.</item>
/// </list>
/// </para>
/// <para>
/// TENANT-SCOPÉ par construction (CLAUDE.md n°9) : le tenant n'est JAMAIS un paramètre de l'appelant —
/// les handlers PIV05 le résolvent du contexte tenant courant (un opérateur ne voit / ne gère que les
/// agents de SON tenant ; un agent d'un autre tenant est introuvable → 404). Chaque mutation est
/// journalisée avec l'identité de l'opérateur (<see cref="IActivityLogger"/>, module Audit). Le mapping
/// des exceptions de domaine (NotFound → 404, Conflict → 409) est assuré par
/// <c>UseStratumErrorHandling</c> du Host — rien n'est dupliqué ici.
/// </para>
/// </summary>
public static class AgentManagementEndpointMapping
{
    /// <summary>Permission de gestion de secrets du tenant (chaîne : un module ne référence pas le Host — frontière de dépendance).</summary>
    private const string SettingsPermission = "liakont.settings";

    /// <summary>Type d'entité de la piste d'audit pour une opération de gestion d'agent.</summary>
    private const string AgentEntityType = "Agent";

    public static IEndpointRouteBuilder MapAgentManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents");

        // GET /api/v1/agents — liste des agents du tenant courant (nom, état, dernière version vue, dernier
        // heartbeat, dates). Aucune clé exposée (préfixe public seul — AgentSummaryDto).
        group.MapGet(string.Empty, async (ISender sender, CancellationToken ct) =>
        {
            var agents = await sender.Send(new GetAgentsQuery(), ct);
            return Results.Ok(agents);
        }).RequireAuthorization(SettingsPermission);

        // POST /api/v1/agents — enregistre un nouvel agent (RegisterAgent PIV05) et émet sa clé. La clé
        // COMPLÈTE n'est renvoyée qu'ICI, une seule fois (jamais relisible ensuite — F12 §4.2). 201 Created.
        group.MapPost(string.Empty, async (
            RegisterAgentRequest? request,
            ISender sender,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            // Nom obligatoire validé à la frontière → 400 (jamais propagé en ArgumentException du domaine,
            // qui retomberait en 500 : ArgumentException n'est pas une DomainException, cf. ErrorHandlingMiddleware).
            if (request is null || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ActionProblem("Le nom de l'agent est obligatoire."));
            }

            var issued = await sender.Send(new RegisterAgentCommand { Name = request.Name }, ct);

            var actor = actorAccessor.Current;
            await activityLogger.LogActivityAsync(
                AgentEntityType,
                issued.AgentId.ToString(),
                "agents.registered",
                string.Create(CultureInfo.InvariantCulture, $"Agent « {request.Name} » enregistré par l'opérateur (préfixe de clé {issued.KeyPrefix})."),
                ActorId(actor),
                metadata: new { agentName = request.Name, issued.KeyPrefix },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            // La clé complète (FullKey) n'existe que dans CE corps de réponse — à transmettre immédiatement à l'agent.
            return Results.Created($"/api/v1/agents/{issued.AgentId}", issued);
        }).RequireAuthorization(SettingsPermission);

        // POST /api/v1/agents/{id}/revoke — révoque la clé d'un agent (compromission, retrait) : elle est
        // immédiatement refusée à l'ingestion. Idempotent. 404 si l'agent n'existe pas dans le tenant courant.
        group.MapPost("/{id:guid}/revoke", async (
            Guid id,
            ISender sender,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            await sender.Send(new RevokeAgentCommand { AgentId = id }, ct);

            var actor = actorAccessor.Current;
            await activityLogger.LogActivityAsync(
                AgentEntityType,
                id.ToString(),
                "agents.revoked",
                "Agent révoqué par l'opérateur : sa clé est immédiatement refusée à l'ingestion.",
                ActorId(actor),
                metadata: null,
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.NoContent();
        }).RequireAuthorization(SettingsPermission);

        // POST /api/v1/agents/{id}/rotate-key — rotation de clé : la nouvelle clé est renvoyée une seule fois,
        // l'ancienne est invalidée IMMÉDIATEMENT (F12 §4.2 — aucune fenêtre de recouvrement). 404 hors tenant,
        // 409 si l'agent est révoqué (on réenregistre alors un nouvel agent — RotateKey du domaine).
        group.MapPost("/{id:guid}/rotate-key", async (
            Guid id,
            ISender sender,
            IActorContextAccessor actorAccessor,
            IActivityLogger activityLogger,
            CancellationToken ct) =>
        {
            var issued = await sender.Send(new RotateAgentKeyCommand { AgentId = id }, ct);

            var actor = actorAccessor.Current;
            await activityLogger.LogActivityAsync(
                AgentEntityType,
                id.ToString(),
                "agents.key_rotated",
                string.Create(CultureInfo.InvariantCulture, $"Clé de l'agent renouvelée par l'opérateur (nouveau préfixe {issued.KeyPrefix}) : l'ancienne clé est immédiatement invalidée."),
                ActorId(actor),
                metadata: new { issued.KeyPrefix },
                companyId: actor.CompanyId,
                cancellationToken: ct);

            return Results.Ok(issued);
        }).RequireAuthorization(SettingsPermission);

        return app;
    }

    /// <summary>Identité d'audit de l'opérateur (GUID utilisateur ; « system » si non authentifié — théorique sur un endpoint authentifié).</summary>
    private static string ActorId(IActorContext actor) =>
        actor.IsAuthenticated ? actor.UserId.ToString() : "system";

    /// <summary>Corps de la requête d'enregistrement d'un agent.</summary>
    /// <param name="Name">Nom lisible de l'agent (obligatoire — un nom vide est rejeté en 400).</param>
    public sealed record RegisterAgentRequest(string? Name);

    /// <summary>Détail d'erreur d'action (message opérateur en français — CLAUDE.md n°12).</summary>
    public sealed record ActionProblem(string Message);
}
