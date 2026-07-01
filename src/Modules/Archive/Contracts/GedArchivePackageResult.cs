namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Résultat de l'archivage d'un document GED (F19 §5.1, option C). DISTINCT d'<see cref="ArchivePackageResult"/>
/// (facture) : un document GED-seul n'a NI entrée <c>documents.archive_entries</c>, NI maillon de chaîne de
/// hashes fiscale — il ne porte donc NI <c>EntryId</c> NI <c>ChainHash</c> (les fabriquer serait faux et
/// laisserait croire à un chaînage fiscal inexistant). Son intégrité de référence en V1 = rangement write-once
/// WORM + <see cref="ContentHash"/> indexé (§3.4.1) ; la valeur probante renforcée est déférée au coffre tiers
/// (fast-follow GED20).
/// </summary>
/// <param name="ArchivePath">Chemin (relatif au tenant) du manifest du paquet dans le coffre (sous <c>_ged/…</c>).</param>
/// <param name="ContentHash">Empreinte SHA-256 (hex minuscule) du contenu du paquet — reportée dans <c>managed_documents.content_hash</c> par l'appelant.</param>
/// <param name="ArchivedUtc">Horodatage de rangement (UTC).</param>
/// <param name="AlreadyArchived">Vrai si un paquet identique était déjà rangé (re-rangement idempotent, no-op).</param>
public sealed record GedArchivePackageResult(
    string ArchivePath,
    string ContentHash,
    DateTimeOffset ArchivedUtc,
    bool AlreadyArchived);
