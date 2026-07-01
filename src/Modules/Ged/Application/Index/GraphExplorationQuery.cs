namespace Liakont.Modules.Ged.Application.Index;

using System;

/// <summary>
/// Critères d'une exploration de graphe bornée (F19 §6.4). La traversée part de <see cref="RootEntityId"/>, est
/// BIDIRECTIONNELLE, bornée par <see cref="MaxDepth"/> (clampée par l'implémentation dans [0..<see cref="MaxAllowedDepth"/>],
/// anti-DoS), anti-cycle, et paginée en keyset (RL-20) via <see cref="After"/>. Confidentialité matérialisée dans le
/// SQL (RL-31) : sans <see cref="HasConfidentialRight"/>, aucune entité dont le type est confidentiel n'est traversée
/// ni retournée (racine comprise → ensemble vide, pas d'oracle depth-0).
/// </summary>
public sealed record GraphExplorationQuery
{
    /// <summary>Profondeur par défaut si non précisée.</summary>
    public const int DefaultMaxDepth = 4;

    /// <summary>Borne DURE de profondeur (jamais infinie, anti-DoS) — clamp appliqué par l'implémentation.</summary>
    public const int MaxAllowedDepth = 8;

    /// <summary>Taille de page par défaut.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Borne dure de taille de page.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Entité racine (ancre de la traversée).</summary>
    public required Guid RootEntityId { get; init; }

    /// <summary>Profondeur maximale demandée (clampée dans [0..<see cref="MaxAllowedDepth"/>]).</summary>
    public int MaxDepth { get; init; } = DefaultMaxDepth;

    /// <summary>L'acteur porte-t-il <c>liakont.ged.confidential</c> ? Matérialisé dans le SQL (RL-31).</summary>
    public bool HasConfidentialRight { get; init; }

    /// <summary>Curseur keyset EXCLUSIF (dernier tuple de la page précédente) ; <see langword="null"/> = première page.</summary>
    public GraphCursor? After { get; init; }

    /// <summary>Taille de page (bornée dans [1..<see cref="MaxPageSize"/>]).</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
