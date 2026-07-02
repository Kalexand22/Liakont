namespace Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Règle déclarative de mapping d'un axe (F19 §4.5, généralisation de <c>MappingRule</c> du domaine TVA).
/// Un <see cref="Source"/> (sélecteur JSONPath restreint, voir <see cref="GedSelector"/>) désigne, dans le
/// <c>IngestedDocumentDto</c> BRUT, la ou les valeurs à ranger sur l'axe cible <see cref="AxisCode"/>. La règle
/// ne porte AUCUNE valeur inventée (règle 2) : elle décrit seulement OÙ lire la donnée dans la source.
/// </summary>
/// <param name="AxisCode">Code de l'axe cible (paramétrage tenant, jamais un littéral métier en dur — règle 7).</param>
/// <param name="Source">Sélecteur JSONPath restreint sur le document ingéré (chemins simples + filtre d'égalité).</param>
/// <param name="IsRequired">
/// Si vrai et que le sélecteur ne renvoie AUCUNE valeur, le document est <b>déféré</b> (INV-GED-05, règle 3) —
/// jamais mappé au hasard.
/// </param>
/// <param name="IsMulti">
/// Si faux et que le sélecteur renvoie plus d'une valeur, le mapping est <b>déféré</b> (ambiguïté mono-valeur) —
/// jamais deviner laquelle retenir.
/// </param>
public sealed record AxisMappingRule(string AxisCode, string Source, bool IsRequired, bool IsMulti);
