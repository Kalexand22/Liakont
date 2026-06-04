namespace Liakont.Modules.TvaMapping.Infrastructure.Seed;

/// <summary>
/// Modèle de désérialisation d'un seed de table de mapping TVA (item TVA04) — format des fichiers
/// <c>config/exemples/mapping-exemple.json</c> et <c>deployments/&lt;client&gt;/</c> (F03 §4.1).
/// Permissif (champs nullables) : la validation structurelle (catégories UNCL5305, E à 0 % → VATEX,
/// doublons, cohérence du taux) est faite par le domaine à la conversion en
/// <see cref="Liakont.Modules.TvaMapping.Domain.Entities.MappingTable"/> via
/// <see cref="MappingTableSeedReader"/>. La date de validation absente laisse la table « NON
/// VALIDÉE » (le marqueur d'exemple est porté par <see cref="ValidatedBy"/>, item TVA04 §2).
/// </summary>
internal sealed record MappingTableSeed
{
    public string? MappingVersion { get; init; }

    public string? ValidatedBy { get; init; }

    public DateOnly? ValidatedDate { get; init; }

    public string? DefaultBehavior { get; init; }

    public IReadOnlyList<MappingRuleSeed>? Rules { get; init; }
}
