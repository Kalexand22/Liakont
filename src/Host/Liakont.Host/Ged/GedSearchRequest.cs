namespace Liakont.Host.Ged;

using System;
using System.Collections.Generic;

/// <summary>
/// Critères d'UNE requête de recherche documentaire GED émise par la page <c>/ged/recherche</c> (F19 §6.2/§6.7).
/// Purement présentationnel : plein texte libre + conjonction de filtres d'axe + curseur keyset. Le droit
/// <c>liakont.ged.confidential</c> n'est PAS porté ici — il est résolu SERVER-SIDE par <see cref="IGedQueries"/>
/// depuis les permissions de l'acteur (le masquage §6.5 ne dépend jamais d'un booléen fourni par la page).
/// </summary>
public sealed record GedSearchRequest
{
    /// <summary>Taille de page par défaut (alignée sur l'index de recherche GED08).</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Recherche plein texte libre (<c>websearch_to_tsquery</c> côté SQL) ; <see langword="null"/> = aucune.</summary>
    public string? FullText { get; init; }

    /// <summary>Filtres d'axe actifs (conjonction « ET », robuste aux axes multi-valeur, F19 §6.2).</summary>
    public IReadOnlyList<GedAxisFilter> AxisFilters { get; init; } = [];

    /// <summary>
    /// Curseur keyset EXCLUSIF : dernier <c>managed_document_id</c> de la page précédente (<see langword="null"/> =
    /// première page). Jamais d'OFFSET — la pagination consomme des pages déjà bornées côté SQL (RL-20).
    /// </summary>
    public Guid? AfterDocumentId { get; init; }

    /// <summary>Taille de la page demandée (bornée par l'index ; défaut <see cref="DefaultPageSize"/>).</summary>
    public int PageSize { get; init; } = DefaultPageSize;
}
