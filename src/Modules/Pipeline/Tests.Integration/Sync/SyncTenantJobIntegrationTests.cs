namespace Liakont.Modules.Pipeline.Tests.Integration.Sync;

using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Pipeline.Tests.Integration.Check;
using Liakont.Modules.Pipeline.Tests.Integration.Send;
using Liakont.Modules.Transmission.Contracts;
using Liakont.PaClients.Fake;
using Xunit;

/// <summary>
/// SYNC (PIP01d) bout en bout sur une base tenant PostgreSQL réelle, avec archive WORM réelle et plug-in PA
/// factice : pour un document émis, la réconciliation ajoute la facture PA et le(s) tax report(s) en ADDENDA
/// chaînés au paquet WORM — UNIQUEMENT selon les CAPACITÉS DÉCLARÉES de la PA, et de façon IDEMPOTENTE. Tests
/// sériés (une base partagée) : chaque test utilise des identifiants/numéros uniques et son propre plug-in factice.
/// </summary>
public sealed class SyncTenantJobIntegrationTests : IClassFixture<PipelineSendHarness>
{
    private const string TaxReportId = "TR-SYNC-1";

    private readonly PipelineSendHarness _harness;

    public SyncTenantJobIntegrationTests(PipelineSendHarness harness) => _harness = harness;

    [Fact]
    public async Task Sync_With_Capabilities_Archives_Invoice_And_TaxReport_As_Addenda()
    {
        // PA générale (capacités de récupération déclarées) + un tax report rattaché au document avec son XML.
        var options = new FakePaClientOptions
        {
            IssuedTaxReportIds = new[] { TaxReportId },
            TaxReports = new[] { AvailableTaxReport() },
        };
        var (documentId, pivot) = await IssueDocumentAsync("caps", options);
        var paDocumentId = "FAKE-" + pivot.Number;

        (await _harness.ArchiveEntryCountAsync(documentId))
            .Should().Be(1, "le SEND a scellé le paquet WORM initial du document émis.");

        await _harness.RunSyncAsync();

        (await _harness.ArchiveEntryCountAsync(documentId))
            .Should().Be(3, "le SYNC ajoute la facture PA et le tax report du document en addenda chaînés.");
        _harness.PaCallCount(nameof(IPaClient.GetGeneratedDocumentAsync), paDocumentId)
            .Should().BeGreaterThan(0, "la facture générée est récupérée selon la capacité SupportsDocumentRetrieval.");
        _harness.PaCallCount(nameof(IPaClient.GetDocumentStatusAsync), paDocumentId)
            .Should().BeGreaterThan(0, "les tax_report_ids du document sont relus pour une attribution PAR DOCUMENT.");

        // Idempotence : re-jouer le SYNC ne duplique aucun addendum (adressage par empreinte de contenu).
        await _harness.RunSyncAsync();
        (await _harness.ArchiveEntryCountAsync(documentId))
            .Should().Be(3, "un SYNC ré-exécuté est idempotent — même facture / même XML = même entrée WORM.");
    }

    [Fact]
    public async Task Sync_Without_Retrieval_Capabilities_Archives_Nothing()
    {
        // PA sans aucune capacité de RÉCUPÉRATION (sujet du test) ; la facturation B2B reste déclarée pour qu'une
        // facture à destinataire identifié dispose d'un canal et soit ÉMISE (garde aiguillage B2B/B2G document-driven) —
        // sinon elle serait maintenue et le SYNC n'aurait rien à archiver.
        var options = new FakePaClientOptions { Capabilities = new PaCapabilities { PaName = "Fake", SupportsB2bInvoicing = true } };
        var (documentId, pivot) = await IssueDocumentAsync("nocaps", options);
        var paDocumentId = "FAKE-" + pivot.Number;

        await _harness.RunSyncAsync();

        (await _harness.ArchiveEntryCountAsync(documentId))
            .Should().Be(1, "sans capacité de récupération, le SYNC n'ajoute aucun addendum (paquet initial seul).");
        _harness.PaCallCount(nameof(IPaClient.GetGeneratedDocumentAsync), paDocumentId)
            .Should().Be(0, "aucune récupération n'est tentée quand la PA ne déclare pas la capacité.");
    }

    private static PaTaxReport AvailableTaxReport() => new()
    {
        Id = TaxReportId,
        Type = "reglement",
        State = PaTaxReportState.Registered,
        XmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<TaxReport id=\"TR-SYNC-1\"/>")),
    };

    private async Task<(Guid DocumentId, PivotDocumentDto Pivot)> IssueDocumentAsync(string tag, FakePaClientOptions options)
    {
        await _harness.UsePublishedFakeAsync(options);
        _harness.ForceWormAbsent = false;

        var documentId = Guid.NewGuid();
        var pivot = CheckIntegrationFixtures.BuildPivot("sync-" + tag + "-" + documentId.ToString("N"), "NORMAL");
        await _harness.SeedDetectedAndStageAsync(documentId, pivot);
        await _harness.MarkReadyToSendAsync(documentId);
        await _harness.RunSendAsync();
        (await _harness.GetDocumentStateAsync(documentId)).Should().Be("Issued");
        return (documentId, pivot);
    }
}
