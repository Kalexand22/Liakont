namespace Liakont.Modules.Archive.Application;

using System;

/// <summary>Une entrée scellée du coffre (ligne <c>documents.archive_entries</c>).</summary>
/// <param name="EntryId">Identifiant de la ligne.</param>
/// <param name="DocumentId">Document rattaché.</param>
/// <param name="PackagePath">Chemin (relatif au tenant) du manifest de l'entrée.</param>
/// <param name="PackageHash">Empreinte du paquet/addendum.</param>
/// <param name="ChainHash">Maillon de chaîne.</param>
/// <param name="ArchivedUtc">Horodatage d'archivage (UTC).</param>
public sealed record ArchiveEntryRecord(
    Guid EntryId,
    Guid DocumentId,
    string PackagePath,
    string PackageHash,
    string ChainHash,
    DateTimeOffset ArchivedUtc);
