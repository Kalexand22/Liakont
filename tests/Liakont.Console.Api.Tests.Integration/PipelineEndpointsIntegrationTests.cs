namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process des endpoints de lecture du module Pipeline pour la console (API01b) :
/// <c>GET /runs</c> (journal des traitements, filtre par dates) et <c>GET /payments</c> (agrégats jour×taux
/// + état des paramètres fiscaux). Vérifie la permission <c>liakont.read</c> (401/403), l'isolation tenant
/// (A≠B, physique), les filtres (intervalle de dates, période année-mois) et l'exposition de la décision
/// fiscale en attente (statut <c>Suspended</c> + motif), sans aucune redérivation de règle fiscale.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class PipelineEndpointsIntegrationTests
{
    private const string RunsPath = "/api/v1/runs";
    private const string PaymentsPath = "/api/v1/payments";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConsoleApiFactory _factory;

    public PipelineEndpointsIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRuns_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.GetAsync(RunsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRuns_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.GetAsync(RunsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRuns_As_Reader_Returns_Tenant_Scoped_List()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(client, RunsPath);

        runs.Should().HaveCount(2, "le tenant A a exactement deux traitements seedés");
        runs.Should().Contain(r => r.Detail == ConsoleApiFactory.TenantAJanRunDetail);
        runs.Should().Contain(r => r.Detail == ConsoleApiFactory.TenantAFebRunDetail);
        runs.Should().NotContain(r => r.Detail == ConsoleApiFactory.TenantBRunDetail);
    }

    [Fact]
    public async Task GetRuns_Ordered_By_Started_Descending()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(client, RunsPath);

        runs.Should().BeInDescendingOrder(r => r.StartedAt);
    }

    [Fact]
    public async Task GetRuns_Filter_By_Date_Range_Bounds_Results()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(client, $"{RunsPath}?from=2026-02-01&to=2026-02-28");

        runs.Should().ContainSingle();
        runs[0].Detail.Should().Be(ConsoleApiFactory.TenantAFebRunDetail);
        runs[0].DocumentsProcessed.Should().Be(5);
    }

    [Fact]
    public async Task GetRuns_Filter_To_Is_Inclusive_At_Day_Boundary()
    {
        // Le run de février est à 2026-02-15T08:00Z ; un filtre from=to=2026-02-15 ne le retient QUE si la
        // borne haute est inclusive au jour (logique AddDays(1) → borne exclusive au lendemain minuit). Sans
        // cette inclusivité, started_at 08:00 serait exclu par une borne « < 2026-02-15T00:00 » — faux-vert détecté.
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(client, $"{RunsPath}?from=2026-02-15&to=2026-02-15");

        runs.Should().ContainSingle();
        runs[0].Detail.Should().Be(ConsoleApiFactory.TenantAFebRunDetail);
    }

    [Fact]
    public async Task GetRuns_Filter_By_Date_Range_Excluding_All_Returns_Empty()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(client, $"{RunsPath}?from=2030-01-01");

        runs.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRuns_Is_Tenant_Isolated()
    {
        using var clientB = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var runs = await GetRunsAsync(clientB, RunsPath);

        runs.Should().ContainSingle("le tenant B n'a qu'un traitement seedé");
        runs[0].Detail.Should().Be(ConsoleApiFactory.TenantBRunDetail);
    }

    [Fact]
    public async Task GetPayments_Without_Authentication_Returns_401()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA);

        var response = await client.GetAsync(PaymentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayments_Without_Read_Permission_Returns_403()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.NoPermissionUserId);

        var response = await client.GetAsync(PaymentsPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPayments_As_Reader_Exposes_Aggregates_And_Fiscal_Decision_Pending()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var payments = await GetPaymentsAsync(client, PaymentsPath);

        payments.Aggregates.Should().HaveCount(2, "le tenant A a deux agrégats seedés (un Calculated, un Suspended)");
        payments.Aggregates.Should().Contain(a => a.Status == "Calculated");
        payments.Aggregates.Should().Contain(a => a.Status == "Suspended");
        payments.FiscalDecisionPending.Should().BeTrue("un agrégat est suspendu faute de décision fiscale");
        payments.FiscalDecisionReason.Should().Be(ConsoleApiFactory.FiscalPendingReasonText);
    }

    [Fact]
    public async Task GetPayments_Filter_By_Period_Bounds_Results()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var inPeriod = await GetPaymentsAsync(client, $"{PaymentsPath}?period=2026-01");
        inPeriod.Aggregates.Should().HaveCount(2, "les deux agrégats du tenant A sont datés de janvier 2026");

        var outOfPeriod = await GetPaymentsAsync(client, $"{PaymentsPath}?period=2026-02");
        outOfPeriod.Aggregates.Should().BeEmpty();
        outOfPeriod.FiscalDecisionPending.Should().BeFalse();
    }

    [Fact]
    public async Task GetPayments_Invalid_Period_Returns_400()
    {
        using var client = _factory.CreateClient(ConsoleApiFactory.TenantA, ConsoleApiFactory.ReaderUserId);

        var response = await client.GetAsync($"{PaymentsPath}?period=janvier-2026");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPayments_Is_Tenant_Isolated_Without_Fiscal_Pending()
    {
        using var clientB = _factory.CreateClient(ConsoleApiFactory.TenantB, ConsoleApiFactory.ReaderUserId);

        var payments = await GetPaymentsAsync(clientB, PaymentsPath);

        payments.Aggregates.Should().ContainSingle("le tenant B n'a qu'un agrégat seedé");
        payments.Aggregates[0].Status.Should().Be("Calculated");
        payments.Aggregates[0].TaxableBase.Should().Be(300.00m);
        payments.FiscalDecisionPending.Should().BeFalse();
        payments.FiscalDecisionReason.Should().BeNull();
    }

    private static async Task<List<RunItem>> GetRunsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<RunItem>>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private static async Task<PaymentsResponse> GetPaymentsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaymentsResponse>(JsonOptions);
        result.Should().NotBeNull();
        return result!;
    }

    private sealed record RunItem(
        Guid Id,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        int DocumentsProcessed,
        int DocumentsSucceeded,
        int DocumentsFailed,
        string? Detail);

    private sealed record PaymentsResponse(
        List<AggregateItem> Aggregates,
        bool FiscalDecisionPending,
        string? FiscalDecisionReason);

    private sealed record AggregateItem(
        Guid Id,
        DateOnly AggregateDate,
        decimal VatRate,
        decimal TaxableBase,
        decimal VatAmount,
        string Status,
        string? Reason,
        DateTimeOffset ComputedUtc);
}
