namespace Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Compte Plateforme Agréée vu par le paramétrage de la console (API01c) : le compte (secrets masqués —
/// <see cref="PaAccountDto.HasApiKey"/> uniquement, INV-TENANTSETTINGS-003) ENRICHI de ses capacités
/// déclarées. <see cref="Capabilities"/> est <c>null</c> et <see cref="PluginAvailable"/> est <c>false</c>
/// quand aucun plug-in n'est chargé pour le <see cref="PaAccountDto.PluginType"/> du compte : l'endpoint
/// reste alors une lecture valide (200) et signale le défaut de configuration plutôt que d'échouer
/// (CLAUDE.md n°12 — message opérateur) ; jamais de capacité inventée pour un plug-in absent.
/// </summary>
public record PaAccountSettingsDto
{
    /// <summary>Compte PA en lecture (secrets toujours masqués — voir <see cref="PaAccountDto"/>).</summary>
    public required PaAccountDto Account { get; init; }

    /// <summary>
    /// <c>true</c> si un plug-in est chargé pour le type de PA du compte (capacités résolues) ;
    /// <c>false</c> = type non enregistré sur cette instance (capacités indisponibles).
    /// </summary>
    public required bool PluginAvailable { get; init; }

    /// <summary>Capacités déclarées de la PA, ou <c>null</c> si <see cref="PluginAvailable"/> est <c>false</c>.</summary>
    public PaCapabilitiesSummaryDto? Capabilities { get; init; }
}
