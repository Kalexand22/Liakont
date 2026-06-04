namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

/// <summary>
/// Tests de contrat CROSS-RUNTIME sur le JEU COMPLET de fixtures (PIV03). Ce fichier est LIÉ dans les
/// deux projets de test — plateforme (.NET 10) ET agent (net48) — et exécuté des deux côtés. Pour
/// chaque golden file de <c>tests/fixtures/contrat-v1/</c> :
/// <list type="bullet">
/// <item>la sérialisation canonique du builder reproduit le fichier OCTET POUR OCTET (preuve que
/// net48 et .NET 10 produisent la même sortie) ;</item>
/// <item>l'empreinte SHA-256 est FIGÉE (<see cref="FrozenHashes"/>) : un changement = rupture de
/// contrat détectée (description PIV03) ;</item>
/// <item>le round-trip désérialiser→re-sérialiser est sans perte.</item>
/// </list>
/// Les empreintes figées sont identiques des deux côtés : c'est l'ancre d'identité cross-runtime
/// (F12 §3.4, ADR-0007). Les enveloppes (batch, heartbeat) ne sont pas hashées (frontière documentée
/// dans <see cref="ContractFixtures"/>) ; on prouve seulement leur identité de format cross-runtime.
/// </summary>
public sealed class ContractFixtureTests
{
    /// <summary>
    /// Empreintes SHA-256 FIGÉES (hex minuscule) du JSON canonique de chaque document de référence.
    /// Anti-doublon (PIV04) et détection d'altération (TRK03). Toute évolution VOLONTAIRE du contrat
    /// se régénère via <c>LIAKONT_REGEN_FIXTURES=1</c> + <c>LIAKONT_FIXTURE_OUT=&lt;dossier&gt;</c>,
    /// puis report des nouvelles empreintes ici — et passage en revue humaine (gate de segment).
    /// </summary>
    private static readonly Dictionary<string, string> FrozenHashes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["facture-standard-b2c"] = "093d03f4b3cd21025ae646da060b7383fa11db1dab375eb4acc70c920b6b59bd",
        ["vente-sur-marge-exoneree"] = "19e8b669e901918f342b1b54bdc3c4967707404a94e027454c0d8e10110b0324",
        ["avoir-simple-lie"] = "dd77f842b8dd604b0b9b9855ebce87835fa37ead30eea6fcc0f57e40a2e851c5",
        ["avoir-partiel"] = "a662f064a9dfadf1769a6935d7d24d25a03fbbb914e8a73107040d975ca88984",
        ["avoir-groupe-multi-refs"] = "04eef3e2e4f470f094b8f71f19da360c4481661b2415eb534c8eee2ceb3b7d39",
        ["facture-b2b-pro"] = "9ef37f38c06112225dfedebf855136af9fe5eefc5b30608d43310dd1b631bf8d",
        ["facture-prestation-paiements"] = "b30d6a942afe250efb26c48711c85bc91aa6de949148e52e00d07c1d6331be81",
    };

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "contrat-v1");

    [Theory]
    [MemberData(nameof(ContractFixtures.DocumentCases), MemberType = typeof(ContractFixtures))]
    public void Document_fixture_is_canonical_byte_identical_and_hash_frozen(string name)
    {
        PivotDocumentDto document = ContractFixtures.GetDocument(name);
        string golden = ReadFixture(name + ".json");

        // 1. Identité cross-runtime : le builder reproduit le golden octet pour octet, de chaque côté.
        CanonicalJson.Serialize(document).Should().Be(
            golden,
            "le golden file est l'unique sortie canonique attendue sur net48 ET .NET 10 (F12 §3.4)");

        // 2. Empreinte figée : ancre anti-doublon, identique des deux côtés.
        string expectedHash = FrozenHashes[name];
        PayloadHasher.ComputeHash(document).Should().Be(
            expectedHash,
            "l'empreinte du document est figée — un changement est une rupture de contrat (PIV03)");

        // 3. Le fichier golden LUI-MÊME est l'artefact canonique hashé (ASCII pur ⇒ octets = UTF-8).
        PayloadHasher.ComputeHash(golden).Should().Be(expectedHash);

        // 4. Round-trip sans perte.
        PivotDocumentDto rebuilt = PivotCanonicalReader.ReadDocument(golden);
        CanonicalJson.Serialize(rebuilt).Should().Be(golden, "désérialiser puis re-sérialiser est stable");
        PayloadHasher.ComputeHash(rebuilt).Should().Be(expectedHash);
    }

    [Theory]
    [MemberData(nameof(ContractFixtures.DocumentCases), MemberType = typeof(ContractFixtures))]
    public void Document_fixture_json_is_pure_ascii(string name)
    {
        string golden = ReadFixture(name + ".json");

        foreach (char c in golden)
        {
            (c >= ' ' && c <= '~').Should().BeTrue(
                "la sortie canonique est ASCII pur : tout caractère non-ASCII est échappé \\uXXXX (ADR-0007)");
        }
    }

    [Fact]
    public void Frozen_hashes_are_64_lowercase_hex_and_unique()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in FrozenHashes)
        {
            entry.Value.Should().MatchRegex("^[0-9a-f]{64}$", "empreinte SHA-256 hex minuscule (64 car.) pour " + entry.Key);
            seen.Add(entry.Value).Should().BeTrue("deux fixtures distinctes ne doivent pas partager une empreinte (" + entry.Key + ")");
        }
    }

    [Fact]
    public void Batch_fixture_carries_canonical_documents()
    {
        string golden = ReadFixture(ContractFixtures.BatchFixtureName + ".json");

        // Identité cross-runtime de l'enveloppe illustrative.
        ContractFixtures.ComposeBatchRequestJson().Should().Be(golden);

        IDictionary<string, object?> map = PivotCanonicalReader.ParseToMap(golden);
        map["ContractVersion"].Should().Be(AgentContractVersion.ContractVersion);
        var documents = (List<object?>)map["Documents"]!;
        documents.Should().HaveCount(2, "le lot illustratif porte deux documents");

        // Les documents embarqués sont EXACTEMENT les golden canoniques correspondants : le lot
        // transporte des payloads dont l'empreinte par document reste celle figée plus haut.
        golden.Should().Contain(CanonicalJson.Serialize(ContractFixtures.GetDocument("facture-standard-b2c")));
        golden.Should().Contain(CanonicalJson.Serialize(ContractFixtures.GetDocument("avoir-simple-lie")));
    }

    [Fact]
    public void Heartbeat_fixture_matches_wire_shape()
    {
        string golden = ReadFixture(ContractFixtures.HeartbeatFixtureName + ".json");

        ContractFixtures.ComposeHeartbeatJson().Should().Be(golden);

        IDictionary<string, object?> map = PivotCanonicalReader.ParseToMap(golden);
        map["ContractVersion"].Should().Be(AgentContractVersion.ContractVersion);
        map["AgentVersion"].Should().Be("2.4.0");
        ((string)map["SentAtUtc"]!).Should().MatchRegex(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", "horodatage UTC au format figé yyyy-MM-ddTHH:mm:ssZ (ADR-0007)");
        ((string)map["LastSuccessfulSyncUtc"]!).Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");
    }

    /// <summary>
    /// Présence des golden files — OU régénération sur demande explicite. En mode normal (CI), ce
    /// test ÉCHOUE si un golden manque. Pour régénérer après une évolution VOLONTAIRE du contrat :
    /// <c>LIAKONT_REGEN_FIXTURES=1</c> + <c>LIAKONT_FIXTURE_OUT=&lt;tests/fixtures/contrat-v1&gt;</c>
    /// (écrit les fichiers, à committer + reporter les empreintes dans <see cref="FrozenHashes"/>).
    /// </summary>
    [Fact]
    public void Fixtures_are_present_or_regenerated()
    {
        string? outDir = Environment.GetEnvironmentVariable("LIAKONT_FIXTURE_OUT");
        bool regen = Environment.GetEnvironmentVariable("LIAKONT_REGEN_FIXTURES") == "1";

        if (regen && !string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir!);
            foreach (ContractFixtures.DocumentFixture fixture in ContractFixtures.Documents)
            {
                WriteCanonical(outDir!, fixture.Name, CanonicalJson.Serialize(fixture.Document));
            }

            WriteCanonical(outDir!, ContractFixtures.BatchFixtureName, ContractFixtures.ComposeBatchRequestJson());
            WriteCanonical(outDir!, ContractFixtures.HeartbeatFixtureName, ContractFixtures.ComposeHeartbeatJson());
            return;
        }

        foreach (ContractFixtures.DocumentFixture fixture in ContractFixtures.Documents)
        {
            File.Exists(Path.Combine(FixturesDirectory, fixture.Name + ".json")).Should().BeTrue(
                "golden file manquant : " + fixture.Name + " (régénérer via LIAKONT_REGEN_FIXTURES=1)");
        }

        File.Exists(Path.Combine(FixturesDirectory, ContractFixtures.BatchFixtureName + ".json")).Should().BeTrue();
        File.Exists(Path.Combine(FixturesDirectory, ContractFixtures.HeartbeatFixtureName + ".json")).Should().BeTrue();
    }

    private static string ReadFixture(string fileName)
    {
        string path = Path.Combine(FixturesDirectory, fileName);
        File.Exists(path).Should().BeTrue(
            "la fixture " + fileName + " doit être présente (copiée en sortie ; régénérer via LIAKONT_REGEN_FIXTURES=1)");
        return File.ReadAllText(path);
    }

    private static void WriteCanonical(string directory, string name, string json) =>
        File.WriteAllText(Path.Combine(directory, name + ".json"), json, new UTF8Encoding(false));
}
