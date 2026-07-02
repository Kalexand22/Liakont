namespace Liakont.Modules.Ged.Domain.Mapping;

using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Valeur d'axe MAPPÉE et normalisée (une par valeur retenue ; un axe multi-valeur en produit plusieurs).
/// <see cref="Value"/> porte la colonne typée (decimal exact pour un axe <c>number</c>) prête à l'insert par
/// le consommateur d'ingestion (GED05b, via le chemin d'écriture GED04). Aucune valeur inventée (règle 2).
/// </summary>
/// <param name="AxisCode">Code de l'axe cible.</param>
/// <param name="Value">Valeur normalisée (colonne typée + forme canonique).</param>
public sealed record MappedAxisValue(string AxisCode, NormalizedAxisValue Value);
