namespace Liakont.Modules.Ged.Application.Index;

/// <summary>
/// Filtre d'égalité sur un axe : le document doit porter la valeur <see cref="Value"/> (brute — normalisée par
/// l'implémentation avec le même <c>ValueNormalizer</c> qu'à l'écriture) pour l'axe <see cref="AxisCode"/>. Un axe
/// inconnu/inactif ou une valeur incompatible avec le type d'axe ne matche aucun document (jamais deviner, règle 2).
/// </summary>
public sealed record AxisFilter(string AxisCode, string Value);
