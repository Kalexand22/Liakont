namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Xunit;

/// <summary>
/// B2C04 — la voie d'envoi est le PRODUCTEUR du lien reporting↔pièces (B2C03) : quand une déclaration
/// e-reporting B2C (flux 10.3) est ÉMISE, le lien immuable entre la transmission et sa pièce source (le
/// bordereau acheteur, identifié par la référence source du pivot) est GELÉ à l'émission (append-only,
/// tenant-scopé, CLAUDE.md n°4/n°9). CIBLÉ sur le marqueur 10.3 : une facture ORDINAIRE émise ne crée AUCUN
/// lien (pas de régression de la voie unique <c>SendDocumentAsync</c>). Base ISOLÉE par méthode.
/// </summary>
public sealed class ReportingPieceLinkProducerTests : IAsyncLifetime
{
    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Issued_B2cReportingDeclaration_Freezes_Link_To_Its_Source_Piece()
    {
        // PA publiée déclarant l'e-reporting B2C (défaut V1) : la déclaration 10.3 est routée, émise, et son
        // lien reporting↔pièces est gelé vers la pièce source (référence du bordereau acheteur).
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("Issued", "une déclaration 10.3 vers une PA capable est routée et émise.");

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("une déclaration 10.3 émise gèle exactement un lien vers sa pièce source.");
        links.Single().SourceReference.Should().Be(sourceReference, "le lien rattache la transmission à la référence de la pièce source (bordereau acheteur).");
        links.Single().DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task Issued_Declaration_Link_Is_Idempotent_Across_Reconciliation()
    {
        // APPEND-ONLY idempotent (CLAUDE.md n°4) : un nouveau cycle SEND (qui raccroche un Issued par anti-doublon)
        // ne ré-insère PAS le lien — un seul lien reste gelé après deux passes.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();
        await _harness.RunSendAsync();

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("le gel du lien est idempotent — un Issued ré-examiné ne ré-insère pas le lien.");
    }

    [Fact]
    public async Task Issued_Ordinary_Invoice_Does_Not_Freeze_Any_Link()
    {
        // NON-RÉGRESSION : le gel du lien est CIBLÉ sur le marqueur 10.3. Une facture ORDINAIRE émise ne crée
        // AUCUN lien reporting↔pièces.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var invoice = CheckIntegrationFixtures.BuildPivot("inv-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, invoice);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
        (await _harness.GetReportingPieceLinksAsync(documentId))
            .Should().BeEmpty("une facture ordinaire (sans marqueur 10.3) ne gèle aucun lien reporting↔pièces.");
    }
}
