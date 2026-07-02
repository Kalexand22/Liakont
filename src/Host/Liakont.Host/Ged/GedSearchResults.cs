namespace Liakont.Host.Ged;

using System;
using System.Collections.Generic;

/// <summary>
/// Résultat d'UNE page de recherche documentaire GED, tel que consommé par la vue-pure <c>GedSearchView</c>
/// (projeté depuis <c>DocumentSearchResult</c>, GED08). La pagination est keyset : <see cref="NextCursor"/> porte le
/// curseur de la page suivante (<see langword="null"/> = dernière page) — aucun chemin ne matérialise l'intégralité
/// du corpus (RL-20).
/// </summary>
public sealed record GedSearchResults
{
    /// <summary>Résultat vide (avant toute recherche, ou aucune correspondance).</summary>
    public static GedSearchResults Empty { get; } = new();

    /// <summary>Documents de la page courante (déjà masqués côté serveur, §6.5).</summary>
    public IReadOnlyList<GedSearchHit> Hits { get; init; } = [];

    /// <summary>Facettes calculées sur l'ensemble filtré (axes confidentiels exclus sans le droit, §6.5).</summary>
    public IReadOnlyList<GedSearchFacet> Facets { get; init; } = [];

    /// <summary>Curseur keyset de la page suivante ; <see langword="null"/> si c'est la dernière page.</summary>
    public Guid? NextCursor { get; init; }
}
