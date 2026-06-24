namespace Liakont.Host.B2cReporting;

using System.Collections.Generic;

/// <summary>
/// Modèle de présentation de la page des émissions e-reporting B2C de la marge (B4). Pur conteneur
/// présentationnel (aucune logique métier/fiscale, CLAUDE.md n°2) : la liste des agrégats transmis avec leur
/// état courant. Un record (testable en bUnit sans dépendance DI).
/// </summary>
internal sealed record B2cMarginEmissionsViewModel
{
    /// <summary>Agrégats d'émission de la marge (un par contenu transmis), du plus récent au plus ancien.</summary>
    public required IReadOnlyList<B2cMarginEmissionRow> Emissions { get; init; }
}
