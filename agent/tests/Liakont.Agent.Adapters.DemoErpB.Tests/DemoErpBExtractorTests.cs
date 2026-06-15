namespace Liakont.Agent.Adapters.DemoErpB.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Adapters.DemoErpB;
using Liakont.Agent.Adapters.TestSupport;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Regroupement STREAMING de <see cref="DemoErpBExtractor.ExtractDocuments"/> (entête + lignes float via
/// le LEFT JOIN, émission du dernier document, facture sans ligne) et QUARANTAINE d'un document malformé
/// (codex P2). Éprouvé avec le même <see cref="IDataReader"/> factice partagé que DemoErpA.
/// </summary>
public class DemoErpBExtractorTests
{
    private static readonly SourceEmitterConfig Emitter =
        new SourceEmitterConfig("123456782", "Société Fictive de Démonstration", OperationCategory.Mixte);

    [Fact]
    public void Groups_rows_into_documents_emitting_first_last_and_lineless_invoices()
    {
        var rows = new[]
        {
            Row("1", 1), Row("1", 2),
            Row("2", 1),
            Row("3", null),
        };
        var extractor = new DemoErpBExtractor(new FakeSourceConnectionFactory(rows), Emitter, new CapturingAgentLog());

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).ToList();

        docs.Should().HaveCount(3);
        docs[0].Number.Should().Be("B-2026-0001");
        docs[0].Lines.Should().HaveCount(2);
        docs[1].Lines.Should().HaveCount(1);
        docs[2].Number.Should().Be("B-2026-0003");
        docs[2].Lines.Should().BeEmpty();
    }

    [Fact]
    public void Quarantines_a_malformed_document_and_keeps_extracting_the_rest()
    {
        var rows = new[]
        {
            Row("1", 1),
            Row("2", 1, issuedOn: null),
            Row("3", 1),
        };
        var log = new CapturingAgentLog();
        var extractor = new DemoErpBExtractor(new FakeSourceConnectionFactory(rows), Emitter, log);

        List<PivotDocumentDto> docs = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).ToList();

        docs.Select(d => d.Number).Should().Equal("B-2026-0001", "B-2026-0003");
        log.Warnings.Should().ContainSingle().Which.Should().Contain("quarantaine");
    }

    [Fact]
    public void Credit_note_with_resolved_origin_carries_the_reference()
    {
        var rows = new[] { Row("5", 1, kind: "C", originNo: "B-2026-0001", originIssuedOn: "2026-05-30") };
        var extractor = new DemoErpBExtractor(new FakeSourceConnectionFactory(rows), Emitter, new CapturingAgentLog());

        PivotDocumentDto doc = extractor.ExtractDocuments(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow).Single();

        doc.SourceDocumentKind.Should().Be("C");
        doc.CreditNoteRefs.Should().ContainSingle().Which.Number.Should().Be("B-2026-0001");
    }

    private static Dictionary<string, object> Row(
        string invoiceId,
        int? lineNumber,
        string? kind = "I",
        string? issuedOn = "2026-06-01",
        string? originNo = null,
        string? originIssuedOn = null)
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["InvoiceId"] = invoiceId,
            ["InvoiceNo"] = "B-2026-" + invoiceId.PadLeft(4, '0'),
            ["Kind"] = kind!,
            ["IssuedOn"] = (object?)issuedOn ?? DBNull.Value,
            ["OriginInvoiceNo"] = (object?)originNo ?? DBNull.Value,
            ["BuyerName"] = "Customer One",
            ["BuyerSiren"] = DBNull.Value,
            ["BuyerIsCompany"] = false,
            ["NetTotal"] = 100.0,
            ["VatTotal"] = 20.0,
            ["GrossTotal"] = 120.0,
            ["Currency"] = "EUR",
            ["OriginIssuedOn"] = (object?)originIssuedOn ?? DBNull.Value,
            ["LineNumber"] = lineNumber.HasValue ? (object)lineNumber.Value : DBNull.Value,
            ["Label"] = "Item",
            ["Qty"] = 1.0,
            ["UnitPrice"] = 100.0,
            ["NetAmount"] = 100.0,
            ["VatAmount"] = 20.0,
            ["VatRate"] = 20.0,
            ["VatRegime"] = "20",
        };
    }
}
