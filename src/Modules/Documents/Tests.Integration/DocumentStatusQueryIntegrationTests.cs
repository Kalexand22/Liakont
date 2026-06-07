namespace Liakont.Modules.Documents.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Documents.Domain.Entities;
using Xunit;

/// <summary>
/// <c>FindStatusBySourceReferenceAndPayloadHashAsync</c> (PIP01a) sur PostgreSQL réel (Testcontainers) :
/// retourne l'état durable du document pour la clé (référence source, empreinte), ou <c>null</c> si
/// inconnue. Clés uniques par test → robuste à la fixture partagée.
/// </summary>
[Collection("DocumentsIntegration")]
public sealed class DocumentStatusQueryIntegrationTests
{
    private readonly Fixtures.DocumentsDatabaseFixture _fixture;

    public DocumentStatusQueryIntegrationTests(Fixtures.DocumentsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindStatus_Returns_The_Document_State_For_The_Key()
    {
        var harness = new DocumentsHarness(_fixture);
        var document = DocumentTestData.NewDetected(
            documentNumber: $"PIP01A-{Guid.NewGuid():N}",
            sourceReference: $"SRC-{Guid.NewGuid():N}",
            payloadHash: $"hash-{Guid.NewGuid():N}");

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            await uow.CreateDetectedAsync(document, DocumentEvent.Detected(document.Id, DocumentTestData.DetectedAt));
            await uow.CommitAsync();
        }

        var status = await harness.Queries.FindStatusBySourceReferenceAndPayloadHashAsync(
            document.SourceReference, document.PayloadHash);

        status.Should().NotBeNull();
        status!.Id.Should().Be(document.Id);
        status.DocumentNumber.Should().Be(document.DocumentNumber);
        status.State.Should().Be(nameof(DocumentState.Detected));
    }

    [Fact]
    public async Task FindStatus_Returns_Null_For_An_Unknown_Key()
    {
        var harness = new DocumentsHarness(_fixture);

        var status = await harness.Queries.FindStatusBySourceReferenceAndPayloadHashAsync(
            "absent-" + Guid.NewGuid(), "absent-" + Guid.NewGuid());

        status.Should().BeNull();
    }
}
