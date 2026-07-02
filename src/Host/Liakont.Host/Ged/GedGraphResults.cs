namespace Liakont.Host.Ged;

using System.Collections.Generic;

/// <summary>
/// Résultat d'UNE page d'exploration de graphe GED, tel que consommé par la vue-pure <c>GedGraphView</c>
/// (projeté depuis <c>GraphExplorationResult</c>, GED08). La pagination est keyset : <see cref="NextCursor"/>
/// porte le curseur de la page suivante (<see langword="null"/> = dernière page) — aucun chemin ne matérialise
/// l'intégralité du voisinage (RL-20, INV-GED-09).
/// </summary>
public sealed record GedGraphResults
{
    /// <summary>Résultat vide (avant chargement, ou objet sans document rattaché dans le périmètre exploré).</summary>
    public static GedGraphResults Empty { get; } = new();

    /// <summary>Documents atteignables de la page courante (déjà masqués côté serveur, §6.4/§6.5).</summary>
    public IReadOnlyList<GedGraphHit> Hits { get; init; } = [];

    /// <summary>Curseur keyset de la page suivante ; <see langword="null"/> si c'est la dernière page.</summary>
    public GedGraphCursor? NextCursor { get; init; }
}
