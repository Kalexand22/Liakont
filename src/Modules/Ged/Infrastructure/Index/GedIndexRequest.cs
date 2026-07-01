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
internal sealed record GedIndexRequest(
    Guid ManagedDocumentId,
    IngestedDocumentDto Ingested,
    string Source,
    GedDocumentSoftLinks? SoftLinks = null);
