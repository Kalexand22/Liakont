namespace Liakont.Modules.Ged.Application.Index;

using System;
using System.Collections.Generic;

/// <summary>
/// Résultat d'une <see cref="IDocumentSearchIndex.SearchAsync"/> : une page de documents (keyset) + les facettes
/// calculées sur l'ENSEMBLE des documents correspondants (pas seulement la page). Les facettes ne portent que sur
/// les axes <c>is_facetable</c> non confidentiels (ou tous si le droit est présent) — aucun compte confidentiel
/// n'est révélé sans le droit (anti-oracle, RL-31).
/// </summary>
public sealed record DocumentSearchResult
{
    /// <summary>Résultat vide (aucun document, aucune facette, pas de page suivante).</summary>
    public static DocumentSearchResult Empty { get; } = new()
    {
        Hits = Array.Empty<DocumentSearchHit>(),
        Facets = Array.Empty<SearchFacet>(),
        NextCursor = null,
    };

    /// <summary>Documents de la page courante, triés par <c>managed_document_id</c> (keyset).</summary>
    public required IReadOnlyList<DocumentSearchHit> Hits { get; init; }

    /// <summary>Facettes (axe, valeur, compte) sur l'ensemble correspondant.</summary>
    public required IReadOnlyList<SearchFacet> Facets { get; init; }

    /// <summary>Curseur de la page suivante (dernier id de cette page) ou <see langword="null"/> si dernière page.</summary>
    public Guid? NextCursor { get; init; }
}
