namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;

/// <summary>
/// Double d'<see cref="ITenantSettingsQueries"/> pour le test de réversibilité (API03). Expose une société
/// et un compte PA dont la clé est masquée (<see cref="PaAccountDto.HasApiKey"/>) — jamais la clé elle-même.
/// </summary>
internal sealed class FakeTenantSettingsQueries : ITenantSettingsQueries
{
    public static readonly Guid CompanyId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<TenantProfileDto?>(null);

    public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<FiscalSettingsDto?>(null);

    public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaAccountDto>>(
        [
            new PaAccountDto
            {
                Id = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                CompanyId = companyId,
                PluginType = "fake-pa",
                Environment = "Sandbox",
                AccountIdentifiers = "acct-123",
                HasApiKey = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UnixEpoch,
            },
        ]);

    public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<ExtractionScheduleDto?>(null);

    public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<AlertThresholdsDto?>(null);

    public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) =>
        Task.FromResult<Guid?>(CompanyId);

    /// <summary>Statut du tenant courant : null = pas de profil = ACTIF (defaut neutre des tests).</summary>
    public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
