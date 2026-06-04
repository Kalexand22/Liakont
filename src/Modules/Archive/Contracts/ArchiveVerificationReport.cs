namespace Liakont.Modules.Archive.Contracts;

using System.Collections.Generic;

/// <summary>
/// Rapport de vérification COMPLET du coffre d'un tenant (TRK06, ArchiveVerifier) : la vérification de
/// contenu + chaînage de TRK05 (<see cref="Chain"/>) ENRICHIE de la vérification des preuves d'ancrage
/// temporel (<see cref="Anchors"/>). C'est l'objet de l'action opérateur « Vérifier l'intégrité de
/// l'archive du tenant » (API03/WEB04) et du volet intégrité de l'export contrôle fiscal.
/// </summary>
/// <param name="Chain">Intégrité contenu + chaînage des paquets et addenda (TRK05).</param>
/// <param name="Anchors">Vérification des preuves d'ancrage temporel (vide si aucun ancrage configuré).</param>
/// <param name="IsChainAnchored"><c>true</c> si au moins une preuve d'ancrage VALIDE couvre la tête de chaîne actuelle.</param>
/// <param name="IsFullyVerified"><c>true</c> si la chaîne est intacte ET toutes les preuves d'ancrage présentes sont valides.</param>
/// <param name="Summary">Synthèse française du rapport (pour l'opérateur et la notice d'export).</param>
public sealed record ArchiveVerificationReport(
    ArchiveIntegrityReport Chain,
    IReadOnlyList<ArchiveAnchorVerification> Anchors,
    bool IsChainAnchored,
    bool IsFullyVerified,
    string Summary);
