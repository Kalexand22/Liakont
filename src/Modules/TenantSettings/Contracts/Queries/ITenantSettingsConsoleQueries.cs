namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Lecture composée du paramétrage du tenant pour la console (API01c). Assemble, pour le tenant COURANT
/// (résolu sans <c>companyId</c>, comme <see cref="ITenantSettingsQueries.GetCurrentCompanyId"/>), le
/// profil + fiscal + comptes PA (secrets masqués) + l'état de la table TVA + les capacités déclarées des
/// PA configurées. La composition cross-module (TVA, capacités PA) vit dans l'Infrastructure du module
/// (frontière Contracts — même patron que les services d'export d'Archive consommés par API03) ; la
/// couche Web ne référence que cette interface. Tenant-scopée (CLAUDE.md n°9/17).
/// </summary>
public interface ITenantSettingsConsoleQueries
{
    /// <summary>
    /// Retourne la vue d'ensemble du paramétrage du tenant courant. Renvoie une vue VIDE (profil/fiscal/
    /// TVA <c>null</c>, comptes PA vides) tant que le profil n'est pas créé (CFG02) — jamais <c>null</c>.
    /// </summary>
    /// <param name="ct">Jeton d'annulation.</param>
    Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default);
}
