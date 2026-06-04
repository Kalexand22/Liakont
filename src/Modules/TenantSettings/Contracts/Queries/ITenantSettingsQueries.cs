namespace Liakont.Modules.TenantSettings.Contracts.Queries;

using Liakont.Modules.TenantSettings.Contracts.DTOs;

/// <summary>
/// Lectures du paramétrage tenant. Chaque méthode est scopée par <paramref name="companyId"/>
/// (résolu par le contexte appelant via <c>ICompanyFilter</c>) — jamais de lecture cross-tenant
/// (CLAUDE.md n°9/17). Les implémentations n'exposent JAMAIS de clé API (INV-TENANTSETTINGS-003).
/// </summary>
public interface ITenantSettingsQueries
{
    Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default);

    Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default);

    Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default);

    Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default);
}
