namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Paramétrage tenant fictif pour la supervision : <see cref="GetCurrentCompanyId"/>,
/// <see cref="GetAlertThresholds"/> et <see cref="GetTenantProfile"/> (profil = contact d'alerte, SUP03)
/// sont configurables (présents/absents). Les autres lectures lèvent (non sollicitées).
/// </summary>
internal sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
{
    private readonly Guid? _companyId;
    private readonly AlertThresholdsDto? _thresholds;
    private readonly TenantProfileDto? _profile;

    public FakeTenantSettingsQueries(
        Guid? companyId = null,
        AlertThresholdsDto? thresholds = null,
        TenantProfileDto? profile = null)
    {
        _companyId = companyId;
        _thresholds = thresholds;
        _profile = profile;
    }

    public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

    public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_thresholds);

    public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_profile);

    public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
