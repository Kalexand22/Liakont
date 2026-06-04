namespace Liakont.Modules.Archive.Contracts;

using System.Collections.Generic;

/// <summary>
/// Dossier d'export contrôle fiscal (TRK06, F06 §7) pour un document ou une période : les paquets
/// d'archive (payload transmis, réponse PA, rendu lisible, manifest, addenda), le rapport d'intégrité
/// (<see cref="Verification"/>), les preuves d'ancrage, la chronologie complète des
/// <c>DocumentEvent</c> et une NOTICE DE VÉRIFICATION EN FRANÇAIS expliquant au vérificateur comment
/// contrôler les preuves. Consommé par API03 (qui le sérialise en archive téléchargeable).
/// </summary>
/// <param name="Scope">Portée de l'export (ex. <c>document:&lt;id&gt;</c> ou <c>période:2026-05</c>).</param>
/// <param name="Files">Les fichiers du dossier d'export.</param>
/// <param name="Verification">Le rapport d'intégrité (chaîne + ancrages) au moment de l'export.</param>
/// <param name="IsComplete"><c>false</c> si aucun document n'a été trouvé pour la portée demandée.</param>
/// <param name="Notice">La notice de vérification en français (également présente en fichier dans <see cref="Files"/>).</param>
public sealed record FiscalControlExport(
    string Scope,
    IReadOnlyList<FiscalExportFile> Files,
    ArchiveVerificationReport Verification,
    bool IsComplete,
    string Notice);
