namespace Liakont.Modules.Reconciliation.Web;

using System;
using System.Collections.Generic;
using System.Threading;
using Liakont.Modules.Reconciliation.Contracts;
using Liakont.Modules.Reconciliation.Contracts.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Endpoints de RÉCONCILIATION PDF de la console (API04), montés sous <c>/api/v1/reconciliation</c> par
/// le Host. Délèguent au module Reconciliation (TRK07) — file d'attente en lecture, affichage d'un PDF,
/// confirmation / rejet d'une proposition, lien manuel PDF ↔ document. Aucune logique métier ici : le
/// rapprochement, l'addendum WORM et la piste d'audit sont du ressort du service (TRK07/TRK05).
/// <para>
/// Permission : <c>liakont.actions</c> (actions opérateur). TENANT-SCOPÉ par construction (la base et le
/// coffre routent vers le tenant courant — blueprint §7, CLAUDE.md n°9). Toute action est journalisée avec
/// l'identité de l'opérateur (DocumentEvent pour un rapprochement, entrée de file pour un rejet).
/// </para>
/// </summary>
public static class ReconciliationEndpointMapping
{
    /// <summary>Permission d'actions opérateur (chaîne : un module ne référence pas le Host — frontière de dépendance).</summary>
    private const string ActionPermission = "liakont.actions";

    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reconciliation");

        // GET /api/v1/reconciliation — la file d'attente : propositions, orphelins, documents sans PDF.
        group.MapGet(string.Empty, async (
            IReconciliationQueries queries,
            CancellationToken ct) =>
        {
            var proposals = await queries.GetPendingProposalsAsync(ct);
            var orphans = await queries.GetOrphanPdfsAsync(ct);
            var documentsWithoutPdf = await queries.GetIssuedDocumentsWithoutPdfAsync(ct);
            return Results.Ok(new ReconciliationQueueResponse
            {
                Proposals = proposals,
                Orphans = orphans,
                DocumentsWithoutPdf = documentsWithoutPdf,
            });
        }).RequireAuthorization(ActionPermission);

        // GET /api/v1/reconciliation/{id}/pdf — contenu du PDF (affichage navigateur).
        group.MapGet("/{id:guid}/pdf", async (
            Guid id,
            IReconciliationQueries queries,
            CancellationToken ct) =>
        {
            ReconciliationPdfContent? pdf = await queries.OpenQueueEntryPdfAsync(id, ct);
            if (pdf is null)
            {
                return Results.NotFound();
            }

            // Results.File DISPOSE le flux après écriture (enableRangeProcessing par défaut désactivé).
            return Results.File(pdf.Content, "application/pdf", pdf.FileName);
        }).RequireAuthorization(ActionPermission);

        // POST /api/v1/reconciliation/{id}/confirm — confirme la proposition (vers le document proposé).
        group.MapPost("/{id:guid}/confirm", async (
            Guid id,
            IReconciliationService service,
            IActorContextAccessor actorContext,
            CancellationToken ct) =>
        {
            await service.ConfirmProposalAsync(id, ResolveOperatorIdentity(actorContext.Current), ct);
            return Results.NoContent();
        }).RequireAuthorization(ActionPermission);

        // POST /api/v1/reconciliation/{id}/reject — rejette la proposition (PDF reclassé en orphelin).
        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            IReconciliationService service,
            IActorContextAccessor actorContext,
            CancellationToken ct) =>
        {
            await service.RejectProposalAsync(id, ResolveOperatorIdentity(actorContext.Current), ct);
            return Results.NoContent();
        }).RequireAuthorization(ActionPermission);

        // POST /api/v1/reconciliation/link — lien manuel PDF (entrée de file) ↔ document choisi par l'opérateur.
        group.MapPost("/link", async (
            LinkPdfRequest request,
            IReconciliationService service,
            IActorContextAccessor actorContext,
            CancellationToken ct) =>
        {
            await service.ConfirmManualReconciliationAsync(
                request.QueueEntryId, request.DocumentId, ResolveOperatorIdentity(actorContext.Current), ct);
            return Results.NoContent();
        }).RequireAuthorization(ActionPermission);

        return app;
    }

    /// <summary>
    /// Identité lisible de l'opérateur courant pour la journalisation : nom affiché, à défaut e-mail, à
    /// défaut identifiant. Lève si aucune identité n'est résolue (endpoint authentifié) plutôt que de
    /// journaliser une action anonyme (CLAUDE.md n°12).
    /// </summary>
    private static string ResolveOperatorIdentity(IActorContext actor)
    {
        if (!string.IsNullOrWhiteSpace(actor.DisplayName))
        {
            return actor.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(actor.Email))
        {
            return actor.Email;
        }

        if (actor.UserId != Guid.Empty)
        {
            return actor.UserId.ToString();
        }

        throw new InvalidOperationException(
            "Identité de l'opérateur introuvable : action de réconciliation impossible sans opérateur (CLAUDE.md n°12).");
    }

    /// <summary>Corps de la requête de lien manuel : l'entrée de file et le document cible.</summary>
    /// <param name="QueueEntryId">Entrée de file d'attente (proposition ou orphelin) à rattacher.</param>
    /// <param name="DocumentId">Document émis cible du rapprochement.</param>
    public sealed record LinkPdfRequest(Guid QueueEntryId, Guid DocumentId);

    /// <summary>Réponse de la file de réconciliation (API04) : les trois catégories de la file d'attente.</summary>
    public sealed record ReconciliationQueueResponse
    {
        /// <summary>Propositions de confiance moyenne en attente de confirmation.</summary>
        public required IReadOnlyList<ReconciliationProposalDto> Proposals { get; init; }

        /// <summary>PDF orphelins (aucune correspondance ou ambiguïté).</summary>
        public required IReadOnlyList<OrphanPdfDto> Orphans { get; init; }

        /// <summary>Documents émis pour lesquels aucun PDF n'a (encore) été rapproché.</summary>
        public required IReadOnlyList<DocumentWithoutPdfDto> DocumentsWithoutPdf { get; init; }
    }
}
