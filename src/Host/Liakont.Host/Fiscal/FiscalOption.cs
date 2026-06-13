namespace Liakont.Host.Fiscal;

/// <summary>
/// Une option d'une liste fermée fiscale : la <see cref="Value"/> envoyée au contrat (nom d'énumération
/// exact, jamais une valeur inventée) et son <see cref="Label"/> français affiché à l'opérateur.
/// </summary>
public sealed record FiscalOption(string Value, string Label);
