namespace Liakont.Host.Tests.Unit.MultiTenancy;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.MultiTenancy;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Xunit;

/// <summary>
/// Tests de la sonde de paramétrage tenant (<see cref="TenantConfigurationProbe.HasAnyConfigurationAsync"/>),
/// socle de la garde provisioning create-only (BUG-14). La raison d'être de la sonde est de couvrir un seed
/// dont l'UNIQUE composant serait la planification, les seuils OU un compte PA (pas seulement le fiscal) —
/// d'où des cas dédiés exerçant CHAQUE branche, y compris les trois non-fiscales : ancrer la garde sur le
/// seul fiscal laisserait un tel ré-import écraser silencieusement des réglages saisis via la console.
/// </summary>
public sealed class TenantConfigurationProbeTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Returns_False_When_No_Component_Is_Present()
    {
        var queries = new FakeQueries();

        (await queries.HasAnyConfigurationAsync(Company)).Should().BeFalse();
    }

    [Fact]
    public async Task Returns_True_When_Only_Fiscal_Is_Present()
    {
        var queries = new FakeQueries { Fiscal = AFiscal() };

        (await queries.HasAnyConfigurationAsync(Company)).Should().BeTrue();
    }

    [Fact]
    public async Task Returns_True_When_Only_The_Schedule_Is_Present()
    {
        // Branche non-fiscale : un seed dont le seul bloc est « schedule » doit déclencher la garde.
        var queries = new FakeQueries { Schedule = ASchedule() };

        (await queries.HasAnyConfigurationAsync(Company)).Should().BeTrue();
    }

    [Fact]
    public async Task Returns_True_When_Only_The_Thresholds_Are_Present()
    {
        var queries = new FakeQueries { Thresholds = AThresholds() };

        (await queries.HasAnyConfigurationAsync(Company)).Should().BeTrue();
    }

    [Fact]
    public async Task Returns_True_When_Only_A_Pa_Account_Is_Present()
    {
        var queries = new FakeQueries { PaAccounts = [APaAccount()] };

        (await queries.HasAnyConfigurationAsync(Company)).Should().BeTrue();
    }

    private static FiscalSettingsDto AFiscal() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        CreatedAt = default,
    };

    private static ExtractionScheduleDto ASchedule() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        Hours = ["03:00"],
        CatchUpOnStart = true,
        CreatedAt = default,
    };

    private static AlertThresholdsDto AThresholds() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        AgentSilentHours = 12,
        MissedRunHours = 24,
        PushQueueMaxItems = 50,
        PushQueueMaxAgeHours = 6,
        BlockedDocumentsDays = 5,
        PaRejectionsDays = 2,
        AlertTenantContact = false,
        CreatedAt = default,
    };

    private static PaAccountDto APaAccount() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        PluginType = "Fake",
        Environment = "Staging",
        AccountIdentifiers = "{}",
        HasApiKey = false,
        IsActive = true,
        CreatedAt = default,
    };

    private sealed class FakeQueries : ITenantSettingsQueries
    {
        public FiscalSettingsDto? Fiscal { get; init; }

        public ExtractionScheduleDto? Schedule { get; init; }

        public AlertThresholdsDto? Thresholds { get; init; }

        public IReadOnlyList<PaAccountDto> PaAccounts { get; init; } = [];

        public Task<FiscalSettingsDto?> GetFiscalSettings(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Fiscal);

        public Task<ExtractionScheduleDto?> GetExtractionSchedule(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Schedule);

        public Task<AlertThresholdsDto?> GetAlertThresholds(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(Thresholds);

        public Task<IReadOnlyList<PaAccountDto>> GetPaAccounts(Guid companyId, CancellationToken ct = default) =>
            Task.FromResult(PaAccounts);

        // Méthodes hors périmètre de la sonde — jamais appelées par HasAnyConfigurationAsync.
        public Task<TenantProfileDto?> GetTenantProfile(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BillingMentionsDto?> GetBillingMentions(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> GetAuctionVerticalEnabled(Guid companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetCurrentCompanyId(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> GetCurrentTenantStatut(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
