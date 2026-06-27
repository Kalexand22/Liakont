namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Domain.B2cReporting;
using Liakont.Modules.Pipeline.Infrastructure.Persistence;
using Liakont.Modules.Pipeline.Infrastructure.Queries;
using Liakont.Modules.Pipeline.Tests.Integration.Fixtures;
using Xunit;

/// <summary>
/// Registre de la marge à déclarer (L2, <c>pipeline.margin_registry</c>) bout en bout sur PostgreSQL réel :
/// prouve la migration V008, l'UPSERT idempotent sur <c>document_id</c> (un doc = un taux, mise à jour en
/// place — PROJECTION recalculable, jamais append-only/WORM), la suppression, et l'agrégation mensuelle de la
/// query (GROUP BY mois×devise×taux, SOMME base HT + TVA, filtre de période). Base PARTAGÉE : chaque test
/// utilise un MOIS distinct (la query est filtrée par période) — aucune dépendance à l'ordre des tests.
/// </summary>
[Collection("PipelineIntegration")]
public sealed class MarginRegistryIntegrationTests
{
    private readonly PipelineDatabaseFixture _fixture;

    public MarginRegistryIntegrationTests(PipelineDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Upsert_Is_Idempotent_On_DocumentId_And_Updates_In_Place()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresMarginRegistryStore(factory);
        var queries = new PostgresMarginRegistryQueries(factory);
        var documentId = Guid.NewGuid();

        await store.UpsertAsync(Entry(documentId, new DateOnly(2099, 1, 10), rate: 20m, baseHt: 8.33m, vat: 1.67m));

        // Re-CHECK du MÊME document (valeurs corrigées) → mise à jour EN PLACE (ON CONFLICT), jamais un doublon.
        await store.UpsertAsync(Entry(documentId, new DateOnly(2099, 1, 10), rate: 20m, baseHt: 100.00m, vat: 20.00m));

        var row = (await queries.GetMonthlyAsync("2099-01")).Single();
        row.RatePercent.Should().Be(20m);
        row.MarginBaseHt.Should().Be(100.00m);
        row.MarginVat.Should().Be(20.00m);
        row.DocumentCount.Should().Be(1, "l'upsert remplace l'entrée du document, jamais un doublon.");
    }

    [Fact]
    public async Task GetMonthly_Groups_By_Rate_And_Sums()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresMarginRegistryStore(factory);
        var queries = new PostgresMarginRegistryQueries(factory);

        await store.UpsertAsync(Entry(Guid.NewGuid(), new DateOnly(2099, 2, 3), rate: 20m, baseHt: 100.00m, vat: 20.00m));
        await store.UpsertAsync(Entry(Guid.NewGuid(), new DateOnly(2099, 2, 20), rate: 20m, baseHt: 50.00m, vat: 10.00m));
        await store.UpsertAsync(Entry(Guid.NewGuid(), new DateOnly(2099, 2, 25), rate: 5.5m, baseHt: 50.00m, vat: 2.75m));

        var rows = (await queries.GetMonthlyAsync("2099-02")).OrderBy(r => r.RatePercent).ToList();

        rows.Should().HaveCount(2, "deux taux distincts dans le mois.");
        rows[0].RatePercent.Should().Be(5.5m);
        rows[0].MarginBaseHt.Should().Be(50.00m);
        rows[0].MarginVat.Should().Be(2.75m);
        rows[0].DocumentCount.Should().Be(1);
        rows[1].RatePercent.Should().Be(20m);
        rows[1].MarginBaseHt.Should().Be(150.00m, "la base HT est sommée sur les deux documents à 20 %.");
        rows[1].MarginVat.Should().Be(30.00m);
        rows[1].DocumentCount.Should().Be(2);
    }

    [Fact]
    public async Task Delete_Removes_The_Entry_And_Is_Idempotent()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresMarginRegistryStore(factory);
        var queries = new PostgresMarginRegistryQueries(factory);
        var documentId = Guid.NewGuid();

        await store.UpsertAsync(Entry(documentId, new DateOnly(2099, 3, 5), rate: 20m, baseHt: 100.00m, vat: 20.00m));
        (await queries.GetMonthlyAsync("2099-03")).Should().ContainSingle();

        await store.DeleteAsync(documentId);
        (await queries.GetMonthlyAsync("2099-03")).Should().BeEmpty();

        // Suppression idempotente : un document sans entrée (non-marge au re-CHECK) ne lève jamais.
        await store.DeleteAsync(documentId);
    }

    [Fact]
    public async Task GetMonthly_Filters_By_Period()
    {
        var factory = _fixture.CreateConnectionFactory();
        var store = new PostgresMarginRegistryStore(factory);
        var queries = new PostgresMarginRegistryQueries(factory);

        await store.UpsertAsync(Entry(Guid.NewGuid(), new DateOnly(2099, 4, 10), rate: 20m, baseHt: 100.00m, vat: 20.00m));
        await store.UpsertAsync(Entry(Guid.NewGuid(), new DateOnly(2099, 5, 10), rate: 20m, baseHt: 999.00m, vat: 199.80m));

        var rows = await queries.GetMonthlyAsync("2099-04");

        rows.Should().ContainSingle("le filtre de période ne retient que le mois demandé.");
        rows.Single().Period.Should().Be("2099-04");
        rows.Single().MarginBaseHt.Should().Be(100.00m);
    }

    private static MarginRegistryEntry Entry(Guid documentId, DateOnly issueDate, decimal rate, decimal baseHt, decimal vat) =>
        new()
        {
            DocumentId = documentId,
            IssueDate = issueDate,
            CurrencyCode = "EUR",
            VatRate = rate,
            MarginBaseHt = baseHt,
            MarginVat = vat,
        };
}
