namespace Liakont.Modules.Ingestion.Tests.Integration.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Stub des lectures de paramétrage tenant (TenantSettings.Contracts) pour les tests d'ingestion. Par
/// défaut « profil non configuré » (companyId / profil / fiscal nuls → l'émetteur n'est PAS rempli, le
/// comportement reste celui d'avant pour les tests existants). Renseigner <see cref="CompanyId"/> +
/// <see cref="Profile"/> + <see cref="Fiscal"/> prouve le remplissage de l'émetteur à l'ingestion
/// (ADR-0023 amendé).
/// </summary>
internal sealed class StubTenantSettingsQueries : ITenantSettingsQueries
{
    public Guid? CompanyId { get; set; }

    public TenantProfileDto? Profile { get; set; }

    public FiscalSettingsDto? Fiscal { get; set; }

    public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(CompanyId);

    public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) => Task.FromResult(Profile);

    public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) => Task.FromResult(Fiscal);

    public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaAccountDto>>(Array.Empty<PaAccountDto>());

    public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<ExtractionScheduleDto?>(null);

    public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<AlertThresholdsDto?>(null);

    public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) => Task.FromResult<string?>(null);
}
