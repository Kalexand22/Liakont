namespace Liakont.Modules.Supervision.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Paramétrage tenant fictif pour les règles de supervision : <see cref="GetCurrentCompanyId"/> et
/// <see cref="GetAlertThresholds"/> sont configurables (company présente/absente, seuils présents/absents).
/// Les autres lectures lèvent (non sollicitées par les règles SUP01b).
/// </summary>
internal sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
{
    private readonly Guid? _companyId;
    private readonly AlertThresholdsDto? _thresholds;

    public FakeTenantSettingsQueries(Guid? companyId = null, AlertThresholdsDto? thresholds = null)
    {
        _companyId = companyId;
        _thresholds = thresholds;
    }

    public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) => Task.FromResult(_companyId);

    public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(_thresholds);

    public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
