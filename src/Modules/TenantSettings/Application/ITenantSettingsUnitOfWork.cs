namespace Liakont.Modules.TenantSettings.Application;

using Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Unité de travail transactionnelle du module. Toutes les lectures et écritures sont scopées
/// par <c>companyId</c> (résolu via <c>ICompanyFilter</c> par l'appelant) — jamais de requête
/// cross-tenant (CLAUDE.md n°9/17). Le module ne fait que du CRUD de paramétrage : aucun
/// update/delete sur une table d'audit (la piste d'audit reste append-only, hors de cette UoW).
/// </summary>
public interface ITenantSettingsUnitOfWork : IAsyncDisposable
{
    // ── Profil ──
    Task<TenantProfile?> GetTenantProfileByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task InsertTenantProfileAsync(TenantProfile profile, CancellationToken ct = default);

    Task UpdateTenantProfileAsync(TenantProfile profile, CancellationToken ct = default);

    // ── Paramétrage fiscal ──
    Task<FiscalSettings?> GetFiscalSettingsByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task InsertFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default);

    Task UpdateFiscalSettingsAsync(FiscalSettings settings, CancellationToken ct = default);

    // ── Comptes PA ──
    Task<PaAccount?> GetPaAccountByIdAsync(Guid id, Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<PaAccount>> GetPaAccountsByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task InsertPaAccountAsync(PaAccount account, CancellationToken ct = default);

    Task UpdatePaAccountAsync(PaAccount account, CancellationToken ct = default);

    // ── Planification d'extraction ──
    Task<ExtractionSchedule?> GetExtractionScheduleByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task InsertExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default);

    Task UpdateExtractionScheduleAsync(ExtractionSchedule schedule, CancellationToken ct = default);

    // ── Seuils d'alerte ──
    Task<AlertThresholds?> GetAlertThresholdsByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task InsertAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default);

    Task UpdateAlertThresholdsAsync(AlertThresholds thresholds, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
}

/// <summary>Fabrique d'unités de travail (ouvre une transaction sur la base du tenant courant).</summary>
public interface ITenantSettingsUnitOfWorkFactory
{
    Task<ITenantSettingsUnitOfWork> BeginAsync(CancellationToken ct = default);
}
