namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Ingestion.Contracts;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Implémentation de <see cref="IDocumentDetailConsoleQueries"/> : assemble la vue détail à partir des
/// lectures du module Documents (<see cref="IDocumentQueries.GetByIdAsync"/> + <c>GetEventsAsync</c> +
/// <c>GetArchiveReferenceAsync</c>), à l'identique de l'endpoint <c>GET /api/v1/documents/{id}</c>
/// (DocumentsEndpointMapping). Aucune règle métier : la projection « dernier événement de blocage » et la
/// présence d'archive sont de la PRÉSENTATION de la piste d'audit en lecture (pas de fiscalité, pas de
/// machine à états — celles-ci restent dans les handlers/domaines). Tenant-scopée (la connexion EST le tenant).
/// </summary>
internal sealed partial class DocumentDetailConsoleQueryService : IDocumentDetailConsoleQueries
{
    // Noms d'événements/d'état du domaine Documents, référencés en CHAÎNE (un projet du Host ne référence pas
    // le Domain d'un module — frontière de dépendance) ; mêmes valeurs que DocumentsEndpointMapping.
    private const string BlockedEventType = "DocumentBlocked";
    private const string RecheckedStillBlockedEventType = "DocumentRecheckedStillBlocked";
    private const string BlockedDocumentState = "Blocked";
    private const string EReportedDocumentState = "EReported";

    private readonly IDocumentQueries _documents;
    private readonly IDocumentContentReplayService _contentReplay;
    private readonly IB2cMarginEmissionQueries _emissions;
    private readonly ISender _sender;
    private readonly IIngestedPdfStore _pdfStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DocumentDetailConsoleQueryService> _logger;

    public DocumentDetailConsoleQueryService(
        IDocumentQueries documents,
        IDocumentContentReplayService contentReplay,
        IB2cMarginEmissionQueries emissions,
        ISender sender,
        IIngestedPdfStore pdfStore,
        ITenantContext tenantContext,
        ILogger<DocumentDetailConsoleQueryService> logger)
    {
        _documents = documents;
        _contentReplay = contentReplay;
        _emissions = emissions;
        _sender = sender;
        _pdfStore = pdfStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documents.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var events = await _documents.GetEventsAsync(id, cancellationToken).ConfigureAwait(false);
        var archive = await _documents.GetArchiveReferenceAsync(id, cancellationToken).ConfigureAwait(false);

        // Motif de blocage = le DERNIER événement porteur d'un motif (entrée en Blocked, OU re-vérification
        // opérateur restée bloquée avec un motif réévalué — DocumentRecheckedStillBlocked, item FIX02), et
        // seulement si le document est ENCORE bloqué (sinon le motif est périmé → message trompeur). Ainsi
        // l'onglet Contrôles affiche le DERNIER motif évalué après un recheck, plus un motif périmé. Tri stable
        // par horodatage puis Id.
        var blockingReason = string.Equals(document.State, BlockedDocumentState, StringComparison.Ordinal)
            ? events
                .Where(e => string.Equals(e.EventType, BlockedEventType, StringComparison.Ordinal)
                    || string.Equals(e.EventType, RecheckedStillBlockedEventType, StringComparison.Ordinal))
                .OrderByDescending(e => e.TimestampUtc)
                .ThenByDescending(e => e.Id)
                .Select(e => e.Detail)
                .FirstOrDefault()
            : null;

        // Détail ligne à ligne (onglet Contenu, FIX205 + BUG-5) : on l'expose DÈS que le document est lu/contrôlé
        // (états Bloqué / Prêt-à-envoyer), pas seulement après transmission — c'est là qu'on diagnostique un
        // blocage. Le module Pipeline relit le pivot SOURCE stagé et REJOUE le mapping via la SOURCE UNIQUE
        // (CheckTvaMapping) : pivot enrichi si le mapping passe, pivot source (régime lu, catégorie/VATEX vides)
        // s'il bloque (diagnostic factuel, jamais deviné). Frontière respectée : on consomme la query Contracts.
        var (content, pivot) = await BuildContentAsync(id, events, cancellationToken).ConfigureAwait(false);

        // Récap de marge (aide à la déclaration de TVA, art. 297 E) : calcul fiscal DÉLÉGUÉ au module Pipeline
        // (cœurs e-reporting réutilisés) sur le MÊME pivot que le contenu affiché. null hors régime de la marge.
        var marginRecap = await ResolveMarginRecapAsync(id, pivot, cancellationToken).ConfigureAwait(false);

        var eReportedBatchId = await ResolveEReportedBatchIdAsync(id, document.State, cancellationToken).ConfigureAwait(false);

        var hasSourcePdf = await ResolveHasSourcePdfAsync(document.SourceReference, cancellationToken).ConfigureAwait(false);

        return new DocumentDetailViewModel
        {
            Document = document,
            Events = events,
            BlockingReason = blockingReason,
            Content = content,
            MarginRecap = marginRecap,
            EReportedBatchId = eReportedBatchId,
            HasSourcePdf = hasSourcePdf,
            Archive = archive,
            IsArchived = archive is not null,
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to replay document content for document {DocumentId}; falling back to the transmitted snapshot.")]
    private static partial void LogContentReplayFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resolve the e-reporting emission batch for document {DocumentId}; the deep link is omitted (the detail remains available).")]
    private static partial void LogEReportedBatchResolutionFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to resolve the margin recap for document {DocumentId}; the recap is omitted (the detail remains available).")]
    private static partial void LogMarginRecapFailed(ILogger logger, Guid documentId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to probe the ingested source PDF; the attachment link is omitted (the detail remains available).")]
    private static partial void LogSourcePdfProbeFailed(ILogger logger, Exception exception);

    /// <summary>
    /// Sonde la présence du PDF d'origine (poussé par l'agent, module Ingestion) pour proposer le lien
    /// « pièce jointe » — jamais de lien mort. AIDE AUXILIAIRE (miroir du récap de marge) : une panne de
    /// la sonde n'expose pas le lien mais ne casse JAMAIS le détail ; seule l'annulation se propage.
    /// Tenant-scopée : le stockage est borné au tenant résolu du circuit (CLAUDE.md n°9).
    /// </summary>
    private async Task<bool> ResolveHasSourcePdfAsync(string sourceReference, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            // Pas de tenant résolu (super-admin cross-tenant, prérendu) : pas de stockage à sonder.
            return false;
        }

        try
        {
            return await _pdfStore.LinkedPdfExistsAsync(tenantId, sourceReference, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSourcePdfProbeFailed(_logger, ex);
            return false;
        }
    }

    // Lien « Voir la déclaration » (fiche → /emissions-marge-b2c/{lot}) : le LOT d'émission auquel appartient le
    // document e-reporté est lu depuis la SOURCE DE VÉRITÉ de la liaison — le journal d'émission B2C
    // (pipeline.b2c_margin_emissions.emission_batch_id, via la query Contracts). Cette liaison existe pour TOUT
    // document e-reporté, qu'il l'ait été par le job (frais) OU par le backfill V012 (RÉTROACTIF, sans événement
    // d'audit) — contrairement à l'événement DocumentEReported, absent des documents rétro-corrigés. On ne résout QUE
    // si l'état persisté est EReported (une seule vérité : documents.state). PRÉSENTATION pure : une panne de lecture
    // n'expose pas le lien mais ne casse JAMAIS le détail (miroir du récap de marge) ; seule l'annulation se propage.
    private async Task<Guid?> ResolveEReportedBatchIdAsync(Guid id, string state, CancellationToken cancellationToken)
    {
        if (!string.Equals(state, EReportedDocumentState, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return await _emissions.GetEmissionBatchIdForDocumentAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEReportedBatchResolutionFailed(_logger, id, ex);
            return null;
        }
    }

    /// <summary>
    /// Projette le contenu ligne à ligne (FIX205 + BUG-5). PRIORITÉ au pivot RÉELLEMENT TRANSMIS (snapshot porté
    /// par le dernier événement d'envoi) : pour un document émis/refusé, le détail affiché DOIT être la VÉRITÉ
    /// transmise à la Plateforme Agréée — jamais une re-dérivation depuis la table de mapping COURANTE (qui a pu
    /// changer depuis l'émission : catégorie/VATEX/taux affichés divergeraient de l'envoi — inacceptable pour un
    /// produit de conformité ; et le staging source peut survivre brièvement à l'émission avant sa purge WORM).
    /// EN L'ABSENCE de snapshot (document non transmis : Bloqué / Prêt-à-envoyer / détecté), on REJOUE le mapping
    /// read-time sur le pivot source stagé pour rendre les lignes visibles AVANT transmission (BUG-5) — c'est là
    /// qu'on diagnostique un blocage. Un échec du rejeu n'expose JAMAIS de ligne inventée : la vue affiche sa note.
    /// </summary>
    private async Task<(DocumentContentView Content, PivotDocumentDto? Pivot)> BuildContentAsync(
        Guid id,
        IReadOnlyList<DocumentEventDto> events,
        CancellationToken cancellationToken)
    {
        // Pivot RÉELLEMENT transmis, porté par le DERNIER événement d'envoi (DocumentIssued, ou DocumentRejected) :
        // seuls ces événements portent un PayloadSnapshot. Tri stable par horodatage puis Id (même ordre que la
        // projection « dernier événement » ci-dessus et l'endpoint).
        var transmittedPivotJson = events
            .Where(e => !string.IsNullOrWhiteSpace(e.PayloadSnapshot))
            .OrderByDescending(e => e.TimestampUtc)
            .ThenByDescending(e => e.Id)
            .Select(e => e.PayloadSnapshot)
            .FirstOrDefault();

        // Document TRANSMIS : on affiche EXACTEMENT ce qui a été envoyé (vérité d'audit), pas un rejeu re-dérivé.
        // On expose AUSSI le pivot transmis (pour le récap de marge) — même source que les lignes affichées.
        if (!string.IsNullOrWhiteSpace(transmittedPivotJson))
        {
            var transmittedPivot = DocumentLineProjection.TryReadTransmittedPivot(transmittedPivotJson);
            return (DocumentLineProjection.FromPivot(transmittedPivot), transmittedPivot);
        }

        // Document NON transmis : rejeu read-time du pivot source stagé (BUG-5) pour rendre les lignes visibles dès
        // les états Bloqué / Prêt-à-envoyer. Le rejeu est un CONFORT d'affichage : son échec (mapping/staging) ne
        // doit jamais casser le détail — on le trace, la vue bascule alors sur sa note (jamais de ligne inventée).
        try
        {
            var replay = await _contentReplay.ReplayAsync(id, cancellationToken).ConfigureAwait(false);
            if (replay.Available)
            {
                return (DocumentLineProjection.FromPivot(replay.MappedPivot), replay.MappedPivot);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // On NE journalise PAS une annulation de requête (navigation / déconnexion Blazor) comme une erreur :
            // l'OperationCanceledException se propage (même convention que FromTransmittedSnapshot, qui ne l'avale pas).
            LogContentReplayFailed(_logger, id, ex);
        }

        return (DocumentContentView.Empty, null);
    }

    /// <summary>
    /// Récap de marge du document via le module Pipeline (<see cref="GetDocumentMarginRecapQuery"/>) sur le pivot
    /// affiché. Pure PRÉSENTATION : aucune fiscalité ici (le handler résout régime + marge). <c>null</c> si pas de
    /// pivot ou document hors régime de la marge. Le récap est une aide AUXILIAIRE : sa panne (mapping/tenant) ne doit
    /// JAMAIS casser la lecture du détail (vérité d'audit) — on attrape, on trace, on l'omet (miroir du rejeu de
    /// contenu) ; seule l'annulation de requête se propage (navigation / déconnexion Blazor).
    /// </summary>
    private async Task<MarginRecapView?> ResolveMarginRecapAsync(Guid id, PivotDocumentDto? pivot, CancellationToken cancellationToken)
    {
        if (pivot is null)
        {
            return null;
        }

        try
        {
            var recap = await _sender.Send(new GetDocumentMarginRecapQuery { Pivot = pivot }, cancellationToken).ConfigureAwait(false);
            if (recap is null)
            {
                return null;
            }

            return new MarginRecapView
            {
                BuyerFeesTtc = recap.BuyerFeesTtc,
                SellerFeesTtc = recap.SellerFeesTtc,
                MarginTtc = recap.MarginTtc,
                BaseHt = recap.BaseHt,
                Tva = recap.Tva,
                RatePercent = recap.RatePercent,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMarginRecapFailed(_logger, id, ex);
            return null;
        }
    }
}
