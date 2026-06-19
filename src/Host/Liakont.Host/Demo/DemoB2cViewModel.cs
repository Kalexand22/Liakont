namespace Liakont.Host.Demo;

using System.Collections.Generic;

/// <summary>
/// Modèle assemblé de l'écran « Démo e-reporting B2C — Essentiel » (B2C04). Restitue, pour le tenant courant,
/// les DÉCLARATIONS 10.3 (flux Essentiel) et leur état de bout en bout : transmis/accusé (état <c>Issued</c>),
/// bloqué (régime non mappé — p. ex. régime 6), présence du lien reporting↔pièces (B2C03) et accès à l'export
/// contrôle fiscal. Aucune logique métier ici : le modèle est une PROJECTION de lecture (la page reste
/// présentationnelle, CLAUDE.md n°19 ; l'envoi passe par la voie unique du pipeline).
/// </summary>
public sealed record DemoB2cViewModel
{
    /// <summary>Les déclarations 10.3 du tenant courant (les plus récentes en tête), ou liste vide.</summary>
    public required IReadOnlyList<DemoB2cDeclarationRow> Declarations { get; init; }
}
