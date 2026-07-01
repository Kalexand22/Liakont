namespace Liakont.Modules.Ged.Tests.Unit.Ingestion;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Ged.Serialization;
using Liakont.Modules.Ged.Infrastructure.Serialization;
using Xunit;

/// <summary>
/// Round-trip du lecteur canonique GED (GED05b) : <c>GedCanonicalJson.Serialize(GedCanonicalJsonReader.Read(json)) == json</c>
/// octet par octet — le consommateur d'ingestion reconstruit fidèlement le pivot GED stagé (miroir EXACT du writer
/// F19 §4.2). Sans ce round-trip, le mapping aval verrait un document déformé.
/// </summary>
public sealed class GedCanonicalJsonReaderTests
{
    [Fact]
    public void Read_then_serialize_is_byte_identical_for_a_rich_document()
    {
        var document = new IngestedDocumentDto(
            sourceReference: "PV-2026-42",
            documentType: "PV_VENTE",
            sourceTimestampUtc: new DateTime(2026, 7, 1, 10, 30, 0, DateTimeKind.Utc),
            content: new IngestedContentRef("blob/pv-42.pdf", "application/pdf", 12345, "abcd1234"),
            sourceFields: new Dictionary<string, string>
            {
                ["date_cloture"] = "2026-06-30",
                ["Réf facture"] = "F-77",
                ["seller_id"] = "S-9",
            },
            sourceAxes: new[] { new RawAxisHint("lots", new List<string> { "12", "13" }) },
            sourceEntities: new[] { new RawEntityHint("seller", "S-9", "Étude Dupont") },
            sourceRelations: new[] { new RawRelationHint("concerne", "V-5", "vente") });

        var json = GedCanonicalJson.Serialize(document);
        var roundTripped = GedCanonicalJson.Serialize(GedCanonicalJsonReader.Read(json));

        roundTripped.Should().Be(json);
    }

    [Fact]
    public void Read_then_serialize_is_byte_identical_for_a_minimal_document()
    {
        // Optionnels ABSENTS (horodatage, contenu) et collections vides : le writer les omet / émet un objet vide.
        var document = new IngestedDocumentDto(
            sourceReference: "DOC-1",
            documentType: "NOTE",
            sourceFields: new Dictionary<string, string>());

        var json = GedCanonicalJson.Serialize(document);
        var roundTripped = GedCanonicalJson.Serialize(GedCanonicalJsonReader.Read(json));

        roundTripped.Should().Be(json);
    }

    [Fact]
    public void Read_reconstructs_the_declared_fields_axes_entities_and_relations()
    {
        var document = new IngestedDocumentDto(
            sourceReference: "PV-2026-42",
            documentType: "PV_VENTE",
            sourceFields: new Dictionary<string, string> { ["k"] = "v" },
            sourceAxes: new[] { new RawAxisHint("lots", new List<string> { "12" }) },
            sourceEntities: new[] { new RawEntityHint("seller", "S-9", null) },
            sourceRelations: new[] { new RawRelationHint("concerne", "V-5", "vente") });

        var read = GedCanonicalJsonReader.Read(GedCanonicalJson.Serialize(document));

        read.SourceReference.Should().Be("PV-2026-42");
        read.DocumentType.Should().Be("PV_VENTE");
        read.SourceFields.Should().ContainKey("k").WhoseValue.Should().Be("v");
        read.SourceAxes.Should().ContainSingle().Which.Values.Should().Equal("12");
        read.SourceEntities.Should().ContainSingle().Which.ExternalId.Should().Be("S-9");
        read.SourceEntities[0].Display.Should().BeNull("le libellé optionnel absent reste null (symétrie pivot)");
        read.SourceRelations.Should().ContainSingle().Which.TargetType.Should().Be("vente");
    }
}
