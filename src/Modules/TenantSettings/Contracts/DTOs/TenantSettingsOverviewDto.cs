namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Vue d'ensemble du paramétrage du tenant pour la console (API01c, <c>GET /api/v1/settings</c>) :
/// profil légal, paramétrage fiscal, comptes Plateforme Agréée (secrets masqués + capacités déclarées)
/// et état de la table TVA. Tenant-scopée (CLAUDE.md n°9/17). Tous les membres sont nullables / vides
/// tant que le tenant n'est pas (entièrement) paramétré : un profil non encore créé (CFG02) renvoie une
/// vue VIDE en 200 (état transitoire, pas une erreur). N'expose JAMAIS de secret (INV-TENANTSETTINGS-003)
/// ni de capacité agent/adaptateur (reportée à API01d — read-model non sourcé).
/// </summary>
public record TenantSettingsOverviewDto
{
    /// <summary>Profil légal du tenant, ou <c>null</c> tant qu'il n'est pas créé (CFG02).</summary>
    public TenantProfileDto? Profile { get; init; }

    /// <summary>Paramétrage fiscal du tenant, ou <c>null</c> (décision expert-comptable en attente).</summary>
    public FiscalSettingsDto? FiscalSettings { get; init; }

    /// <summary>Résumé d'état de la table TVA, ou <c>null</c> si aucune table n'est paramétrée.</summary>
    public TvaMappingSummaryDto? TvaMapping { get; init; }

    /// <summary>Comptes PA configurés (secrets masqués + capacités déclarées). Jamais <c>null</c> (vide si aucun).</summary>
    public required IReadOnlyList<PaAccountSettingsDto> PaAccounts { get; init; }
}
