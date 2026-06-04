namespace Liakont.Modules.TvaMapping.Domain.Services;

using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Construit une <see cref="MappingRule"/> à partir d'entrées primitives (chaînes des énumérations,
/// telles qu'elles arrivent de la console via API04/WEB07 — item TVA05). Les codes d'énumération
/// (catégorie UNCL5305, part, mode de taux) sont parsés STRICTEMENT : toute valeur hors liste sourcée
/// est rejetée, jamais devinée (CLAUDE.md n°2). La cohérence structurelle de la règle dans la table
/// (doublon, E à 0 % → VATEX, taux) reste vérifiée par <see cref="MappingTableValidator"/> au moment
/// de l'ajout/remplacement dans la table.
/// </summary>
public static class MappingRuleFactory
{
    /// <summary>
    /// Construit une règle valide syntaxiquement (énumérations admises, code régime non vide). Lève
    /// <see cref="System.ArgumentException"/> (message opérateur français) sur une valeur d'énumération
    /// inconnue ou un code régime vide.
    /// </summary>
    public static MappingRule Create(
        string? sourceRegimeCode,
        string? label,
        string? part,
        IReadOnlyDictionary<string, string>? sourceFlags,
        string? category,
        string? vatex,
        string? note,
        string? rateMode,
        decimal? rateValue)
    {
        var code = sourceRegimeCode?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw new ArgumentException(
                "Le code du régime source est obligatoire pour une règle de mapping TVA.",
                nameof(sourceRegimeCode));
        }

        return new MappingRule
        {
            SourceRegimeCode = code,
            Label = Trimmed(label),
            Part = ParsePart(part),
            SourceFlags = NormalizeFlags(sourceFlags),
            Category = VatCategoryParser.Parse(category),
            Vatex = Trimmed(vatex),
            Note = Trimmed(note),
            RateMode = ParseRateMode(rateMode),
            RateValue = rateValue,
        };
    }

    /// <summary>Parse strictement une part (nom de <see cref="MappingPart"/>) ; rejette l'inconnu.</summary>
    public static MappingPart ParsePart(string? part)
        => ParseEnum<MappingPart>(part, "part de ligne");

    /// <summary>Parse strictement un mode de taux (nom de <see cref="RateMode"/>) ; rejette l'inconnu.</summary>
    public static RateMode ParseRateMode(string? rateMode)
        => ParseEnum<RateMode>(rateMode, "mode de taux");

    private static TEnum ParseEnum<TEnum>(string? value, string label)
        where TEnum : struct, Enum
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException(
                $"La {label} est obligatoire. Valeurs admises : {string.Join(", ", Enum.GetNames<TEnum>())}.",
                nameof(value));
        }

        // Correspondance EXACTE sur le nom (jamais une valeur numérique ni une casse libre, qui
        // reviendraient à accepter une valeur hors liste — même discipline que VatCategoryParser).
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, trimmed, StringComparison.Ordinal))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new ArgumentException(
            $"{label} inconnue : « {trimmed} ». Valeurs admises : {string.Join(", ", Enum.GetNames<TEnum>())} — aucune n'est devinée.",
            nameof(value));
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static IReadOnlyDictionary<string, string>? NormalizeFlags(IReadOnlyDictionary<string, string>? flags)
        => flags is { Count: > 0 } ? flags : null;
}
