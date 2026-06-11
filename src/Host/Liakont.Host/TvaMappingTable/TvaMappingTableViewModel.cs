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
    /// <c>true</c> si une société (profil tenant) est résolue pour la requête : conditionne l'affichage
    /// du paramétrage propre au tenant (activation du vertical enchères). <c>false</c> tant que le profil
    /// n'est pas créé (CFG02) — la page reste alors en vue vide transitoire.
    /// </summary>
    public bool TenantResolved { get; init; }

    /// <summary>
    /// Activation du vertical « vente aux enchères » du tenant (paramétrage produit, décision opérateur
    /// D4 — lot FIX03). Quand <c>false</c> (défaut), le champ « part » de l'éditeur de règle est masqué
    /// (part <c>Autre</c> implicite) ; quand <c>true</c>, les trois parts sont proposées.
    /// </summary>
    public bool AuctionVerticalEnabled { get; init; }

    /// <summary>
    /// Rapport de cohérence du paramétrage (lot FIX03) : règles mortes (part non consultée, code jamais
    /// observé) signalées avant validation. <c>null</c> si aucune société n'est résolue (CFG02).
    /// </summary>
    public MappingConsistencyReportDto? Consistency { get; init; }

    /// <summary>
    /// Listes FERMÉES proposées à l'édition d'une règle (catégories UNCL5305, parts, modes de taux,
    /// codes VATEX). Sourcées (F03 §2.1/§2.2 + énumérations du domaine) ; jamais de saisie libre.
    /// </summary>
    public required TvaMappingEditOptionsDto EditOptions { get; init; }
}
