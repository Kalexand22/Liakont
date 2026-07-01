namespace Liakont.Modules.Ged.Application.Index;

/// <summary>Une facette : nombre de documents portant la valeur <see cref="Value"/> (normalisée) sur l'axe <see cref="AxisCode"/>.</summary>
public sealed record SearchFacet(string AxisCode, string Value, long Count);
