namespace Liakont.Host.Parametrage;

using System.Collections.Generic;
using Liakont.Host.Components;
using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Données présentationnelles de la page Paramétrage du tenant (WEB04b), assemblées par la page
/// <c>Parametrage</c> à partir des lectures Contracts (TenantSettings via <c>GET /settings</c> +
/// registre d'agents) et rendues par <c>ParametrageView</c>. Modèle PUR (aucune dépendance DI,
/// aucune logique métier) pour rester testable en bUnit sans authentification ni base. Les DTO
/// exposés sont des projections de lecture, secrets masqués en amont (jamais de clé/chaîne ODBC).
/// </summary>
public sealed record ParametrageViewModel
{
    /// <summary>Profil légal du tenant, ou <c>null</c> tant qu'il n'est pas créé (CFG02).</summary>
    public TenantProfileDto? Profile { get; init; }

    /// <summary>Paramétrage fiscal du tenant, ou <c>null</c> (décision expert-comptable en attente).</summary>
    public FiscalSettingsDto? FiscalSettings { get; init; }

    /// <summary>Résumé d'état de la table de mapping TVA, ou <c>null</c> si aucune table n'est paramétrée.</summary>
    public TvaMappingSummaryDto? TvaMapping { get; init; }

    /// <summary>Comptes PA configurés (secrets masqués + capacités déclarées). Jamais <c>null</c> (vide si aucun).</summary>
    public required IReadOnlyList<PaAccountSettingsDto> PaAccounts { get; init; }

    /// <summary>Agents du tenant en lecture seule (vide si aucun agent enregistré).</summary>
    public required IReadOnlyList<AgentStatusLine> Agents { get; init; }
}
