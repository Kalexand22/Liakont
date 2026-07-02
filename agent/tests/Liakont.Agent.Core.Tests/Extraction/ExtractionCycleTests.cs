namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Extraction;
using Liakont.Agent.Core.Storage;
using Xunit;

/// <summary>
/// Cycle d'extraction (F12 §2.2) : enfilage idempotent des documents, anti re-push (déjà acquitté),
/// collecte des PDF selon les capacités, restitution des régimes TVA source, et avancée du filigrane.
/// Tests RÉELS contre une base SQLite temporaire.
/// </summary>
public class ExtractionCycleTests
{
    private static readonly DateTime From = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mid = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Clock = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Run_enqueues_documents_of_the_period()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor("Fixture", documents: new[] { PivotTestData.Document("REF-1", Mid) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.DocumentsEnqueued.Should().Be(1);
            queue.Count().Should().Be(1);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 10).Should().ContainSingle()
                .Which.SourceReference.Should().Be("REF-1");
        }
    }

    [Fact]
    public void Run_is_idempotent_when_replayed_with_the_same_documents()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor("Fixture", documents: new[] { PivotTestData.Document("REF-1", Mid) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            cycle.Run(extractor, From, To);
            cycle.Run(extractor, From, To);

            queue.Count().Should().Be(1);
        }
    }

    [Fact]
    public void Run_skips_documents_already_acknowledged()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor("Fixture", documents: new[] { PivotTestData.Document("REF-1", Mid) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            cycle.Run(extractor, From, To);
            long id = queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 1)[0].Id;
            queue.Acknowledge(id);

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.DocumentsSkipped.Should().Be(1);
            result.DocumentsEnqueued.Should().Be(0);
            queue.Count().Should().Be(0);
        }
    }

    [Fact]
    public void Run_collects_linked_pdfs_when_the_capability_is_declared()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(providesSourceDocuments: true),
                documents: new[] { PivotTestData.Document("REF-1", Mid) },
                attachments: new[] { new SourceAttachment("REF-1", "C:\\pdf\\a.pdf") });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.LinkedPdfsEnqueued.Should().Be(1);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Pdf, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Run_collects_two_same_named_attachments_without_silent_drop()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            // Deux pièces jointes de MÊME nom (« scan.pdf ») mais de chemins distincts pour un même
            // document : le discriminant = chemin doit éviter la collision et la perte silencieuse.
            var extractor = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(providesSourceDocuments: true),
                documents: new[] { PivotTestData.Document("REF-1", Mid) },
                attachments: new[]
                {
                    new SourceAttachment("REF-1", "C:\\pdf\\a\\scan.pdf"),
                    new SourceAttachment("REF-1", "C:\\pdf\\b\\scan.pdf"),
                });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.LinkedPdfsEnqueued.Should().Be(2);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Pdf, 10).Should().HaveCount(2);
        }
    }

    [Fact]
    public void Run_collects_a_late_pdf_for_an_already_acknowledged_document()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            // Cycle 1 : le document part SANS PDF (pas encore déposé dans la source) et est acquitté.
            var withoutPdf = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(providesSourceDocuments: true),
                documents: new[] { PivotTestData.Document("REF-1", Mid) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());
            cycle.Run(withoutPdf, From, To);
            queue.Acknowledge(queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 1)[0].Id);

            // Cycle 2 : le PDF est arrivé. Le document (déjà acquitté) est sauté, mais son PDF est
            // quand même collecté — sinon il serait DÉFINITIVEMENT perdu (le skip anti re-push du
            // document courait avant la collecte). L'idempotence PDF reste garantie par sa propre clé.
            var withPdf = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(providesSourceDocuments: true),
                documents: new[] { PivotTestData.Document("REF-1", Mid) },
                attachments: new[] { new SourceAttachment("REF-1", "C:\\pdf\\tardif.pdf") });

            ExtractionResult result = cycle.Run(withPdf, From, To);

            result.DocumentsSkipped.Should().Be(1, "le document acquitté n'est pas ré-enfilé");
            result.LinkedPdfsEnqueued.Should().Be(1, "le PDF tardif est rattrapé");
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Pdf, 10).Should().ContainSingle();

            // Cycle 3 : rien de neuf — le PDF déjà enfilé n'est pas dupliqué.
            cycle.Run(withPdf, From, To).LinkedPdfsEnqueued.Should().Be(0);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Pdf, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Run_collects_pool_pdfs_when_the_capability_is_declared()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(providesUnlinkedDocumentPool: true),
                poolDocuments: new[] { new PoolDocument("vrac-1", "C:\\pool\\1.pdf") });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.PoolPdfsEnqueued.Should().Be(1);
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.PdfPool, 10).Should().ContainSingle();
        }
    }

    [Fact]
    public void Run_quarantines_a_non_serializable_document_without_aborting_the_cycle()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            // Un document NON conforme (OperationCategory hors plage → CanonicalJson.Serialize lève,
            // garde WriteEnum RDL01) ne doit PAS avorter le cycle : le document valide suivant est enfilé
            // et le filigrane avance (sinon un seul document fautif bloquerait l'extraction du tenant en boucle).
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[]
                {
                    PivotTestData.DocumentWithUndefinedOperationCategory("REF-BAD", Mid),
                    PivotTestData.Document("REF-OK", Mid),
                });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.DocumentsQuarantined.Should().Be(1, "le document non sérialisable est mis en quarantaine, jamais transmis");
            result.DocumentsSkipped.Should().Be(0, "la quarantaine (conformité) est comptée à part des skips anti-re-push");
            result.DocumentsEnqueued.Should().Be(1, "le document valide de la fenêtre est quand même enfilé");
            queue.Peek(QueueItemStatus.Pending, QueueItemKind.Document, 10).Should().ContainSingle()
                .Which.SourceReference.Should().Be("REF-OK");
            queue.GetExtractionWatermarkUtc().Should().Be(To, "un document fautif ne bloque pas l'avancée du filigrane");
        }
    }

    [Fact]
    public void Run_stashes_source_tax_regimes_and_advances_the_watermark()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new FixtureExtractor(
                "Fixture",
                documents: new[] { PivotTestData.Document("REF-1", Mid) },
                sourceTaxRegimes: new[] { new SourceTaxRegimeDto("0", "Normal", 3) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.SourceTaxRegimesCollected.Should().Be(1);
            queue.GetState(LocalQueue.SourceTaxRegimesKey).Should().Contain("\"0\"");
            queue.GetExtractionWatermarkUtc().Should().Be(To);
        }
    }

    [Fact]
    public void Run_stashes_extractor_capabilities_for_the_next_push()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            // Capacités déclarées par la source (ADR-0004 D2 / RD401) : le cycle les range dans l'état de
            // la file, le drainage les joindra au prochain lot. Les formes énumérées voyagent en brut.
            var extractor = new FixtureExtractor(
                "Fixture",
                capabilities: new ExtractorCapabilities(exposesPayments: true, isMutableAfterIssue: true, regimeKeyShape: RegimeKeyShape.Composite),
                documents: new[] { PivotTestData.Document("REF-1", Mid) });
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            cycle.Run(extractor, From, To);

            string? stashed = queue.GetState(LocalQueue.ExtractorCapabilitiesKey);
            stashed.Should().NotBeNullOrEmpty();
            var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<ExtractorCapabilitiesDto>(stashed!);
            dto!.ExposesPayments.Should().BeTrue();
            dto.IsMutableAfterIssue.Should().BeTrue();
            dto.RegimeKeyShape.Should().Be("Composite");
        }
    }

    [Fact]
    public void Run_advances_watermark_when_regime_listing_is_momentarily_unavailable()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new RegimeFailingExtractor(
                new[] { PivotTestData.Document("REF-1", Mid) },
                () => new SourceUnavailableException("source momentanément indisponible (test)."));
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            ExtractionResult result = cycle.Run(extractor, From, To);

            result.DocumentsEnqueued.Should().Be(1);
            result.SourceTaxRegimesCollected.Should().Be(0, "le rafraîchissement des régimes est best-effort");
            queue.GetExtractionWatermarkUtc().Should().Be(To, "un échec PASSAGER de listage des régimes ne bloque pas le filigrane");
        }
    }

    [Fact]
    public void Run_propagates_and_holds_watermark_when_regime_listing_is_fatally_broken()
    {
        using (var db = new TempDatabase())
        using (var queue = new LocalQueue(db.Path, new MutableClock(Clock)))
        {
            var extractor = new RegimeFailingExtractor(
                new[] { PivotTestData.Document("REF-1", Mid) },
                () => new SourceSchemaException("schéma des régimes incompatible (test)."));
            var cycle = new ExtractionCycle(queue, new NullAgentLog());

            Action act = () => cycle.Run(extractor, From, To);

            act.Should().Throw<SourceSchemaException>("une erreur de SCHÉMA des régimes est FATALE, jamais avalée par le best-effort");
            queue.GetExtractionWatermarkUtc().Should().BeNull("un échec fatal du listage des régimes bloque l'avancée du filigrane (intervention requise)");
        }
    }

    // Extracteur qui réussit l'extraction des documents mais dont le listage des régimes échoue avec
    // l'exception fournie — éprouve les DEUX branches du stash best-effort (passagère vs fatale).
    private sealed class RegimeFailingExtractor : IExtractor
    {
        private readonly IReadOnlyList<PivotDocumentDto> _documents;
        private readonly Func<Exception> _onListRegimes;

        public RegimeFailingExtractor(IReadOnlyList<PivotDocumentDto> documents, Func<Exception> onListRegimes)
        {
            _documents = documents;
            _onListRegimes = onListRegimes;
        }

        public string SourceName => "RegimeFailing";

        public ExtractorCapabilities Capabilities { get; } = new ExtractorCapabilities();

        public ExtractorInfo GetInfo() => new ExtractorInfo("RegimeFailing", "1.0.0", "Test");

        public HealthCheckResult CheckHealth() => HealthCheckResult.Healthy("test");

        public IEnumerable<PivotDocumentDto> ExtractDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) => _documents;

        public IEnumerable<PivotPaymentDto> ExtractPayments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
            Array.Empty<PivotPaymentDto>();

        public IReadOnlyList<SourceTaxRegimeDto> ListSourceTaxRegimes() => throw _onListRegimes();

        public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) => Array.Empty<SourceAttachment>();

        public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
            Array.Empty<PoolDocument>();
    }
}
