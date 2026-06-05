namespace Liakont.Agent.Adapters.EncheresV6.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Tests bout-en-bout du mode FIXTURES avec une source PDF « dossier de fichiers » configurée (ADP05) :
/// les fixtures incluent des PDF factices (mode LIÉ et mode POOL). Prouve que l'extracteur de fixtures
/// déclare les capacités PDF selon la config et délègue correctement la résolution des pièces jointes.
/// </summary>
public sealed class EncheresV6FixtureExtractorPdfTests : IDisposable
{
    private const string SalesJson = @"{
  ""regimes"": [ { ""code_regime"": ""5"", ""libelle"": ""Normal"" } ],
  ""bordereaux"": [
    {
      ""no_ba"": ""4500"",
      ""numero_piece"": ""F-2026-0500"",
      ""bordereau_ou_avoir"": ""B"",
      ""date_vente"": ""2026-01-12"",
      ""total_ht"": 100.0, ""total_tva"": 20.0, ""total_ttc"": 120.0,
      ""lignes"": [
        { ""type_ligne"": ""4"", ""designation"": ""Lot 1"", ""montant_ht"": 100.0, ""montant_tva"": 20.0, ""code_regime"": ""5"", ""no_ligne"": ""ligne#1"" }
      ]
    }
  ]
}";

    private static readonly DateTime PeriodFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodTo = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly RecordingAgentLog _log = new RecordingAgentLog();

    public EncheresV6FixtureExtractorPdfTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "encheresv6-fixture-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Fixture_extractor_without_pdf_source_declares_no_pdf_capability()
    {
        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromJson(SalesJson, Emitter(), OperationCategory.LivraisonBiens);

        extractor.Capabilities.ProvidesSourceDocuments.Should().BeFalse();
        extractor.Capabilities.ProvidesUnlinkedDocumentPool.Should().BeFalse();
        extractor.GetAttachments("no_ba=4500").Should().BeEmpty();
    }

    [Fact]
    public void Fixture_extractor_with_linked_pdf_source_finds_the_bordereau_pdf()
    {
        string linked = CreateFolder("linked");
        string pdf = WritePdf(linked, "bordereau-4500.pdf");
        var pdfSource = new FileSystemEncheresV6PdfSource(
            new EncheresV6PdfSourceOptions(linkedFolderPath: linked), _log);

        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromJson(SalesJson, Emitter(), OperationCategory.LivraisonBiens, pdfSource);

        extractor.Capabilities.ProvidesSourceDocuments.Should().BeTrue();

        // La référence vient du document réellement extrait (boucle de consommation de l'agent).
        PivotDocumentDto document = extractor.ExtractDocuments(PeriodFrom, PeriodTo).Single();
        IReadOnlyList<SourceAttachment> attachments = extractor.GetAttachments(document.SourceReference);

        attachments.Should().ContainSingle();
        attachments[0].FilePath.Should().Be(pdf);
        attachments[0].SourceReference.Should().Be("no_ba=4500");
    }

    [Fact]
    public void Fixture_extractor_with_pool_pdf_source_lists_the_pool()
    {
        string pool = CreateFolder("pool");
        WritePdfAt(pool, "scan-1.pdf", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        WritePdfAt(pool, "scan-2.pdf", new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        var pdfSource = new FileSystemEncheresV6PdfSource(
            new EncheresV6PdfSourceOptions(poolFolderPath: pool), _log);

        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromJson(SalesJson, Emitter(), OperationCategory.LivraisonBiens, pdfSource);

        extractor.Capabilities.ProvidesUnlinkedDocumentPool.Should().BeTrue();
        extractor.ListPoolDocuments(PeriodFrom, PeriodTo).Select(d => d.FileName)
            .Should().BeEquivalentTo("scan-1.pdf", "scan-2.pdf");
    }

    [Fact]
    public void Fixture_extractor_supports_linked_and_pool_modes_together()
    {
        string linked = CreateFolder("linked");
        WritePdf(linked, "4500.pdf");
        string pool = CreateFolder("pool");
        WritePdfAt(pool, "vrac-1.pdf", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        var pdfSource = new FileSystemEncheresV6PdfSource(
            new EncheresV6PdfSourceOptions(linkedFolderPath: linked, poolFolderPath: pool), _log);

        EncheresV6FixtureExtractor extractor =
            EncheresV6FixtureExtractor.FromJson(SalesJson, Emitter(), OperationCategory.LivraisonBiens, pdfSource);

        extractor.Capabilities.ProvidesSourceDocuments.Should().BeTrue();
        extractor.Capabilities.ProvidesUnlinkedDocumentPool.Should().BeTrue();
        extractor.GetAttachments("no_ba=4500").Should().ContainSingle();
        extractor.ListPoolDocuments(PeriodFrom, PeriodTo).Should().ContainSingle();
    }

    private static string WritePdf(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "%PDF-1.4 fictif " + fileName);
        return path;
    }

    private static void WritePdfAt(string folder, string fileName, DateTime lastWriteUtc)
    {
        File.SetLastWriteTimeUtc(WritePdf(folder, fileName), lastWriteUtc);
    }

    private static EncheresV6EmitterIdentity Emitter() =>
        new EncheresV6EmitterIdentity(name: "Étude Fictïve SVV", siren: "111111111", countryCode: "FR");

    private string CreateFolder(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
