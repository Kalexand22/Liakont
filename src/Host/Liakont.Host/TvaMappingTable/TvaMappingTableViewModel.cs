namespace Liakont.Host.TvaMappingTable;

using System.Collections.Generic;
using Liakont.Modules.TvaMapping.Contracts.DTOs;

/// <summary>
/// Modèle de la page « Paramétrage comptable — Table TVA » (WEB07a + édition WEB07b) : la table de
/// mapping du tenant (ou <c>null</c> si non paramétrée), son journal de modifications append-only,
/// l'identité de l'opérateur courant, le rapport de couverture (régimes source non mappés « à
/// compléter », item TVA03) et les listes FERMÉES d'édition (catégories / parts / modes de taux /
/// VATEX — item TVA05). Assemblé hors de la page Blazor (la page reste présentationnelle, CLAUDE.md
/// n°19) et testable unitairement. Tenant-scopé (CLAUDE.md n°9).
/// </summary>
public sealed class TvaMappingTableViewModel
{
    /// <summary>Table de mapping TVA du tenant, ou <c>null</c> si aucune table n'est paramétrée.</summary>
    public MappingTableDto? Table { get; init; }

    /// <summary>Journal des modifications de la table, du plus récent au plus ancien (vide si aucune).</summary>
    public required IReadOnlyList<MappingChangeLogEntryDto> ChangeLog { get; init; }

    /// <summary>
    /// Identité lisible de l'opérateur authentifié courant. Sert de garde de confirmation à la
    /// validation (l'opérateur ressaisit son nom — il ne peut valider qu'en son propre nom) ET de
    /// valeur enregistrée comme valideur côté serveur (parité avec l'endpoint API04 /validate, qui
    /// résout <c>validatedBy</c> depuis l'identité authentifiée — jamais une signature au nom d'un tiers).
    /// </summary>
    public required string CurrentOperatorName { get; init; }

    /// <summary>
    /// Rapport de couverture du mapping (item TVA03) : régimes source observés non mappés à compléter
    /// en priorité. <c>null</c> si aucune société n'est résolue (profil tenant pas encore créé — CFG02).
    /// </summary>
    public MappingCoverageReportDto? Coverage { get; init; }

    /// <summary>
    /// Listes FERMÉES proposées à l'édition d'une règle (catégories UNCL5305, parts, modes de taux,
    /// codes VATEX). Sourcées (F03 §2.1/§2.2 + énumérations du domaine) ; jamais de saisie libre.
    /// </summary>
    public required TvaMappingEditOptionsDto EditOptions { get; init; }
}
