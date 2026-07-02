namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;
using Liakont.Agent.Contracts.Ged;

/// <summary>
/// Requête d'indexation d'UN document dans l'index GED (base tenant, schéma <c>ged_index</c>). Portée par le foyer
/// d'écriture unique <see cref="IGedDocumentIndexer"/> : à partir de l'identité GED (<see cref="ManagedDocumentId"/>)
/// et du pivot BRUT (<see cref="Ingested"/>), l'indexeur charge le profil VALIDÉ, applique <c>GedMapper</c> (mappé ou
/// DÉFÉRÉ), et écrit sous garde de concurrence (idempotence RL-04).
/// </summary>
/// <param name="ManagedDocumentId">Identité GED du document (clé primaire de <c>managed_documents</c>).</param>
/// <param name="Ingested">Le pivot GED BRUT à mapper (canal d'ingestion ou projection d'un document fiscal archivé).</param>
/// <param name="Source">
/// Provenance des liens écrits (<c>ck_dal_source</c> : <c>agent</c> pour l'ingestion GED, <c>import</c> pour le
/// backfill rétroactif) — jamais inventée, portée par l'appelant.
/// </param>
/// <param name="SoftLinks">Liens souples vers le corpus fiscal (backfill GED10) ; <see langword="null"/> pour le canal GED pur.</param>
/// <param name="ResumeDeferred">
/// Reprise CIBLÉE d'un document déjà rangé <c>deferred</c> (GDF10) : <see langword="true"/> autorise l'indexeur à
/// RE-MAPPER un document <c>deferred</c> qui mappe désormais (profil créé+validé depuis le 1er passage) et à le PROMOUVOIR
/// <c>deferred</c>→<c>indexed</c>. Réservé au canal BACKFILL (<see cref="Liakont.Modules.Ged.Infrastructure.Backfill.GedArchivedDocumentBackfill"/>),
/// dont le job Host re-énumère tout le corpus à chaque run : un re-run reprend les déférés devenus mappables. Le canal
/// d'ingestion GED05b laisse <see langword="false"/> (sémantique de replay INCHANGÉE : tout statut existant = no-op, RL-04).
/// Un statut TERMINAL <c>indexed</c> reste TOUJOURS un no-op (idempotence conservée), quel que soit ce drapeau.
/// </param>
internal sealed record GedIndexRequest(
    Guid ManagedDocumentId,
    IngestedDocumentDto Ingested,
    string Source,
    GedDocumentSoftLinks? SoftLinks = null,
    bool ResumeDeferred = false);
