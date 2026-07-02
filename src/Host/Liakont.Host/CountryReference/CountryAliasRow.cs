namespace Liakont.Host.CountryReference;

using System;

/// <summary>
/// Ligne d'affichage d'une correspondance de code pays (ADR-0038) pour l'écran « Référentiel pays » (Lot 4).
/// Projection de LECTURE alimentée par <see cref="Liakont.Modules.Reference.Contracts.ICountryAliasReferential"/>
/// (via <see cref="ICountryAliasConsoleService"/>) — formatage uniquement, AUCUNE règle métier. Ne porte que ce
/// que la console affiche / trie / recherche / exporte : code source, code ISO cible, date de dernière
/// modification. Les mutations passent par les commandes MediatR (upsert / remove), jamais par cette vue.
/// </summary>
public sealed record CountryAliasRow
{
    /// <summary>Code source normalisé (MAJUSCULES) — clé de la correspondance et des actions de ligne.</summary>
    public required string SourceCode { get; init; }

    /// <summary>Code ISO 3166-1 alpha-2 cible.</summary>
    public required string IsoCode { get; init; }

    /// <summary>Date UTC de dernière modification de la correspondance.</summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
