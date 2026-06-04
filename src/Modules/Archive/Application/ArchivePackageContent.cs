namespace Liakont.Modules.Archive.Application;

using System.Collections.Generic;

/// <summary>
/// Contenu composé d'un paquet (ou d'un addendum) AVANT scellement : les fichiers de contenu, leur
/// empreinte de paquet (entry_hash) et les pièces absentes. Le <c>manifest.json</c> n'en fait pas partie :
/// il est construit ENSUITE avec le <c>chain_hash</c> et l'horodatage de scellement.
/// </summary>
/// <param name="ContentFiles">Fichiers de contenu (hors manifest).</param>
/// <param name="PackageHash">Empreinte du paquet/addendum (entry_hash, hex minuscule).</param>
/// <param name="AbsentPieces">Pièces optionnelles absentes, avec motif.</param>
public sealed record ArchivePackageContent(
    IReadOnlyList<ArchiveFile> ContentFiles,
    string PackageHash,
    IReadOnlyList<ArchiveAbsentPiece> AbsentPieces);
