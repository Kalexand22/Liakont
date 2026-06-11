namespace Liakont.Modules.Documents.Contracts.Queries;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.DTOs;

/// <summary>
/// Lectures du module Documents pour l'API/console (item TRK01). Toutes les requêtes sont
/// TENANT-SCOPÉES PAR CONSTRUCTION : elles s'exécutent sur la base DU TENANT courant (la connexion EST
/// le tenant — database-per-tenant, blueprint §7) ; aucune requête cross-tenant n'est possible
/// (CLAUDE.md n°9/17).
/// </summary>
public interface IDocumentQueries
{
    /// <summary>Document par identifiant, ou <c>null</c> s'il n'existe pas dans ce tenant.</summary>
    Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Document par numéro (EN 16931 BT-1) dans le tenant, ou <c>null</c>. Le numéro n'est pas unique en
    /// base (un document rejeté peut être remplacé sous un nouveau numéro, l'ancien passant Superseded —
    /// F06 §4, mécanique TRK02/TRK03) : cette lecture retourne le document le PLUS RÉCENT pour ce numéro.
    /// </summary>
    Task<DocumentDto?> GetByNumberAsync(string documentNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents d'un état donné, paginés (file d'attente console). <paramref name="page"/> est 1-basé ;
    /// <paramref name="pageSize"/> est borné par l'implémentation. Triés par dernière mise à jour décroissante.
    /// </summary>
    Task<IReadOnlyList<DocumentSummaryDto>> GetByStateAsync(
        string state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Liste paginée de documents pour la console (API01a, GET /documents) : filtres optionnels (plage de
    /// dates d'émission, état, type, recherche libre), total correspondant aux filtres, et compteurs par
    /// état pour le bandeau de synthèse. Triée par dernière mise à jour décroissante.
    /// </summary>
    Task<DocumentListResult> GetDocumentsAsync(DocumentListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Piste d'audit complète d'un document (ordre chronologique).</summary>
    Task<IReadOnlyList<DocumentEventDto>> GetEventsAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Référence d'archive d'un document (API01a, détail) : la dernière entrée du coffre WORM pour ce
    /// document (TRK05), ou <c>null</c> si le document n'est pas encore archivé. Lecture seule de
    /// <c>documents.archive_entries</c> (même schéma tenant). La vérification cryptographique complète du
    /// coffre est une action à la demande distincte (API03).
    /// </summary>
    Task<ArchiveReferenceDto?> GetArchiveReferenceAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents dont l'envoi est ENGAGÉ mais dont l'issue est INCERTAINE (état <c>Sending</c>) — F06 §5,
    /// item TRK03. Un timeout réseau sur un POST peut masquer une émission qui a RÉUSSI côté Plateforme
    /// Agréée : avant de retenter, le pipeline (PIP01) doit vérifier côté PA, sinon un renvoi crée un
    /// doublon. Cette lecture fournit la liste à raccrocher. Triés par dernière mise à jour décroissante.
    /// </summary>
    Task<IReadOnlyList<DocumentSummaryDto>> GetPotentiallySentDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Le document le PLUS ANCIEN actuellement dans l'état <paramref name="state"/> (le plus petit
    /// <c>last_update_utc</c>), ou <c>null</c> si aucun document n'est dans cet état pour ce tenant.
    /// Lecture d'âge par état pour la supervision (SUP01b — règles « documents bloqués / rejets PA non
    /// traités depuis &gt; N jours », F12 §5.2) : l'appelant dérive l'âge de <c>LastUpdateUtc</c> (temps
    /// passé dans l'état). Bornée à UNE ligne — jamais de pagination de toute la file pour décider d'une
    /// alerte de seuil.
    /// </summary>
    Task<DocumentSummaryDto?> GetOldestDocumentInStateAsync(string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Statut d'un document par (référence source, empreinte du payload), ou <c>null</c> si aucun document
    /// n'existe pour cette clé dans le tenant. Brique du point de statut agent (ADR-0012/0014, élaboré par
    /// PIP01d) : la sémantique Processed/Pending est dérivée de l'état par l'appelant ; cette requête
    /// retourne l'état DURABLE du document le plus récent pour la clé (un renvoi idempotent partage
    /// l'empreinte ; un remplacement après rejet partage la référence source).
    /// </summary>
    Task<DocumentStatusDto?> FindStatusBySourceReferenceAndPayloadHashAsync(
        string sourceReference,
        string payloadHash,
        CancellationToken cancellationToken = default);
}
