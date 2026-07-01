namespace Liakont.Modules.Archive.Contracts;

using System;
using System.Collections.Generic;

/// <summary>
/// Demande d'archivage d'un document GÉNÉRIQUE (non fiscal) dans le coffre WORM du tenant (F19 §5.1,
/// option C). Le paquet est rangé write-once via <see cref="Liakont.Modules.Archive.Domain.IArchiveStore"/>
/// sous <c>_ged/{kind}/{année}/{mois}/{clé}/</c>, HORS de la chaîne de hashes fiscale
/// (<c>documents.archive_entries</c>) : un document GED-seul n'a AUCUNE ligne d'entrée fiscale
/// (INV-ARCH-GED-1). Symétrique générique d'<see cref="ArchivePackageRequest"/> (facture) — la facture reste
/// sur <see cref="IArchiveService.ArchiveIssuedDocumentAsync"/> (hash inchangé, hash-neutralité structurelle).
/// </summary>
/// <param name="ArchiveKind">Nature GÉNÉRIQUE du rangement (valeur produit — jamais un littéral métier « lot/vente », F19 §7). Premier segment d'arborescence.</param>
/// <param name="ArchiveKey">Clé d'arborescence du paquet (remplace le numéro de document fiscal).</param>
/// <param name="FiledOn">Date de rangement (année/mois de l'arborescence).</param>
/// <param name="Contents">Pièces arbitraires du paquet (au moins une). Contenu conservé exact.</param>
/// <param name="ReadableHtml">Rendu lisible optionnel (aperçu console), ou <c>null</c>.</param>
/// <param name="IndexAxes">Projection plate des axes d'index (RL-19 : valeur nulle si confidentiel).</param>
public sealed record GedArchivePackageRequest(
    string ArchiveKind,
    string ArchiveKey,
    DateOnly FiledOn,
    IReadOnlyList<ArchiveAttachment> Contents,
    string? ReadableHtml,
    IReadOnlyList<ArchiveIndexAxis> IndexAxes);
