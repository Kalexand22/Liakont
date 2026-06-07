namespace Liakont.Modules.Documents.Contracts.DTOs;

using System;

/// <summary>
/// Critères de la liste paginée de documents pour la console (API01a, GET /documents). Tous les
/// filtres sont optionnels ; combinés en ET. La liste est TENANT-SCOPÉE PAR CONSTRUCTION (la connexion
/// EST le tenant — database-per-tenant, blueprint §7) : aucun filtre tenant ici, aucune lecture
/// cross-tenant possible (CLAUDE.md n°9/17).
/// </summary>
public sealed record DocumentListFilter
{
    /// <summary>Borne basse de la date d'émission (incluse), ou <c>null</c>.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Borne haute de la date d'émission (incluse), ou <c>null</c>.</summary>
    public DateOnly? To { get; init; }

    /// <summary>État exact à filtrer (nom de l'état, ex. <c>ReadyToSend</c>), ou <c>null</c> pour tous.</summary>
    public string? State { get; init; }

    /// <summary>Type de document brut à filtrer, ou <c>null</c> pour tous.</summary>
    public string? Type { get; init; }

    /// <summary>
    /// Recherche libre (insensible à la casse) sur le numéro, la référence source et le nom du client,
    /// ou <c>null</c>.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Page 1-basée. Bornée par l'implémentation.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Taille de page. Bornée par l'implémentation.</summary>
    public int PageSize { get; init; } = 50;
}
