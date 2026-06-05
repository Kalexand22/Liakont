namespace Liakont.Modules.Pipeline.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
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
