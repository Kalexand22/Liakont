namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// Persistance du document et de sa piste d'audit (item TRK01) sur PostgreSQL réel (Testcontainers) :
/// round-trip de tous les champs, round-trip decimal sans perte (montants piégeux), idempotence de la
/// création par identifiant, upsert par identifiant, requêtes par numéro et par état paginées.
/// Couvre INV-DOCUMENTS-002/004/005/006.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentPersistenceIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentPersistenceIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateDetected_RoundTrips_All_Fields_And_Writes_Genesis_Event()
    {
        var harness = new DocumentsHarness(_fixture);
        var document = DocumentTestData.NewDetected();
        var genesis = DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var created = await uow.CreateDetectedAsync(document, genesis);
            created.Should().BeTrue();
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByIdAsync(document.Id);
        dto.Should().NotBeNull();
        dto!.SourceReference.Should().Be("SRC-1");
        dto.DocumentNumber.Should().Be("F-2026-001");
        dto.DocumentType.Should().Be("FAC");
        dto.IssueDate.Should().Be(new DateOnly(2026, 5, 14));
        dto.SupplierSiren.Should().Be("123456789");
        dto.CustomerName.Should().Be("Client SARL");
        dto.CustomerIsCompanyHint.Should().BeTrue();
        dto.State.Should().Be(nameof(DocumentState.Detected));
        dto.PayloadHash.Should().Be("hash-1");
        dto.PaDocumentId.Should().BeNull();
        dto.MappingVersion.Should().BeNull();

        var events = await harness.Queries.GetEventsAsync(document.Id);
        events.Should().ContainSingle();
        events[0].EventType.Should().Be(nameof(DocumentEventType.DocumentDetected));
        events[0].OperatorIdentity.Should().BeNull();
    }

    [Theory]
    [InlineData("0.10", "0.20", "0.30")]
    [InlineData("1000.00", "162.80", "1162.80")]
    [InlineData("0.01", "0.00", "0.01")]
    public async Task CreateDetected_RoundTrips_Decimal_Amounts_Without_Loss(string net, string tax, string gross)
    {
        var harness = new DocumentsHarness(_fixture);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var totalNet = decimal.Parse(net, inv);
        var totalTax = decimal.Parse(tax, inv);
        var totalGross = decimal.Parse(gross, inv);

        var document = DocumentTestData.NewDetected(
            documentNumber: $"DEC-{Guid.NewGuid():N}",
            totalNet: totalNet,
            totalTax: totalTax,
            totalGross: totalGross);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByIdAsync(document.Id);
        dto!.TotalNet.Should().Be(totalNet);
        dto.TotalTax.Should().Be(totalTax);
        dto.TotalGross.Should().Be(totalGross);
    }

    [Fact]
    public async Task GetById_Returns_Null_When_Absent()
    {
        var harness = new DocumentsHarness(_fixture);
        (await harness.Queries.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task CreateDetected_Is_Idempotent_On_Id()
    {
        var harness = new DocumentsHarness(_fixture);
        var document = DocumentTestData.NewDetected(documentNumber: $"IDEM-{Guid.NewGuid():N}");

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt)))
                .Should().BeTrue();
            await uow.CommitAsync();
        }

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            (await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt)))
                .Should().BeFalse("un second push du même identifiant ne recrée rien.");
            await uow.CommitAsync();
        }

        var events = await harness.Queries.GetEventsAsync(document.Id);
        events.Should().ContainSingle("l'événement de genèse n'est pas dupliqué par un re-push.");
    }

    [Fact]
    public async Task Upsert_Updates_Existing_By_Id_And_Preserves_FirstSeen()
    {
        var harness = new DocumentsHarness(_fixture);
        var id = Guid.NewGuid();
        var detected = DocumentTestData.NewDetected(id: id, documentNumber: $"UPS-{Guid.NewGuid():N}");

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.UpsertDocumentAsync(detected);
            await uow.CommitAsync();
        }

        var laterUpdate = DocumentTestData.DetectedAt.AddHours(2);
        var issued = Document.Reconstitute(
            id,
            detected.SourceReference,
            detected.DocumentNumber,
            detected.DocumentType,
            detected.IssueDate,
            detected.SupplierSiren,
            detected.CustomerName,
            detected.CustomerIsCompanyHint,
            detected.TotalNet,
            detected.TotalTax,
            detected.TotalGross,
            DocumentState.Issued,
            detected.PayloadHash,
            paDocumentId: "PA-XYZ",
            mappingVersion: "cmp-v1",
            firstSeenUtc: detected.FirstSeenUtc,
            lastUpdateUtc: laterUpdate);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.UpsertDocumentAsync(issued);
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByIdAsync(id);
        dto!.State.Should().Be(nameof(DocumentState.Issued));
        dto.PaDocumentId.Should().Be("PA-XYZ");
        dto.MappingVersion.Should().Be("cmp-v1");
        dto.FirstSeenUtc.Should().Be(DocumentTestData.DetectedAt, "first_seen_utc n'est jamais écrasé par un upsert.");
        dto.LastUpdateUtc.Should().Be(laterUpdate);
    }

    [Fact]
    public async Task GetByNumber_Returns_Most_Recent_For_That_Number()
    {
        var harness = new DocumentsHarness(_fixture);
        var number = $"NUM-{Guid.NewGuid():N}";

        var older = MakeAt(number, DocumentState.Superseded, DocumentTestData.DetectedAt);
        var newer = MakeAt(number, DocumentState.Issued, DocumentTestData.DetectedAt.AddHours(1));

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.UpsertDocumentAsync(older);
            await uow.UpsertDocumentAsync(newer);
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetByNumberAsync(number);
        dto!.Id.Should().Be(newer.Id);
        dto.State.Should().Be(nameof(DocumentState.Issued));
    }

    [Fact]
    public async Task GetByState_Is_Paginated_And_Filtered()
    {
        var harness = new DocumentsHarness(_fixture);
        var state = "ReadyToSend";
        var baseTime = DocumentTestData.DetectedAt.AddDays(10);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            for (var i = 0; i < 3; i++)
            {
                await uow.UpsertDocumentAsync(MakeAt($"PAGE-{Guid.NewGuid():N}", DocumentState.ReadyToSend, baseTime.AddMinutes(i)));
            }

            // Un document dans un autre état ne doit pas apparaître dans le filtre ReadyToSend.
            await uow.UpsertDocumentAsync(MakeAt($"OTHER-{Guid.NewGuid():N}", DocumentState.Blocked, baseTime.AddMinutes(5)));
            await uow.CommitAsync();
        }

        var firstPage = await harness.Queries.GetByStateAsync(state, page: 1, pageSize: 2);
        firstPage.Should().HaveCount(2);
        firstPage.Should().OnlyContain(d => d.State == state);
        firstPage[0].LastUpdateUtc.Should().BeOnOrAfter(firstPage[1].LastUpdateUtc, "tri par dernière mise à jour décroissante.");
    }

    private static Document MakeAt(string number, DocumentState state, DateTimeOffset lastUpdate)
    {
        return Document.Reconstitute(
            Guid.NewGuid(),
            "SRC",
            number,
            "FAC",
            new DateOnly(2026, 5, 14),
            "123456789",
            "Client SARL",
            customerIsCompanyHint: true,
            totalNet: 100.00m,
            totalTax: 20.00m,
            totalGross: 120.00m,
            state: state,
            payloadHash: "hash",
            paDocumentId: null,
            mappingVersion: null,
            firstSeenUtc: lastUpdate,
            lastUpdateUtc: lastUpdate);
    }
}
