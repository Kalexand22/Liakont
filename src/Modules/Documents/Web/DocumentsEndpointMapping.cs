namespace Liakont.Modules.Documents.Web;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Endpoints de lecture du module Documents pour la console (API01a), montés sous <c>/api/v1/documents</c>
/// par le Host. Toutes les lectures sont TENANT-SCOPÉES par construction (la connexion EST le tenant —
/// database-per-tenant, blueprint §7 ; CLAUDE.md n°9/17) et exigent la permission <c>liakont.read</c>.
/// Aucune logique métier ici : les endpoints délèguent aux requêtes du module (TRK01).
/// La projection « dernier événement » (motif de blocage / pivot transmis) est une PRÉSENTATION de la piste d'audit en lecture, pas de la logique métier fiscale ou de machine à états (qui reste dans les handlers).
/// </summary>
public static class DocumentsEndpointMapping
{
    /// <summary>
    /// Permission de consultation (canonique : <c>LiakontPermissions.Read</c> dans le Host, cataloguée par
    /// Identity — voir docs/architecture/identity-permissions-liakont.md). Référencée en chaîne car un
    /// projet de module ne référence pas le Host (frontière de dépendance).
    /// </summary>
    private const string ReadPermission = "liakont.read";

    /// <summary>Taille de page par défaut quand l'appelant n'en fournit pas.</summary>
    private const int DefaultPageSize = 50;

    /// <summary>Nom d'événement (DocumentEventType, Domain) portant le motif de blocage agrégé (entrée en Blocked).</summary>
    private const string BlockedEventType = "DocumentBlocked";

    /// <summary>Nom d'événement (DocumentEventType, Domain) d'une re-vérification opérateur toujours bloquée (item FIX02) : porte le motif RÉÉVALUÉ, qui prime comme motif courant quand il est le dernier évalué.</summary>
    private const string RecheckedStillBlockedEventType = "DocumentRecheckedStillBlocked";

    /// <summary>Nom d'événement (DocumentEventType, Domain) portant le snapshot du pivot transmis.</summary>
    private const string IssuedEventType = "DocumentIssued";

    /// <summary>Nom d'état (DocumentState, Domain) « bloqué » — un motif de blocage n'est ACTUEL que dans cet état.</summary>
    private const string BlockedDocumentState = "Blocked";

    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents");

        // GET /api/v1/documents — liste paginée filtrée + compteurs par état pour le bandeau de synthèse.
        // Pattern vide = racine du groupe (/api/v1/documents, sans slash final).
        group.MapGet(string.Empty, async (
            DateOnly? from,
            DateOnly? to,
            string? state,
            string? type,
            string? search,
            int? page,
            int? pageSize,
            IDocumentQueries queries,
            CancellationToken ct) =>
        {
            var filter = new DocumentListFilter
            {
                From = from,
                To = to,
                State = state,
                Type = type,
                Search = search,
                Page = page ?? 1,
                PageSize = pageSize ?? DefaultPageSize,
            };

            var result = await queries.GetDocumentsAsync(filter, ct);
            return Results.Ok(result);
        }).RequireAuthorization(ReadPermission);

        // GET /api/v1/documents/{id} — détail : pivot transmis (snapshot), historique (events),
        // motif de blocage courant, référence d'archive + état d'intégrité (présence WORM).
        group.MapGet("/{id:guid}", async (
            Guid id,
            IDocumentQueries queries,
            CancellationToken ct) =>
        {
            var document = await queries.GetByIdAsync(id, ct);
            if (document is null)
            {
                return Results.NotFound();
            }

            var events = await queries.GetEventsAsync(id, ct);
            var archive = await queries.GetArchiveReferenceAsync(id, ct);

            // Motif courant = le DERNIER événement porteur d'un motif de blocage, qu'il s'agisse de l'entrée en
            // Blocked (DocumentBlocked) ou d'une re-vérification opérateur restée bloquée avec un motif réévalué
            // (DocumentRecheckedStillBlocked, item FIX02) : ainsi l'onglet Contrôles affiche le dernier motif évalué,
            // jamais un motif périmé après un recheck.
            var blockingReason = events
                .Where(e => string.Equals(e.EventType, BlockedEventType, StringComparison.Ordinal)
                    || string.Equals(e.EventType, RecheckedStillBlockedEventType, StringComparison.Ordinal))
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => e.Detail)
                .FirstOrDefault();

            var pivotSnapshot = events
                .Where(e => string.Equals(e.EventType, IssuedEventType, StringComparison.Ordinal))
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => e.PayloadSnapshot)
                .FirstOrDefault();

            var response = new DocumentDetailResponse
            {
                Document = document,
                Events = events,
                BlockingReason = string.Equals(document.State, BlockedDocumentState, StringComparison.Ordinal)
                    ? blockingReason
                    : null,
                PivotSnapshotJson = pivotSnapshot,
                Archive = archive,
                ArchiveIntegrity = archive is null
                    ? ArchiveIntegrityState.NotArchived
                    : ArchiveIntegrityState.Archived,
            };

            return Results.Ok(response);
        }).RequireAuthorization(ReadPermission);

        return app;
    }

    /// <summary>État d'intégrité d'archive exposé dans le détail (présence dans le coffre WORM).</summary>
    private static class ArchiveIntegrityState
    {
        public const string NotArchived = "NotArchived";

        public const string Archived = "Archived";
    }

    /// <summary>
    /// Réponse du détail d'un document (API01a) : la vue complète, sa piste d'audit, le motif de blocage
    /// courant le cas échéant, le snapshot du pivot transmis (si émis) et la référence d'archive.
    /// </summary>
    public sealed record DocumentDetailResponse
    {
        public required DocumentDto Document { get; init; }

        public required IReadOnlyList<DocumentEventDto> Events { get; init; }

        /// <summary>Motif de blocage agrégé (dernier événement <c>DocumentBlocked</c>) UNIQUEMENT si le document est actuellement <c>Blocked</c>, sinon <c>null</c> (un motif périmé sur un document débloqué/émis serait un message opérateur trompeur — CLAUDE.md n°12). L'historique complet reste dans <c>Events</c>.</summary>
        public string? BlockingReason { get; init; }

        /// <summary>Pivot transmis à la Plateforme Agréée (snapshot du dernier événement <c>DocumentIssued</c>), ou <c>null</c>.</summary>
        public string? PivotSnapshotJson { get; init; }

        /// <summary>Référence d'archive WORM du document, ou <c>null</c> s'il n'est pas encore archivé.</summary>
        public ArchiveReferenceDto? Archive { get; init; }

        /// <summary><c>Archived</c> si une entrée de coffre existe, <c>NotArchived</c> sinon.</summary>
        public required string ArchiveIntegrity { get; init; }
    }
}
