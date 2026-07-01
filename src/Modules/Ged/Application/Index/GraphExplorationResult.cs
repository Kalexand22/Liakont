namespace Liakont.Modules.Ged.Application.Index;

using System;
using System.Collections.Generic;

/// <summary>Résultat d'une <see cref="IDocumentSearchIndex.ExploreGraphAsync"/> : une page de documents atteignables (keyset).</summary>
public sealed record GraphExplorationResult
{
    /// <summary>Résultat vide.</summary>
    public static GraphExplorationResult Empty { get; } = new()
    {
        Documents = Array.Empty<GraphDocumentHit>(),
        NextCursor = null,
    };

    /// <summary>Documents atteignables via le graphe, triés par (document, entité, rôle).</summary>
    public required IReadOnlyList<GraphDocumentHit> Documents { get; init; }

    /// <summary>Curseur de la page suivante ou <see langword="null"/> si dernière page.</summary>
    public GraphCursor? NextCursor { get; init; }
}
