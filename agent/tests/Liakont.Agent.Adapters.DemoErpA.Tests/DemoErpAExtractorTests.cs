namespace Liakont.Agent.Adapters.DemoErpA.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.DemoErpA;
using Liakont.Agent.Adapters.TestSupport;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Regroupement STREAMING de <see cref="DemoErpAExtractor.ExtractDocuments"/> (entête + lignes via le
/// LEFT JOIN, émission du dernier document, facture sans ligne) et QUARANTAINE d'un document malformé
/// (codex P2) : un document à date absente est ignoré (journalisé) sans figer la fenêtre, les autres
/// sont émis. Éprouvé avec un <see cref="IDataReader"/> factice partagé — aucun pilote ODBC requis.
/// </summary>
public class DemoErpAExtractorTests
{
    [Fact]
    public void Groups_rows_into_documents_emitting_first_last_and_lineless_invoices()
    {
        // Facture 1 (2 lignes) · facture 2 (1 ligne) · facture 3 (sans ligne, no_ligne NULL).
        var rows = new[]
        {
            Row("1", 1), Row("1", 2),
            Row("2", 1),
            Row("3", null),
        };
        var extractor = new DemoErpAExtractor(new FakeSourceConnectionFactory(rows), new CapturingAgentLog());

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).ToList();

        docs.Should().HaveCount(3);
        docs[0].Number.Should().Be("A-2026-0001");
        docs[0].Lines.Should().HaveCount(2);
        docs[1].Lines.Should().HaveCount(1);
        docs[2].Number.Should().Be("A-2026-0003");
        docs[2].Lines.Should().BeEmpty(); // facture sans ligne : émise quand même
    }

    [Fact]
    public void Quarantines_a_malformed_document_and_keeps_extracting_the_rest()
    {
        // Facture 2 : date absente → MapDocument lève SourceSchemaException → quarantaine (journalisée),
        // sans interrompre l'extraction des factures 1 et 3.
        var rows = new[]
        {
            Row("1", 1),
            Row("2", 1, dateEmission: null),
            Row("3", 1),
        };
        var log = new CapturingAgentLog();
        var extractor = new DemoErpAExtractor(new FakeSourceConnectionFactory(rows), log);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).ToList();

        docs.Select(d => d.Number).Should().Equal("A-2026-0001", "A-2026-0003");
        log.Warnings.Should().ContainSingle().Which.Should().Contain("quarantaine");
    }

    [Fact]
    public void Credit_note_with_resolved_origin_carries_the_reference()
    {
        var rows = new[] { Row("5", 1, typePiece: "AVO", origineNum: "A-2026-0001", origineDate: "2026-05-30") };
        var extractor = new DemoErpAExtractor(new FakeSourceConnectionFactory(rows), new CapturingAgentLog());

        PivotDocumentDto doc = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).Single();

        doc.SourceDocumentKind.Should().Be("AVO");
        doc.CreditNoteRefs.Should().ContainSingle().Which.Number.Should().Be("A-2026-0001");
    }

    private static Dictionary<string, object> Row(
        string factureId,
        int? noLigne,
        string? typePiece = "FAC",
        string? dateEmission = "2026-06-01",
        string? origineNum = null,
        string? origineDate = null)
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["facture_id"] = factureId,
            ["numero"] = "A-2026-" + factureId.PadLeft(4, '0'),
            ["type_piece"] = typePiece!,
            ["date_emission"] = (object?)dateEmission ?? DBNull.Value,
            ["facture_origine_numero"] = (object?)origineNum ?? DBNull.Value,
            ["total_ht"] = 100.00m,
            ["total_tva"] = 20.00m,
            ["total_ttc"] = 120.00m,
            ["devise"] = "EUR",
            ["client_nom"] = "Jean Dupont",
            ["client_siren"] = DBNull.Value,
            ["client_societe"] = false,
            ["client_cp"] = "35000",
            ["client_ville"] = "Rennes",
            ["client_pays"] = "FR",
            ["origine_date"] = (object?)origineDate ?? DBNull.Value,
            ["no_ligne"] = noLigne.HasValue ? (object)noLigne.Value : DBNull.Value,
            ["designation"] = "Ligne",
            ["quantite"] = 1m,
            ["prix_unitaire_ht"] = 100.00m,
            ["montant_ht"] = 100.00m,
            ["montant_tva"] = 20.00m,
            ["taux_tva"] = 20.0m,
            ["code_regime"] = "20",
        };
    }
}
