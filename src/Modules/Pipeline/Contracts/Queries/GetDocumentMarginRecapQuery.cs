namespace Liakont.Modules.Pipeline.Contracts.Queries;

using Liakont.Agent.Contracts.Pivot;
using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Récap de marge d'UN document (aide à la déclaration de TVA du détail, art. 297 E) à partir de son pivot DÉJÀ
/// enrichi (snapshot transmis ou rejeu read-time) : si le document est au régime de la marge (buyer-indépendant,
/// B2C ou B2B), résout commission acheteur + vendeur, taux des honoraires (mapping F03 <c>Part.Frais</c>) et base
/// HT/TVA. Lecture seule, TENANT-SCOPÉE (société courante résolue côté handler). <c>null</c> si le document n'est
/// pas au régime de la marge OU si la marge est bloquée (fail-closed — jamais un chiffre deviné, CLAUDE.md n°2).
/// </summary>
public sealed record GetDocumentMarginRecapQuery : IQuery<DocumentMarginRecapDto?>
{
    /// <summary>Le pivot enrichi du document (snapshot transmis prioritaire, sinon rejeu read-time du pivot stagé).</summary>
    public required PivotDocumentDto Pivot { get; init; }
}
