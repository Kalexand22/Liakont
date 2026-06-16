namespace Liakont.Modules.Mandats.Tests.Integration;

using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Persistance du registre des mandants (F15 §2.2) sur PostgreSQL réel (Testcontainers) : round-trip,
/// n° TVA nullable, isolation par tenant (INV-MANDATS-1), conflit d'unicité, mutation journalisée.
/// </summary>
[Collection("MandatsIntegration")]
public sealed class MandantPersistenceIntegrationTests
{
    private readonly MandatsDatabaseFixture _fixture;

    public MandantPersistenceIntegrationTests(MandatsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_And_Get_RoundTrips_All_Fields()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandant = Mandant.Create(companyId, "MANDANT-EXEMPLE-1", "Ferme Exemple", "FR00 000000000", "000000000", "EXM-");

        await InsertMandantAsync(harness, mandant, Guid.NewGuid());

        var dto = await harness.Queries.GetMandant(companyId, "MANDANT-EXEMPLE-1");
        dto.Should().NotBeNull();
        dto!.Reference.Should().Be("MANDANT-EXEMPLE-1");
        dto.RaisonSociale.Should().Be("Ferme Exemple");
        dto.SellerVatNumber.Should().Be("FR00 000000000");
        dto.Siren.Should().Be("000000000");
        dto.NumberingPrefix.Should().Be("EXM-");
    }

    [Fact]
    public async Task Insert_With_Null_Vat_RoundTrips()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandant = Mandant.Create(companyId, "MANDANT-EXEMPLE-1", "Ferme Exemple", null, "000000000", "EXM-");

        await InsertMandantAsync(harness, mandant, Guid.NewGuid());

        var dto = await harness.Queries.GetMandant(companyId, "MANDANT-EXEMPLE-1");
        dto!.SellerVatNumber.Should().BeNull();
    }

    [Fact]
    public async Task Get_Returns_Null_When_Absent_For_Tenant()
    {
        var harness = new MandatsHarness(_fixture);
        var dto = await harness.Queries.GetMandant(Guid.NewGuid(), "MANDANT-EXEMPLE-1");
        dto.Should().BeNull();
    }

    [Fact]
    public async Task Tenant_Isolation_Is_Enforced()
    {
        var harness = new MandatsHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await InsertMandantAsync(harness, Mandant.Create(companyA, "MANDANT-A", "Ferme A", null, "000000000", "A-"), Guid.NewGuid());
        await InsertMandantAsync(harness, Mandant.Create(companyB, "MANDANT-B", "Ferme B", null, "111111111", "B-"), Guid.NewGuid());

        (await harness.Queries.GetMandant(companyA, "MANDANT-B")).Should().BeNull("la société A ne voit pas le mandant de B.");
        (await harness.Queries.ListMandants(companyA)).Should().ContainSingle().Which.Reference.Should().Be("MANDANT-A");
        (await harness.Queries.ListMandants(companyB)).Should().ContainSingle().Which.Reference.Should().Be("MANDANT-B");
    }

    [Fact]
    public async Task Insert_Duplicate_Reference_For_Same_Tenant_Throws_Conflict()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();

        await InsertMandantAsync(harness, Mandant.Create(companyId, "MANDANT-DUP", "Ferme", null, "000000000", "D-"), Guid.NewGuid());

        var act = () => InsertMandantAsync(harness, Mandant.Create(companyId, "MANDANT-DUP", "Autre", null, "111111111", "D2-"), Guid.NewGuid());
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Create_Mandant_Logs_CreateMandant_Entry()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        await InsertMandantAsync(harness, Mandant.Create(companyId, "MANDANT-EXEMPLE-1", "Ferme", null, "000000000", "EXM-"), operatorId);

        var log = await harness.Queries.GetChangeLog(companyId);
        log.Should().ContainSingle();
        log[0].ChangeType.Should().Be(nameof(MandatChangeType.CreateMandant));
        log[0].MandatId.Should().BeNull("une création de mandant ne porte pas sur un mandat.");
        log[0].Reference.Should().Be("MANDANT-EXEMPLE-1");
        log[0].OperatorId.Should().Be(operatorId);
        log[0].BeforeValue.Should().BeNull();
        log[0].AfterValue.Should().NotBeNull();
    }

    private static async Task InsertMandantAsync(MandatsHarness harness, Mandant mandant, Guid operatorId)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = MandatChangeLogFactory.ForCreateMandant(mandant, operatorId, "Opérateur de test");
        await uow.InsertMandantAsync(mandant, entry);
        await uow.CommitAsync();
    }
}
