namespace Liakont.Host.Ged;

using System;

/// <summary>
/// Critères d'UNE page d'exploration de graphe émise par la page <c>/ged/objet/{entityType}/{id}</c>
/// (F19 §6.4/§6.7). Purement présentationnel : entité racine + curseur keyset. Le droit
/// <c>liakont.ged.confidential</c> n'est PAS porté ici — il est résolu SERVER-SIDE par <see cref="IGedGraphQueries"/>
/// depuis les permissions de l'acteur (la traversée exclut alors racine + voisins confidentiels, §6.4/§6.5, jamais
/// sur la foi d'un booléen fourni par la page). La borne de profondeur et la taille de page sont re-clampées côté
/// index (anti-DoS).
/// </summary>
public sealed record GedGraphRequest
{
    /// <summary>Profondeur par défaut (alignée sur l'index de graphe GED08).</summary>
    public const int DefaultMaxDepth = 4;

    /// <summary>Taille de page par défaut (alignée sur l'index de graphe GED08).</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Entité racine de la traversée (ancre) — segment <c>{id}</c> de la route.</summary>
    public required Guid RootEntityId { get; init; }

    /// <summary>
    /// Code du type d'entité racine (segment <c>{entityType}</c> de la route). Purement libellé/audit : la
    /// confidentialité RÉELLE de la racine est résolue par son type EN BASE (RL-31), jamais depuis ce code.
    /// </summary>
    public string? EntityTypeCode { get; init; }

    /// <summary>Profondeur maximale demandée (re-clampée côté index dans [0..8], anti-DoS).</summary>
    public int MaxDepth { get; init; } = DefaultMaxDepth;

    /// <summary>
    /// Curseur keyset EXCLUSIF : dernier triplet de la page précédente (<see langword="null"/> = première page).
    /// Jamais d'OFFSET — la pagination consomme des pages déjà bornées côté SQL (RL-20).
    /// </summary>
    public GedGraphCursor? After { get; init; }

    /// <summary>Taille de la page demandée (bornée par l'index ; défaut <see cref="DefaultPageSize"/>).</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
