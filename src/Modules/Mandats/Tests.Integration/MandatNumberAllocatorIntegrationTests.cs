namespace Liakont.Modules.Mandats.Tests.Integration;

using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Allocation hybride du BT-1 fiscal 389 (MND05, ADR-0025) sur PostgreSQL réel (Testcontainers) :
/// idempotence sur la clé source (INV-BT1-2, double appel ⇒ un seul numéro), ré-extraction (document différent,
/// même source ⇒ même numéro), verrou de séquence par mandant (allocations concurrentes sérialisées, sans
/// doublon — INV-BT1-4), <c>bigint</c> (jamais float), assignation HORS payload hashé sur l'acceptation
/// (INV-BT1-1), isolation tenant (≥ 2 sociétés), immuabilité du registre d'allocation, et fail-closed
/// (source vide, mandant inconnu, acceptation absente).
/// </summary>
[Collection("MandatsIntegration")]
public sealed class MandatNumberAllocatorIntegrationTests
{
    private static readonly DateTimeOffset PendingSince = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly string[] TwoConsecutiveNumbers = { "ARM-A-2", "ARM-A-3" };

    private readonly MandatsDatabaseFixture _fixture;

    public MandatNumberAllocatorIntegrationTests(MandatsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Allocate_Assigns_First_Number_To_Acceptance_And_Advances_Bigint_Sequence()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, documentId);

        var number = await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "SRC-1");

        number.Should().Be("ARM-A-1", "le BT-1 fiscal = préfixe du mandant + valeur de séquence (départ à 1).");

        var dto = await harness.AcceptanceQueries.GetAcceptance(companyId, documentId);
        dto!.AllocatedNumber.Should().Be("ARM-A-1", "le BT-1 est assigné à l'acceptation, HORS payload hashé (INV-BT1-1).");

        (await ReadSequenceNextValueAsync(harness, companyId, mandant.Id))
            .Should().Be(2L, "la séquence (bigint) a avancé d'exactement 1.");
    }

    [Fact]
    public async Task Allocate_Is_Idempotent_On_Source_Key_Same_Document()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, documentId);

        var first = await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "SRC-1");
        var second = await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "SRC-1");

        second.Should().Be(first, "un même document source relit le même numéro, jamais ré-alloué (INV-BT1-2).");
        (await ReadSequenceNextValueAsync(harness, companyId, mandant.Id))
            .Should().Be(2L, "un seul numéro a été consommé malgré le double appel.");
        (await ReadAllocationCountAsync(harness, companyId)).Should().Be(1);
    }

    [Fact]
    public async Task ReExtraction_New_Document_Same_Source_Reuses_Number()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var firstDoc = Guid.NewGuid();
        var reExtractedDoc = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, firstDoc);
        await SeedPendingAcceptanceAsync(harness, companyId, reExtractedDoc);

        var firstNumber = await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, firstDoc, "SRC-1");
        var reExtractedNumber = await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, reExtractedDoc, "SRC-1");

        reExtractedNumber.Should().Be(firstNumber, "ré-extraction d'un même document source ⇒ même BT-1 (INV-BT1-2).");
        (await harness.AcceptanceQueries.GetAcceptance(companyId, reExtractedDoc))!.AllocatedNumber
            .Should().Be(firstNumber, "le document ré-extrait porte le même numéro sur sa propre acceptation.");
        (await ReadSequenceNextValueAsync(harness, companyId, mandant.Id))
            .Should().Be(2L, "aucun second numéro consommé pour la même source.");
    }

    [Fact]
    public async Task Sequences_Are_Independent_Per_Mandant()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var mandantA = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        var mandantB = await SeedMandantAsync(harness, companyId, "MANDANT-B", "ARM-B-");
        await SeedPendingAcceptanceAsync(harness, companyId, docA);
        await SeedPendingAcceptanceAsync(harness, companyId, docB);

        var numberA = await harness.NumberAllocator.AllocateAsync(companyId, mandantA.Id, docA, "SRC-A");
        var numberB = await harness.NumberAllocator.AllocateAsync(companyId, mandantB.Id, docB, "SRC-B");

        numberA.Should().Be("ARM-A-1");
        numberB.Should().Be("ARM-B-1", "chaque mandant a SA propre séquence (départ à 1), avec son préfixe.");
        (await ReadSequenceNextValueAsync(harness, companyId, mandantA.Id)).Should().Be(2L);
        (await ReadSequenceNextValueAsync(harness, companyId, mandantB.Id)).Should().Be(2L);
    }

    [Fact]
    public async Task Concurrent_Allocations_Same_Mandant_Are_Serialized_Without_Duplicate()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var primer = Guid.NewGuid();
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, primer);
        await SeedPendingAcceptanceAsync(harness, companyId, doc1);
        await SeedPendingAcceptanceAsync(harness, companyId, doc2);

        // Amorce : crée la ligne de séquence (valeur 1) pour que les deux appels concurrents heurtent un FOR UPDATE.
        (await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, primer, "SRC-0")).Should().Be("ARM-A-1");

        var results = await Task.WhenAll(
            harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, doc1, "SRC-1"),
            harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, doc2, "SRC-2"));

        results.Should().BeEquivalentTo(TwoConsecutiveNumbers,
            "le verrou par mandant sérialise : deux numéros consécutifs DISTINCTS, jamais un doublon (INV-BT1-4).");
        (await ReadSequenceNextValueAsync(harness, companyId, mandant.Id))
            .Should().Be(4L, "trois numéros consommés au total (1 amorce + 2 concurrents), sans trou ni doublon.");
    }

    [Fact]
    public async Task Allocation_Is_Tenant_Scoped()
    {
        var harness = new MandatsHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var docA = Guid.NewGuid();
        var mandantA = await SeedMandantAsync(harness, companyA, "MANDANT-A", "ARM-A-");
        await SeedMandantAsync(harness, companyB, "MANDANT-B", "ARM-B-");
        await SeedPendingAcceptanceAsync(harness, companyA, docA);

        await harness.NumberAllocator.AllocateAsync(companyA, mandantA.Id, docA, "SRC-A");

        (await ReadAllocationCountAsync(harness, companyA)).Should().Be(1);
        (await ReadAllocationCountAsync(harness, companyB)).Should().Be(0,
            "l'allocation de la société A n'est jamais visible sous la société B (CLAUDE.md n°9, INV-BT1-4).");
    }

    [Fact]
    public async Task Allocate_Fails_Closed_When_Mandant_Unknown()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();

        var act = () => harness.NumberAllocator.AllocateAsync(companyId, Guid.NewGuid(), Guid.NewGuid(), "SRC-1");

        await act.Should().ThrowAsync<InvalidOperationException>(
            "un mandant inconnu n'a pas de préfixe sourçable : on bloque, on ne devine pas (CLAUDE.md n°2/3).");
    }

    [Fact]
    public async Task Allocate_Fails_Closed_When_Source_Reference_Is_Blank()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, documentId);

        var act = () => harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "   ");

        await act.Should().ThrowAsync<ArgumentException>(
            "un 389 sans clé d'idempotence source n'est pas numérotable (substitution d'invariant, INV-BT1-3).");
    }

    [Fact]
    public async Task Allocate_Fails_Closed_And_Consumes_Nothing_When_Acceptance_Absent()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");

        // Aucune acceptation pour le document : l'allocation suit l'acceptation (le gate l'a déjà lue).
        var act = () => harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "SRC-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ReadSequenceNextValueAsync(harness, companyId, mandant.Id))
            .Should().BeNull("la transaction a été annulée : aucun numéro consommé si l'assignation échoue (atomicité).");
        (await ReadAllocationCountAsync(harness, companyId)).Should().Be(0);
    }

    [Fact]
    public async Task Allocation_Registry_Is_Immutable_Update_Delete_Truncate_Rejected()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var mandant = await SeedMandantAsync(harness, companyId, "MANDANT-A", "ARM-A-");
        await SeedPendingAcceptanceAsync(harness, companyId, documentId);
        await harness.NumberAllocator.AllocateAsync(companyId, mandant.Id, documentId, "SRC-1");

        using var conn = await harness.ConnectionFactory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE mandats.mandat_number_allocations SET allocated_number = 'falsifié' WHERE company_id = @c",
            new { c = companyId });
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM mandats.mandat_number_allocations WHERE company_id = @c", new { c = companyId });
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE mandats.mandat_number_allocations");

        (await update.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("immuable");
        await delete.Should().ThrowAsync<PostgresException>();
        await truncate.Should().ThrowAsync<PostgresException>();

        (await ReadAllocationCountAsync(harness, companyId)).Should().Be(1, "le numéro fiscal alloué reste intact.");
    }

    private static async Task<Mandant> SeedMandantAsync(
        MandatsHarness harness, Guid companyId, string reference, string numberingPrefix)
    {
        var mandant = Mandant.Create(companyId, reference, "Armement Exemple", null, "000000000", numberingPrefix);
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = MandatChangeLogFactory.ForCreateMandant(mandant, Guid.NewGuid(), "Opérateur de test");
        await uow.InsertMandantAsync(mandant, entry);
        await uow.CommitAsync();
        return mandant;
    }

    private static async Task SeedPendingAcceptanceAsync(MandatsHarness harness, Guid companyId, Guid documentId)
    {
        var acceptance = SelfBilledAcceptance.Create(companyId, documentId, PendingSince, PendingSince.AddDays(30));
        await using var uow = await harness.AcceptanceUowFactory.BeginAsync();
        var entry = SelfBilledAcceptanceLogFactory.ForCreation(acceptance, operatorId: null, "Ingestion (test)");
        await uow.InsertAsync(acceptance, entry);
        await uow.CommitAsync();
    }

    private static async Task<long?> ReadSequenceNextValueAsync(MandatsHarness harness, Guid companyId, Guid mandantId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<long?>(
            "SELECT next_value FROM mandats.mandat_sequences WHERE company_id = @c AND mandant_id = @m",
            new { c = companyId, m = mandantId });
    }

    private static async Task<int> ReadAllocationCountAsync(MandatsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM mandats.mandat_number_allocations WHERE company_id = @c", new { c = companyId });
    }
}
