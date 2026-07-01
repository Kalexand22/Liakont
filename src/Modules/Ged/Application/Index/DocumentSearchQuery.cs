namespace Liakont.Modules.Ged.Application.Index;

using System;
using System.Collections.Generic;

/// <summary>
/// Critères d'une recherche documentaire GED (F19 §6.2/§6.3). Conjonction : un document doit satisfaire le
/// plein-texte (si fourni) ET TOUS les filtres d'axe (si fournis). La pagination est en keyset (RL-20) :
/// <see cref="AfterManagedDocumentId"/> est le curseur EXCLUSIF (dernier id de la page précédente), l'ordre est
/// stable sur <c>managed_document_id</c>. Le tri par pertinence (ts_rank) est un fast-follow (GED21).
/// </summary>
public sealed record DocumentSearchQuery
{
    /// <summary>Taille de page par défaut.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Borne dure de taille de page (anti-DoS).</summary>
    public const int MaxPageSize = 200;

    /// <summary>Requête plein-texte libre (traitée par <c>websearch_to_tsquery('french', …)</c> après unaccent) ; nulle/vide = pas de filtre plein-texte.</summary>
    public string? FullText { get; init; }

    /// <summary>Filtres d'axe en conjonction (chaque axe doit être satisfait) ; vide = pas de filtre d'axe.</summary>
    public IReadOnlyList<AxisFilter> AxisFilters { get; init; } = Array.Empty<AxisFilter>();

    /// <summary>L'acteur porte-t-il <c>liakont.ged.confidential</c> ? Résolu par l'appelant (page/handler) ; matérialisé dans le SQL (RL-31).</summary>
    public bool HasConfidentialRight { get; init; }

    /// <summary>Curseur keyset EXCLUSIF : ne renvoyer que les documents dont l'id est &gt; celui-ci ; <see langword="null"/> = première page.</summary>
    public Guid? AfterManagedDocumentId { get; init; }

    /// <summary>Taille de page (bornée par l'implémentation dans [1..<see cref="MaxPageSize"/>]).</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
