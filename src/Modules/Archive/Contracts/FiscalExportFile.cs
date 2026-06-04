namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// Un fichier du dossier d'export contrôle fiscal (TRK06). L'export est un ensemble de fichiers
/// autonomes (paquets d'archive, rapport d'intégrité, preuves d'ancrage, chronologie, notice) que l'API
/// (API03) assemble en archive téléchargeable pour le vérificateur.
/// </summary>
/// <param name="Path">Chemin relatif dans le dossier d'export (ex. <c>2026/05/F-2026-001/payload.json</c>).</param>
/// <param name="ContentType">Type MIME du fichier.</param>
/// <param name="Content">Octets du fichier.</param>
public sealed record FiscalExportFile(string Path, string ContentType, byte[] Content);
