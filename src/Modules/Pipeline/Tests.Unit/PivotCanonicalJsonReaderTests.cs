namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Pipeline.Infrastructure.Serialization;
using Xunit;

/// <summary>
/// Round-trip du lecteur canonique (INV-PIPELINE-001/002) : pour chaque golden de DOCUMENT UNIQUE de
/// <c>tests/fixtures/contrat-v1/</c>, <c>Serialize(Read(json)) == json</c> octet par octet. Les deux
/// enveloppes de transport (<c>batch-mixte.json</c>, <c>heartbeat.json</c>) ne sont PAS des
/// <c>PivotDocument</c> uniques : elles sont exclues.
/// </summary>
public sealed class PivotCanonicalJsonReaderTests
{
    private static readonly string[] NonDocumentFixtures = { "batch-mixte.json", "heartbeat.json" };

    public static IEnumerable<object[]> SingleDocumentGoldenFiles()
    {
        foreach (var path in Directory.EnumerateFiles(FixturesDirectory(), "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!NonDocumentFixtures.Contains(name, StringComparer.Ordinal))
            {
                yield return new object[] { name };
            }
        }
    }

    [Theory]
    [MemberData(nameof(SingleDocumentGoldenFiles))]
    public void Round_Trip_Is_Byte_For_Byte_Stable(string fixtureFileName)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory(), fixtureFileName));

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        CanonicalJson.Serialize(rebuilt).Should().Be(
            json,
            "désérialiser puis re-sérialiser le golden « {0} » doit être stable octet par octet (ADR-0007)",
            fixtureFileName);
    }

    [Fact]
    public void All_Eight_Single_Document_Goldens_Are_Covered()
    {
        // Garde anti-faux-vert : si un golden manque, le Theory paramétré ne couvrirait rien sans bruit.
        SingleDocumentGoldenFiles().Count().Should().BeGreaterThanOrEqualTo(
            8, "les 8 fixtures de document unique de tests/fixtures/contrat-v1/ doivent être couvertes");
    }

    [Fact]
    public void Decimal_Scale_Is_Preserved_On_Round_Trip()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDirectory(), "facture-standard-b2c.json"));

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        // 120.00m et 120m sont égaux en VALEUR : seule la re-sérialisation prouve la préservation d'échelle.
        CanonicalJson.Serialize(rebuilt).Should().Contain(
            "\"TotalNet\":120.00", "l'échelle décimale source (« 120.00 ») doit être préservée, jamais « 120 »");
    }

    [Fact]
    public void Payment_Due_Date_Survives_The_Round_Trip_For_The_Pa_Send()
    {
        // EXT01 — bout en bout : l'échéance (BT-9) écrite par l'agent dans le staging doit être RELUE par
        // le pipeline (ce lecteur) avant l'envoi à la PA. Sans cette lecture, le champ serait écrit puis
        // PERDU à la relecture → la facture non soldée resterait rejetée par BR-CO-25 malgré l'échéance.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-ECHEANCE",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-F-ECHEANCE",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: new[] { new PivotLineDto("Prestation", 100m, taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }) },
            paymentDueDate: new DateTime(2026, 2, 15));
        var json = CanonicalJson.Serialize(pivot);

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        rebuilt.PaymentDueDate.Should().Be(new DateTime(2026, 2, 15), "BT-9 doit traverser le staging intacte");
        CanonicalJson.Serialize(rebuilt).Should().Be(json, "round-trip stable octet par octet avec BT-9 (ADR-0007)");
    }

    [Fact]
    public void B2c_Reporting_Declaration_Marker_Survives_The_Round_Trip_For_The_Pa_Send()
    {
        // B2C01 — bout en bout : le marqueur de flux 10.3 écrit dans le staging doit être RELU par le pipeline
        // (ce lecteur) avant la garde de capacité au SEND. Sans cette lecture, le marqueur serait écrit puis
        // PERDU à la relecture → la déclaration 10.3 serait transmise même par une PA sans capacité B2C.
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "DECLARATION",
            number: "B2C-10.3-001",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-B2C-001",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            customer: new PivotPartyDto("Client Démo", siren: "987654321"),
            lines: new[] { new PivotLineDto("Adjudication", 100m, taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }) },
            isB2cReportingDeclaration: true);
        var json = CanonicalJson.Serialize(pivot);

        var rebuilt = PivotCanonicalJsonReader.Read(json);

        rebuilt.IsB2cReportingDeclaration.Should().BeTrue("le marqueur 10.3 doit traverser le staging intact");
        CanonicalJson.Serialize(rebuilt).Should().Be(json, "round-trip stable octet par octet avec le marqueur 10.3 (ADR-0007)");
    }

    [Fact]
    public void Document_Without_B2c_Marker_Reads_It_As_False()
    {
        // Miroir hash-neutre : un document ORDINAIRE (sans la clé IsB2cReportingDeclaration dans le JSON) est
        // relu avec le marqueur à false — l'absence de clé n'est jamais une erreur (champ additif optionnel).
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "FACTURE",
            number: "F-ORDINAIRE",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-ORD",
            supplier: new PivotPartyDto("SVV Démo", siren: "123456789"),
            totals: new PivotTotalsDto(100m, 20m, 120m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { new PivotLineDto("Prestation", 100m, taxes: new[] { new PivotLineTaxDto(20m, 20m, VatCategory.S) }) });
        var json = CanonicalJson.Serialize(pivot);

        json.Should().NotContain("IsB2cReportingDeclaration", "un document non-10.3 n'émet pas la clé du marqueur");
        PivotCanonicalJsonReader.Read(json).IsB2cReportingDeclaration.Should().BeFalse();
    }

    [Fact]
    public void Read_Null_Throws()
    {
        var act = () => PivotCanonicalJsonReader.Read(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static string FixturesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "contrat-v1");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Répertoire des golden « tests/fixtures/contrat-v1 » introuvable en remontant depuis " + AppContext.BaseDirectory);
    }
}
