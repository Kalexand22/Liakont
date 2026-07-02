namespace Liakont.Modules.Reference.Contracts.DTOs;

/// <summary>
/// Une correspondance de code pays du référentiel cross-instance (ADR-0038) : un code SOURCE non-ISO
/// (ex. « ENG », « JAP », « BEL ») rapproché de son code ISO 3166-1 alpha-2 (« GB », « JP », « BE »).
/// Projection de lecture pour la console. <c>UpdatedAtUtc</c> = date de dernière modification de l'état
/// COURANT (métadonnée de la table de paramétrage) ; la piste d'audit complète (auteur, avant/après) vit
/// dans le journal append-only, jamais ici.
/// </summary>
public sealed record CountryAliasDto
{
    /// <summary>Code source normalisé (MAJUSCULES, sans espaces) — clé de la correspondance.</summary>
    public required string SourceCode { get; init; }

    /// <summary>Code ISO 3166-1 alpha-2 cible (validé à l'écriture).</summary>
    public required string IsoCode { get; init; }

    /// <summary>Date UTC de dernière modification de la correspondance.</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
