namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Résultat de l'archivage d'un paquet ou d'un addendum : l'entrée scellée en base
/// (<c>documents.archive_entries</c>, alimentée par TRK05) et les empreintes du maillon de chaîne créé.
/// </summary>
/// <param name="EntryId">Identifiant de l'entrée <c>documents.archive_entries</c>.</param>
/// <param name="DocumentId">Identifiant du document archivé.</param>
/// <param name="PackagePath">Chemin (relatif au tenant) du manifest de l'entrée dans le coffre.</param>
/// <param name="PackageHash">Empreinte du paquet/addendum (hex minuscule).</param>
/// <param name="ChainHash">Maillon de chaîne <c>chain_hash(N)</c> (hex minuscule).</param>
/// <param name="ArchivedUtc">Horodatage d'archivage (UTC).</param>
public sealed record ArchivePackageResult(
    Guid EntryId,
    Guid DocumentId,
    string PackagePath,
    string PackageHash,
    string ChainHash,
    DateTimeOffset ArchivedUtc);
