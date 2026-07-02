namespace Liakont.Modules.Ged.Contracts.Backfill;

using System;
using System.Collections.Generic;

/// <summary>
/// Projection PLATE d'UNE entrée du corpus fiscal déjà scellé, à indexer rétroactivement dans la GED (GED10,
/// F19 §11 D12). Bâtie par l'orchestrateur de backfill (côté Host, seul à voir l'archive fiscale ET la GED) à partir
/// de <c>documents.archive_entries</c> + <c>documents.documents</c>, puis passée à
/// <see cref="IGedArchivedDocumentBackfill"/> : la GED reste un SILO (elle ne référence jamais les modules fiscaux —
/// frontière F19 §7). Les champs sont des faits BRUTS de la source ; aucune règle fiscale n'est portée ici.
/// </summary>
/// <param name="ArchiveEntryId">
/// Identité de l'entrée de coffre (<c>documents.archive_entries.id</c>) — CLÉ D'IDEMPOTENCE : le backfill dérive une
/// identité GED déterministe de cette valeur, de sorte qu'un re-passage est un no-op (RL-21).
/// </param>
/// <param name="FiscalDocumentId">Identité du document fiscal (<c>documents.documents.id</c>) — soft-link LOGIQUE.</param>
/// <param name="ArchivePath">Chemin du paquet WORM déjà scellé (chaîne fiscale <c>{année}/…</c>).</param>
/// <param name="ContentHash">Empreinte du paquet scellé (recopiée du coffre, jamais recalculée).</param>
/// <param name="DocumentType">Type BRUT du document fiscal — sert à charger un <c>GedMappingProfile</c> validé, ou à DÉFÉRER.</param>
/// <param name="SourceReference">Référence source du document fiscal (titre affiché de l'entrée GED).</param>
/// <param name="SourceFields">Champs BRUTS projetés (nom → valeur) offerts au mapping déclaratif ; jamais nul (vide admis).</param>
/// <param name="SourceTimestampUtc">Horodatage source (émission), ou <see langword="null"/>.</param>
public sealed record GedBackfillDocumentRequest(
    Guid ArchiveEntryId,
    Guid FiscalDocumentId,
    string ArchivePath,
    string ContentHash,
    string DocumentType,
    string SourceReference,
    IReadOnlyDictionary<string, string> SourceFields,
    DateTime? SourceTimestampUtc = null);
