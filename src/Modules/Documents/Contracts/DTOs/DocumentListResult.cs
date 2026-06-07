namespace Liakont.Modules.Documents.Contracts.DTOs;

using System.Collections.Generic;

/// <summary>
/// Résultat paginé de la liste de documents (API01a, GET /documents) : la page courante, le total
/// correspondant aux filtres (pour la pagination) et les compteurs par état pour le bandeau de synthèse
/// de la console. Les compteurs honorent les filtres de CONTEXTE (dates, type, recherche) mais PAS le
/// filtre d'état lui-même, afin que le bandeau montre la répartition de tous les états du périmètre courant.
/// </summary>
public sealed record DocumentListResult
{
    /// <summary>Documents de la page courante (résumés), triés par dernière mise à jour décroissante.</summary>
    public required IReadOnlyList<DocumentSummaryDto> Items { get; init; }

    /// <summary>Page 1-basée effectivement retournée (après bornage).</summary>
    public required int Page { get; init; }

    /// <summary>Taille de page effectivement appliquée (après bornage).</summary>
    public required int PageSize { get; init; }

    /// <summary>Nombre total de documents correspondant aux filtres (état inclus), tous états de pagination confondus.</summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Compteurs par état du périmètre courant (filtres dates/type/recherche appliqués, filtre d'état
    /// ignoré) — alimente le bandeau de synthèse de la console.
    /// </summary>
    public required IReadOnlyDictionary<string, int> CountsByState { get; init; }
}
