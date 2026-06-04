namespace Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Comportement par défaut d'une table de mapping pour un régime source absent de la table.
/// </summary>
/// <remarks>
/// F03 §4.1 : <c>defaultBehavior: "block"</c> est NON négociable — un régime non mappé bloque le
/// document porteur, il ne produit JAMAIS un envoi à l'aveugle ni une valeur devinée
/// (CLAUDE.md n°2/3). <see cref="Block"/> est la seule valeur sourcée ; n'en ajoutez aucune autre
/// sans décision tracée dans <c>docs/conception/</c>.
/// </remarks>
public enum MappingDefaultBehavior
{
    /// <summary>Régime non mappé = blocage du document (jamais de mapping deviné).</summary>
    Block = 0,
}
