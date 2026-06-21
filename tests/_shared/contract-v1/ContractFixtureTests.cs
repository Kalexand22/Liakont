namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Agent.Contracts.Transport;
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
        ["facture-push-agent-brut"] = "cefc4ed66002ce4305e1a3b3b38dc35f0e16f12399840d3c7fc07115dcf2b4a6",
    };

    /// <summary>
    /// Propriétés d'enveloppe HORS du périmètre du format ILLUSTRATIF (télémétrie diagnostique de
    /// heartbeat ajoutée par la supervision : leur encodage fil définitif appartient à l'ingestion,
    /// PIV04/PIV05). Figées explicitement pour qu'un champ AJOUTÉ au DTO sans être émis ET sans être
    /// listé ici casse <see cref="Envelope_composers_cover_every_dto_property_or_freeze_it_out_of_scope"/>
    /// (revue obligatoire — A7-cov-3).
    /// </summary>
    private static readonly HashSet<string> HeartbeatOutOfIllustrativeScope = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(HeartbeatRequestDto.ServiceState),
        nameof(HeartbeatRequestDto.PushQueueDepth),
        nameof(HeartbeatRequestDto.PushQueueErrorCount),
        nameof(HeartbeatRequestDto.LastRunStartedUtc),
        nameof(HeartbeatRequestDto.LastRunCompletedUtc),
        nameof(HeartbeatRequestDto.LastRunOutcome),
        nameof(HeartbeatRequestDto.LastError),
        nameof(HeartbeatRequestDto.DiskFreeBytes),
    };

    /// <summary>Propriétés de l'enveloppe batch hors du format illustratif (les régimes source ne sont
    /// pas portés par le lot de référence à deux documents).</summary>
    private static readonly HashSet<string> BatchOutOfIllustrativeScope = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PushBatchRequestDto.SourceTaxRegimes),
        nameof(PushBatchRequestDto.ExtractorCapabilities),
    };

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "contrat-v1");

    /// <summary>
    /// Golden d'enveloppe du seam de cohabitation N/N-1 (RDF08) : mêmes documents que la v1, portés
    /// par la version <see cref="ContractFixtures.CohabitationNextVersion"/>. Pas de golden de document
    /// ici — le modèle de payload est partagé tant qu'aucune rupture réelle n'existe.
    /// </summary>
    private static string V2FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "contrat-v2");

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

        map.ContainsKey(nameof(PushBatchRequestDto.ContractVersion)).Should().BeTrue("la clé d'enveloppe doit suivre le nom de propriété du DTO");
        map.ContainsKey(nameof(PushBatchRequestDto.Documents)).Should().BeTrue("la clé d'enveloppe doit suivre le nom de propriété du DTO");

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

        map.ContainsKey(nameof(HeartbeatRequestDto.ContractVersion)).Should().BeTrue();
        map.ContainsKey(nameof(HeartbeatRequestDto.AgentVersion)).Should().BeTrue();
        map.ContainsKey(nameof(HeartbeatRequestDto.SentAtUtc)).Should().BeTrue();
        map.ContainsKey(nameof(HeartbeatRequestDto.LastSuccessfulSyncUtc)).Should().BeTrue();
    }

    // ── Seam de cohabitation N/N-1 (RDF08) ──────────────────────────────────────────────────────
    // ADR-0001/F12 promet « la plateforme supporte N ET N-1 ». Ces tests matérialisent l'axe de
    // SÉRIALISATION du seam AVANT toute rupture réelle : un jeu de golden d'enveloppe v2
    // (tests/fixtures/contrat-v2/) prouve, des DEUX côtés (net48 + .NET 10), que l'axe de version
    // négociée (URL/en-tête) est ORTHOGONAL à l'empreinte par document. L'axe de NÉGOCIATION du seam
    // (426 / N-1) est exercé par AgentContractVersionPolicyTests (plateforme).
    [Fact]
    public void Cohabitation_v2_batch_envelope_is_canonical_and_shares_v1_document_payloads()
    {
        string v1Golden = ReadFixture(ContractFixtures.BatchFixtureName + ".json");
        string v2Golden = ReadV2Fixture(ContractFixtures.BatchFixtureName + ".json");

        // 1. Identité octet-pour-octet cross-runtime de l'enveloppe v2 (preuve net48 == .NET 10).
        ContractFixtures.ComposeBatchRequestJson(ContractFixtures.CohabitationNextVersion).Should().Be(
            v2Golden, "le golden v2 est l'unique sortie canonique attendue des deux côtés (F12 §3.4)");

        // 2. L'enveloppe v2 ne diffère de la v1 QUE par la valeur de ContractVersion : remplacer la
        //    version N par N-1 redonne EXACTEMENT la v1 (axe de version orthogonal au payload).
        v2Golden.Replace(
                "\"ContractVersion\":\"" + ContractFixtures.CohabitationNextVersion + "\"",
                "\"ContractVersion\":\"" + AgentContractVersion.ContractVersion + "\"")
            .Should().Be(v1Golden, "seule la version négociée distingue les enveloppes v1 et v2");

        // 3. Les documents embarqués portent une empreinte IDENTIQUE à leurs golden v1 : un document
        //    poussé sous contrat N ou N-1 hashe pareil (anti-doublon stable à travers le seam).
        foreach (string name in new[] { "facture-standard-b2c", "avoir-simple-lie" })
        {
            string docJson = CanonicalJson.Serialize(ContractFixtures.GetDocument(name));
            v2Golden.Should().Contain(docJson, "le lot v2 transporte les mêmes payloads canoniques que la v1");
            PayloadHasher.ComputeHash(docJson).Should().Be(
                FrozenHashes[name],
                "l'empreinte d'un document est invariante par version de contrat négociée (axe orthogonal)");
        }
    }

    [Fact]
    public void Cohabitation_v2_heartbeat_envelope_is_canonical_and_version_only_differs()
    {
        string v1Golden = ReadFixture(ContractFixtures.HeartbeatFixtureName + ".json");
        string v2Golden = ReadV2Fixture(ContractFixtures.HeartbeatFixtureName + ".json");

        ContractFixtures.ComposeHeartbeatJson(ContractFixtures.CohabitationNextVersion).Should().Be(
            v2Golden, "le golden v2 du heartbeat est l'unique sortie canonique attendue des deux côtés");

        v2Golden.Replace(
                "\"ContractVersion\":\"" + ContractFixtures.CohabitationNextVersion + "\"",
                "\"ContractVersion\":\"" + AgentContractVersion.ContractVersion + "\"")
            .Should().Be(v1Golden, "seule la version négociée distingue les heartbeats v1 et v2");
    }

    /// <summary>
    /// Garde de complétude des composeurs d'enveloppe ILLUSTRATIVE écrits À LA MAIN (le writer
    /// canonique ne réinjecte pas un sous-document déjà sérialisé). Chaque propriété publique du DTO
    /// d'enveloppe est SOIT émise comme clé du JSON illustratif, SOIT figée hors périmètre (XOR) :
    /// un champ ajouté au DTO sans l'un ni l'autre — ou figé ET émis — casse le test et impose une
    /// revue. Évite la dérive silencieuse « heartbeat à la main vs DTO » (A7-cov-3, RDL03).
    /// </summary>
    [Fact]
    public void Envelope_composers_cover_every_dto_property_or_freeze_it_out_of_scope()
    {
        AssertEnvelopeCoverage(typeof(HeartbeatRequestDto), ContractFixtures.ComposeHeartbeatJson(), HeartbeatOutOfIllustrativeScope);
        AssertEnvelopeCoverage(typeof(PushBatchRequestDto), ContractFixtures.ComposeBatchRequestJson(), BatchOutOfIllustrativeScope);
    }

    /// <summary>
    /// Non-régression ADDITIVE ancrée sur TOUT le jeu de fixtures (pas un seul golden) : le dernier
    /// champ additif du contrat (BT-9 <c>PaymentDueDate</c>, ADR-0007) est OMIS pour toute fixture qui
    /// ne le porte pas, et son empreinte figée reste inchangée — preuve qu'ajouter un optionnel en fin
    /// de DTO ne perturbe aucun document existant (A7-evo-5).
    /// </summary>
    /// <param name="name">Nom de la fixture document.</param>
    [Theory]
    [MemberData(nameof(ContractFixtures.DocumentCases), MemberType = typeof(ContractFixtures))]
    public void Additive_optional_field_is_omitted_and_hash_frozen_for_every_fixture(string name)
    {
        PivotDocumentDto document = ContractFixtures.GetDocument(name);
        string json = CanonicalJson.Serialize(document);

        document.PaymentDueDate.Should().BeNull("aucune fixture du jeu ne porte d'échéance — c'est l'ancre de non-régression additive");
        json.Should().NotContain("PaymentDueDate", "un optionnel non porté n'est jamais émis (le hash figé doit rester intact)");
        PayloadHasher.ComputeHash(document).Should().Be(
            FrozenHashes[name],
            "l'ajout d'un champ additif (BT-9) ne change RIEN pour une fixture qui ne le porte pas (" + name + ")");
    }

    /// <summary>
    /// Présence des golden files — OU régénération sur demande explicite. En mode normal (CI), ce
    /// test ÉCHOUE si un golden manque. Pour régénérer après une évolution VOLONTAIRE du contrat :
    /// <c>LIAKONT_REGEN_FIXTURES=1</c> + <c>LIAKONT_FIXTURE_OUT=&lt;tests/fixtures/contrat-v1&gt;</c>.
    /// La régénération écrit les fichiers PUIS FAIT ÉCHOUER LE TEST VOLONTAIREMENT — un flag qui fuite
    /// dans CI échoue bruyamment au lieu de passer silencieusement. Après écriture, reporter les
    /// empreintes dans <see cref="FrozenHashes"/> et committer les fichiers (gate humaine).
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

            // Golden d'enveloppe du seam de cohabitation N/N-1 (RDF08), dans le dossier sœur contrat-v2.
            string? parent = Path.GetDirectoryName(outDir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string v2OutDir = Path.Combine(parent!, "contrat-v2");
            Directory.CreateDirectory(v2OutDir);
            WriteCanonical(v2OutDir, ContractFixtures.BatchFixtureName, ContractFixtures.ComposeBatchRequestJson(ContractFixtures.CohabitationNextVersion));
            WriteCanonical(v2OutDir, ContractFixtures.HeartbeatFixtureName, ContractFixtures.ComposeHeartbeatJson(ContractFixtures.CohabitationNextVersion));
            throw new InvalidOperationException(
                "Golden files régénérés dans " + outDir + ". Retirez LIAKONT_REGEN_FIXTURES/LIAKONT_FIXTURE_OUT, "
                + "reportez les empreintes dans FrozenHashes, puis committez — un run de tests ne doit jamais régénérer silencieusement.");
        }

        foreach (ContractFixtures.DocumentFixture fixture in ContractFixtures.Documents)
        {
            File.Exists(Path.Combine(FixturesDirectory, fixture.Name + ".json")).Should().BeTrue(
                "golden file manquant : " + fixture.Name + " (régénérer via LIAKONT_REGEN_FIXTURES=1)");
        }

        File.Exists(Path.Combine(FixturesDirectory, ContractFixtures.BatchFixtureName + ".json")).Should().BeTrue();
        File.Exists(Path.Combine(FixturesDirectory, ContractFixtures.HeartbeatFixtureName + ".json")).Should().BeTrue();

        // Golden d'enveloppe du seam de cohabitation N/N-1 (RDF08).
        File.Exists(Path.Combine(V2FixturesDirectory, ContractFixtures.BatchFixtureName + ".json")).Should().BeTrue(
            "golden v2 manquant : " + ContractFixtures.BatchFixtureName + " (régénérer via LIAKONT_REGEN_FIXTURES=1)");
        File.Exists(Path.Combine(V2FixturesDirectory, ContractFixtures.HeartbeatFixtureName + ".json")).Should().BeTrue(
            "golden v2 manquant : " + ContractFixtures.HeartbeatFixtureName + " (régénérer via LIAKONT_REGEN_FIXTURES=1)");
    }

    private static void AssertEnvelopeCoverage(Type dtoType, string composedJson, HashSet<string> outOfScope)
    {
        IDictionary<string, object?> map = PivotCanonicalReader.ParseToMap(composedJson);

        foreach (PropertyInfo property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            bool emitted = map.ContainsKey(property.Name);
            bool frozenOut = outOfScope.Contains(property.Name);

            (emitted ^ frozenOut).Should().BeTrue(
                dtoType.Name + "." + property.Name + " doit être SOIT émis dans le format illustratif, SOIT figé hors périmètre (jamais les deux, jamais aucun) — un champ ajouté sans revue casse ici (A7-cov-3)");
        }

        // Pas d'entrée hors-périmètre périmée : chaque nom figé doit encore correspondre à une propriété.
        foreach (string name in outOfScope)
        {
            dtoType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance).Should().NotBeNull(
                "l'entrée hors-périmètre " + dtoType.Name + "." + name + " doit correspondre à une propriété réelle (liste à revoir si un champ a été retiré)");
        }
    }

    private static string ReadFixture(string fileName)
    {
        string path = Path.Combine(FixturesDirectory, fileName);
        File.Exists(path).Should().BeTrue(
            "la fixture " + fileName + " doit être présente (copiée en sortie ; régénérer via LIAKONT_REGEN_FIXTURES=1)");
        return File.ReadAllText(path);
    }

    private static string ReadV2Fixture(string fileName)
    {
        string path = Path.Combine(V2FixturesDirectory, fileName);
        File.Exists(path).Should().BeTrue(
            "le golden v2 " + fileName + " doit être présent (copié en sortie ; régénérer via LIAKONT_REGEN_FIXTURES=1)");
        return File.ReadAllText(path);
    }

    private static void WriteCanonical(string directory, string name, string json) =>
        File.WriteAllText(Path.Combine(directory, name + ".json"), json, new UTF8Encoding(false));
}
