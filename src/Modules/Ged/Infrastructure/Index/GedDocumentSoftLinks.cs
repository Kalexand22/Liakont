namespace Liakont.Modules.Ged.Infrastructure.Index;

using System;

/// <summary>
/// Liens souples (LOGIQUES, sans FK cross-schéma — F19 §3.4.1) d'un <c>managed_document</c> vers le corpus fiscal
/// déjà scellé. NULS pour le canal d'ingestion GED pur (GED05b) ; renseignés par le backfill rétroactif (GED10),
/// qui rattache un document GED à la facture (<see cref="FiscalDocumentId"/>) et à son entrée de coffre
/// (<see cref="ArchiveEntryId"/>, clé d'idempotence) sans jamais toucher la chaîne de hashes fiscale.
/// </summary>
internal sealed record GedDocumentSoftLinks(
    Guid? FiscalDocumentId = null,
    Guid? ArchiveEntryId = null,
    string? ArchivePath = null,
    string? ContentHash = null);
