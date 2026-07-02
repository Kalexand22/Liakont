namespace Liakont.Host.Ged;

/// <summary>
/// Un critère de recherche sur un axe documentaire GED (F19 §6.2) : conjonction (« ET ») avec les autres
/// filtres actifs de la page <c>/ged/recherche</c>. <see cref="AxisCode"/> et <see cref="Value"/> sont
/// normalisés côté serveur (comme à l'écriture) par l'index de recherche — la page ne normalise rien.
/// </summary>
/// <param name="AxisCode">Code machine de l'axe (ex. issu d'une facette cliquée).</param>
/// <param name="Value">Valeur d'axe recherchée.</param>
public sealed record GedAxisFilter(string AxisCode, string Value);
