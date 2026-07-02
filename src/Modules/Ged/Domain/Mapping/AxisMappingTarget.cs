namespace Liakont.Modules.Ged.Domain.Mapping;

using Liakont.Modules.Ged.Domain.Catalog;

/// <summary>
/// Description minimale d'un axe cible telle que requise par <see cref="GedMapper"/> pour NORMALISER une valeur
/// brute (F19 §3.3.1/§4.5) : le <see cref="DataType"/> et l'échelle décimale <see cref="ValueScale"/> pilotent
/// <see cref="ValueNormalizer"/> (un axe <c>number</c> devient un <c>decimal</c> half-up, jamais double — règle 1),
/// et la cardinalité <see cref="IsMultiValue"/> (recopie de <c>AxisDefinition.IsMultiValue</c> du catalogue) permet
/// au mapper de DÉFÉRER une incohérence profil-multi / axe-mono plutôt que de laisser le chemin d'écriture écraser
/// silencieusement les valeurs surnuméraires (INV-GED-05, règle 3).
/// Résolue depuis le catalogue tenant (<c>ged_catalog.axis_definitions</c>) par <see cref="IAxisMappingCatalog"/>.
/// </summary>
/// <param name="AxisCode">Code de l'axe.</param>
/// <param name="DataType">Type de donnée déclaré de l'axe.</param>
/// <param name="ValueScale">Échelle décimale déclarée (axe <c>number</c>) ; <see langword="null"/> = brut.</param>
/// <param name="IsMultiValue">Cardinalité déclarée au catalogue : <see langword="false"/> = un axe MONO ne garde qu'une valeur courante (RL-02).</param>
public sealed record AxisMappingTarget(string AxisCode, AxisDataType DataType, int? ValueScale, bool IsMultiValue);
