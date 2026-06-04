namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Documents.Tests.Integration.Doubles;
using Liakont.Modules.Ingestion.Contracts;
using Xunit;

/// <summary>
/// Câblage du port d'ingestion (PIV04) : un push agent (appel de <see cref="IDocumentIntake"/>) crée un
/// document en état Detected dans la base du tenant, avec son événement d'audit de genèse, de façon
/// idempotente sur l'identifiant (INV-DOCUMENTS-003/008).
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentIntakeIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentIntakeIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Agent_Push_Creates_Detected_Document_With_Genesis_Event()
    {
        var harness = new DocumentsHarness(_fixture);
        var intake = new DocumentIntake(new SingleDatabaseTenantConnectionFactory(_fixture.ConnectionString));
        var input = BuildIntake();

        await intake.RegisterDetectedDocumentAsync(input);

        var dto = await harness.Queries.GetByIdAsync(input.DocumentId);
        dto.Should().NotBeNull();
        dto!.State.Should().Be(nameof(DocumentState.Detected));
        dto.DocumentNumber.Should().Be("F-2026-777");
        dto.SupplierSiren.Should().Be("123456789");
        dto.TotalGross.Should().Be(1162.80m);

        var events = await harness.Queries.GetEventsAsync(input.DocumentId);
        events.Should().ContainSingle();
        events[0].EventType.Should().Be(nameof(DocumentEventType.DocumentDetected));
    }

    [Fact]
    public async Task Intake_Is_Idempotent_On_DocumentId()
    {
        var harness = new DocumentsHarness(_fixture);
        var intake = new DocumentIntake(new SingleDatabaseTenantConnectionFactory(_fixture.ConnectionString));
        var input = BuildIntake();

        await intake.RegisterDetectedDocumentAsync(input);
        await intake.RegisterDetectedDocumentAsync(input);

        var events = await harness.Queries.GetEventsAsync(input.DocumentId);
        events.Should().ContainSingle("un rejeu de l'ingestion ne duplique ni le document ni son audit.");
    }

    private static DetectedDocumentIntake BuildIntake()
    {
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "FAC",
            number: "F-2026-777",
            issueDate: new DateTime(2026, 5, 14),
            sourceReference: $"SRC-{Guid.NewGuid():N}",
            supplier: new PivotPartyDto("Ma SVV", siren: "123456789"),
            totals: new PivotTotalsDto(1000.00m, 162.80m, 1162.80m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client SARL", isCompanyHint: true));

        return new DetectedDocumentIntake
        {
            DocumentId = Guid.NewGuid(),
            TenantId = "acme",
            SourceReference = pivot.SourceReference,
            PayloadHash = $"hash-{Guid.NewGuid():N}",
            Document = pivot,
            ReceivedAtUtc = DocumentTestData.DetectedAt,
        };
    }
}
