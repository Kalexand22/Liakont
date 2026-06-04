namespace Liakont.Modules.TvaMapping.Infrastructure.Seed;

/// <summary>
/// Modèle de désérialisation d'une règle d'un seed de table de mapping TVA (item TVA04, F03 §4.1).
/// Permissif (champs nullables, énumérations en chaîne) : la cohérence fiscale est tranchée par le
/// domaine à la conversion (<see cref="MappingTableSeedReader"/> → <c>MappingTable.Create</c>), qui
/// REJETTE toute catégorie / part / mode de taux inconnus — aucune valeur n'est devinée
/// (CLAUDE.md n°2).
/// </summary>
internal sealed record MappingRuleSeed
{
    public string? SourceRegimeCode { get; init; }

    public string? Label { get; init; }

    public string? Part { get; init; }

    public IReadOnlyDictionary<string, string>? SourceFlags { get; init; }

    public string? Category { get; init; }

    public string? Vatex { get; init; }

    public string? Note { get; init; }

    public string? RateMode { get; init; }

    public decimal? RateValue { get; init; }
}
