namespace Liakont.Modules.FacturX.Application;

using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Contracts;

/// <summary>
/// Port de génération du Factur-X (ADR-0023). Construit, À PARTIR DU PIVOT EN 16931 SEUL, un
/// <see cref="FacturXDocument"/> (PDF/A-3 + <c>factur-x.xml</c> CII embarqué) : la sortie est
/// DÉTERMINISTE du pivot. La décision de générer appartient au pipeline appelant (FX07), pilotée par
/// la capacité PA <c>SupportsFacturXTransmission</c> ; le port lui-même ne consulte AUCUNE
/// <c>PaCapabilities</c> et ne référence aucun plug-in PA (ADR-0023 INV-FX-4). Implémentation :
/// sérialiseur CII maison (FX03) + scellement PDF/A-3 QuestPDF confiné à l'Infrastructure (FX04).
/// </summary>
public interface IFacturXBuilder
{
    /// <summary>
    /// Génère le Factur-X du document pivot. BLOQUE (lève) si un élément obligatoire EN 16931 n'est ni
    /// porté ni dérivable par agrégation normative — jamais de CII tronqué ni de valeur fiscale
    /// fabriquée (ADR-0023 n°3 / INV-FX-2 ; CLAUDE.md n°3).
    /// </summary>
    /// <param name="pivot">Le document pivot EN 16931 à sceller en Factur-X.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>L'artefact Factur-X (PDF/A-3 + CII embarqué).</returns>
    Task<FacturXDocument> BuildAsync(PivotDocumentDto pivot, CancellationToken cancellationToken = default);
}
