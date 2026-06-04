namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>État d'intégrité d'une entrée du coffre (paquet ou addendum).</summary>
/// <param name="EntryId">Identifiant de l'entrée <c>documents.archive_entries</c>.</param>
/// <param name="DocumentId">Document rattaché.</param>
/// <param name="PackagePath">Chemin (relatif au tenant) du manifest de l'entrée.</param>
/// <param name="ContentValid">Le contenu du coffre correspond à l'empreinte de paquet scellée.</param>
/// <param name="ChainValid">Le <c>chain_hash</c> scellé correspond au recalcul du chaînage.</param>
/// <param name="Detail">Message français en cas d'anomalie, ou <c>null</c>.</param>
public sealed record ArchiveIntegrityEntry(
    Guid EntryId,
    Guid DocumentId,
    string PackagePath,
    bool ContentValid,
    bool ChainValid,
    string? Detail);
