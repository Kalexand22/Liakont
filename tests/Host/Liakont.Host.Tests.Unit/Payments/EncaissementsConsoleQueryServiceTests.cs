namespace Liakont.Host.Tests.Unit.Payments;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Payments;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using Xunit;

public sealed class EncaissementsConsoleQueryServiceTests
{
    [Fact]
    public async Task FiscalDecisionPending_Is_True_When_An_Aggregate_Is_Suspended()
    {
        var service = Service([Aggregate("Calculated"), Aggregate("Suspended")], Overview());

        var model = await service.GetEncaissementsAsync(period: "2026-06");

        // Parité avec PaymentsResponse.FiscalDecisionPending : au moins un agrégat suspendu (PIP03a).
        model.FiscalDecisionPending.Should().BeTrue();
    }

    [Fact]
    public async Task FiscalDecisionPending_Is_False_When_No_Aggregate_Is_Suspended()
    {
        var service = Service(
            [Aggregate("Calculated"), Aggregate("NotRequired"), Aggregate("PendingCapability")],
            Overview());

        var model = await service.GetEncaissementsAsync(period: null);

        model.FiscalDecisionPending.Should().BeFalse();
    }

    [Fact]
    public async Task PaymentReporting_Is_Supported_When_A_Loaded_Plugin_Declares_The_Capability()
    {
        var service = Service([], Overview(PaAccount(pluginAvailable: true, supportsPayments: true, paName: "B2Brouter")));

        var model = await service.GetEncaissementsAsync(period: null);

        model.HasConfiguredPa.Should().BeTrue();
        model.PaymentReportingSupported.Should().BeTrue();
        model.PaName.Should().Be("B2Brouter");
    }

    [Fact]
    public async Task PaymentReporting_Is_Not_Supported_When_Plugin_Unavailable_Or_Capability_False()
    {
        var unavailable = Service([], Overview(PaAccount(pluginAvailable: false, supportsPayments: false, pluginType: "superpdp")));
        var capabilityFalse = Service([], Overview(PaAccount(pluginAvailable: true, supportsPayments: false)));

        (await unavailable.GetEncaissementsAsync(null)).PaymentReportingSupported.Should().BeFalse();
        (await capabilityFalse.GetEncaissementsAsync(null)).PaymentReportingSupported.Should().BeFalse();
    }

    [Fact]
    public async Task No_Configured_Pa_Yields_No_Support_And_No_Name()
    {
        var service = Service([], Overview());

        var model = await service.GetEncaissementsAsync(period: null);

        model.HasConfiguredPa.Should().BeFalse();
        model.PaymentReportingSupported.Should().BeFalse();
        model.PaName.Should().BeNull();
    }

    [Fact]
    public async Task PaName_Falls_Back_To_PluginType_When_Capabilities_Are_Absent()
    {
        var service = Service([], Overview(PaAccount(pluginAvailable: false, supportsPayments: false, pluginType: "superpdp")));

        var model = await service.GetEncaissementsAsync(period: null);

        // Plug-in non chargé → pas de capacités → repli sur le type du plug-in pour nommer la PA.
        model.PaName.Should().Be("superpdp");
    }

    [Fact]
    public async Task Maps_Aggregates_With_A_Reason_Placeholder_When_Reason_Is_Absent()
    {
        var service = Service([Aggregate("Calculated")], Overview());

        var model = await service.GetEncaissementsAsync(period: null);

        model.Aggregates.Should().ContainSingle();
        model.Aggregates[0].Reason.Should().Be("—");
    }

    private static EncaissementsConsoleQueryService Service(
        IReadOnlyList<PaymentDailyAggregateDto> aggregates,
        TenantSettingsOverviewDto overview) =>
        new(new FakePaymentAggregationQueries(aggregates), new FakeTenantSettingsConsoleQueries(overview));

    private static PaymentDailyAggregateDto Aggregate(string status) => new()
    {
        Id = Guid.NewGuid(),
        AggregateDate = new DateOnly(2026, 6, 1),
        VatRate = 20m,
        TaxableBase = 100.00m,
        VatAmount = 20.00m,
        Status = status,
        Reason = string.Equals(status, "Calculated", StringComparison.Ordinal) ? null : "motif opérateur",
        ComputedUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static TenantSettingsOverviewDto Overview(params PaAccountSettingsDto[] accounts) => new()
    {
        Profile = null,
        FiscalSettings = null,
        TvaMapping = null,
        PaAccounts = accounts,
    };

    private static PaAccountSettingsDto PaAccount(
        bool pluginAvailable,
        bool supportsPayments,
        string paName = "B2Brouter",
        string pluginType = "b2brouter") => new()
    {
        Account = new PaAccountDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PluginType = pluginType,
            Environment = "test",
            AccountIdentifiers = "compte-démo",
            HasApiKey = true,
            IsActive = true,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        PluginAvailable = pluginAvailable,
        Capabilities = pluginAvailable ? Capabilities(paName, supportsPayments) : null,
    };

    private static PaCapabilitiesSummaryDto Capabilities(string paName, bool supportsPayments) => new()
    {
        PaName = paName,
        SupportsB2cReporting = false,
        SupportsDomesticPaymentReporting = supportsPayments,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = false,
        SupportsCreditNotes = false,
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = false,
        SupportsSelfBilling = false,
        MaxDocumentsPerRequest = null,
    };

    private sealed class FakePaymentAggregationQueries : IPaymentAggregationQueries
    {
        private readonly IReadOnlyList<PaymentDailyAggregateDto> _aggregates;

        public FakePaymentAggregationQueries(IReadOnlyList<PaymentDailyAggregateDto> aggregates) => _aggregates = aggregates;

        public Task<IReadOnlyList<PaymentDailyAggregateDto>> GetAggregationsAsync(string? period, CancellationToken cancellationToken = default) =>
            Task.FromResult(_aggregates);
    }

    private sealed class FakeTenantSettingsConsoleQueries : ITenantSettingsConsoleQueries
    {
        private readonly TenantSettingsOverviewDto _overview;

        public FakeTenantSettingsConsoleQueries(TenantSettingsOverviewDto overview) => _overview = overview;

        public Task<TenantSettingsOverviewDto> GetSettingsOverview(CancellationToken ct = default) =>
            Task.FromResult(_overview);
    }
}
