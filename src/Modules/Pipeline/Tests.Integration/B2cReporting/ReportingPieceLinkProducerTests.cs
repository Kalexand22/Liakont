namespace Liakont.Modules.Pipeline.Tests.Integration.B2cReporting;

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Xunit;

/// <summary>
/// B2C04 — le JOB d'e-reporting B2C est le PRODUCTEUR du lien reporting↔pièces (B2C03) : quand une déclaration
/// e-reporting B2C (flux 10.3) est ÉMISE par son job (la voie document la DIFFÈRE, garde D1), le lien immuable
/// entre la transmission et sa pièce source (la référence source du pivot) est GELÉ à l'émission (append-only,
/// tenant-scopé, CLAUDE.md n°4/n°9) — clé document conservée (D2), traçabilité doc↔reporting préservée pour TOUS
/// les flux B2C (marge ou pas, cf. [[b2c-egale-ereporting-partout]]). À l'inverse, une facture B2B émise par la
/// voie document (e-invoicing) ne crée AUCUN lien d'e-reporting. Base ISOLÉE par méthode.
/// </summary>
public sealed class ReportingPieceLinkProducerTests : IAsyncLifetime
{
    private const string SendB2cTransaction = "SendB2cTransactionAsync";

    private readonly PipelineSendHarness _harness = new();

    public Task InitializeAsync() => _harness.InitializeAsync();

    public Task DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Emitted_B2cReportingDeclaration_Freezes_Link_To_Its_Source_Piece()
    {
        // PA publiée déclarant l'e-reporting B2C (défaut V1). La déclaration ordinaire B2C est DIFFÉRÉE de la voie
        // document (garde D1) et e-reportée par son job ; à l'émission, son lien reporting↔pièces est gelé vers la
        // pièce source (référence source du document).
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();

        _harness.PaCallCount(SendB2cTransaction, "Tlb1/Seller/20260120").Should()
            .Be(1, "la déclaration B2C ordinaire est e-reportée par son job (jamais par la voie document).");
        (await _harness.GetDocumentStateAsync(documentId))
            .Should().Be("ReadyToSend", "le job d'e-reporting ne transitionne pas la machine à états du document.");

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("une déclaration 10.3 émise gèle exactement un lien vers sa pièce source.");
        links.Single().SourceReference.Should().Be(sourceReference, "le lien rattache la transmission à la référence de la pièce source.");
        links.Single().DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task Emitted_Declaration_Link_Is_Idempotent_Across_Reruns()
    {
        // APPEND-ONLY idempotent (CLAUDE.md n°4) : un 2e run du job (qui exclut le document déjà tenté, attempt-once
        // D3) ne ré-insère PAS le lien — un seul lien reste gelé après deux passes.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var sourceReference = "ba-" + documentId.ToString("N");
        var declaration = CheckIntegrationFixtures.BuildB2cReportingDeclaration(sourceReference, "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, declaration);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunB2cPlainAsync();
        await _harness.RunB2cPlainAsync();

        var links = await _harness.GetReportingPieceLinksAsync(documentId);
        links.Should().ContainSingle("le gel du lien est idempotent — un document déjà émis n'est pas ré-émis (jamais 2 liens).");
    }

    [Fact]
    public async Task Issued_B2b_Invoice_Does_Not_Freeze_Any_Reporting_Link()
    {
        // NON-RÉGRESSION : le gel du lien est PROPRE à l'e-reporting B2C. Une facture B2B (acheteur à SIREN) émise
        // par la voie document (e-invoicing) ne crée AUCUN lien reporting↔pièces.
        await _harness.UsePublishedFakeAsync();
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var invoice = CheckIntegrationFixtures.BuildPivot("inv-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, invoice);
        await _harness.MarkReadyToSendAsync(documentId);

        await _harness.RunSendAsync();

        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
        (await _harness.GetReportingPieceLinksAsync(documentId))
            .Should().BeEmpty("une facture B2B (e-invoicing) ne gèle aucun lien reporting↔pièces B2C.");
    }
}
