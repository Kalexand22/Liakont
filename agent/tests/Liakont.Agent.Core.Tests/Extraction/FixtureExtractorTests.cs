namespace Liakont.Agent.Core.Tests.Extraction;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Extracteur de fixtures générique (F01-F02 §4.4) : filtrage par période, capacités, pièces jointes
/// et pool conditionnés aux capacités, et chargement depuis JSON.
/// </summary>
public class FixtureExtractorTests
{
    private static readonly DateTime March = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime April = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Extract_documents_filters_on_the_issue_date_period()
    {
        var extractor = new FixtureExtractor(
            "Fixture",
            documents: new[]
            {
                PivotTestData.Document("IN", new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)),
                PivotTestData.Document("BEFORE", new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)),
                PivotTestData.Document("AFTER", new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc)),
            });

        List<PivotDocumentDto> result = extractor.ExtractDocuments(March, April).ToList();

        result.Select(d => d.SourceReference).Should().Equal("IN");
    }

    [Fact]
    public void Extract_payments_filters_on_the_payment_date_period()
    {
        var extractor = new FixtureExtractor(
            "Fixture",
            payments: new[]
            {
                PivotTestData.Payment(new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), 50m, "P-IN"),
                PivotTestData.Payment(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), 50m, "P-AFTER"),
            });

        List<PivotPaymentDto> result = extractor.ExtractPayments(March, April).ToList();

        result.Select(p => p.SourceReference).Should().Equal("P-IN");
    }

    [Fact]
    public void Attachments_are_returned_only_when_the_capability_is_declared()
    {
        var withCapability = new FixtureExtractor(
            "Fixture",
            capabilities: new ExtractorCapabilities(providesSourceDocuments: true),
            attachments: new[] { new SourceAttachment("REF-1", "C:\\pdf\\a.pdf") });
        var withoutCapability = new FixtureExtractor(
            "Fixture",
            attachments: new[] { new SourceAttachment("REF-1", "C:\\pdf\\a.pdf") });

        withCapability.GetAttachments("REF-1").Should().ContainSingle();
        withoutCapability.GetAttachments("REF-1").Should().BeEmpty();
    }

    [Fact]
    public void Pool_documents_are_returned_only_when_the_capability_is_declared()
    {
        var withCapability = new FixtureExtractor(
            "Fixture",
            capabilities: new ExtractorCapabilities(providesUnlinkedDocumentPool: true),
            poolDocuments: new[] { new PoolDocument("vrac-1", "C:\\pool\\1.pdf") });
        var withoutCapability = new FixtureExtractor(
            "Fixture",
            poolDocuments: new[] { new PoolDocument("vrac-1", "C:\\pool\\1.pdf") });

        withCapability.ListPoolDocuments(March, April).Should().ContainSingle();
        withoutCapability.ListPoolDocuments(March, April).Should().BeEmpty();
    }

    [Fact]
    public void From_json_rebuilds_the_source_with_documents_capabilities_and_regimes()
    {
        const string Json = @"{
  ""sourceName"": ""FixtureDemo"",
  ""capabilities"": { ""providesSourceDocuments"": true, ""regimeKeyShape"": ""Composite"" },
  ""documents"": [
    {
      ""sourceDocumentKind"": ""FAC"",
      ""number"": ""FAC-1"",
      ""issueDate"": ""2026-03-15T00:00:00Z"",
      ""sourceReference"": ""REF-1"",
      ""supplier"": { ""name"": ""Vendeur fictif"" },
      ""totals"": { ""totalNet"": 100, ""totalTax"": 20, ""totalGross"": 120 },
      ""operationCategory"": ""LivraisonBiens""
    }
  ],
  ""sourceTaxRegimes"": [ { ""code"": ""0"", ""label"": ""Normal"", ""occurrences"": 3 } ]
}";

        FixtureExtractor extractor = FixtureExtractor.FromJson(Json);

        extractor.SourceName.Should().Be("FixtureDemo");
        extractor.Capabilities.ProvidesSourceDocuments.Should().BeTrue();
        extractor.Capabilities.RegimeKeyShape.Should().Be(RegimeKeyShape.Composite);
        extractor.ExtractDocuments(March, April).Should().ContainSingle().Which.SourceReference.Should().Be("REF-1");
        extractor.ListSourceTaxRegimes().Should().ContainSingle().Which.Code.Should().Be("0");
    }

    [Fact]
    public void From_json_rejects_invalid_json_with_a_schema_exception()
    {
        Action act = () => FixtureExtractor.FromJson("{ pas du json");

        act.Should().Throw<SourceSchemaException>();
    }
}
