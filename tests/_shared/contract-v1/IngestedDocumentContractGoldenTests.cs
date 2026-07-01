namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Ged.Serialization;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

// Données golden de test : les petits tableaux littéraux passés au constructeur (CA1861) sont
// volontaires — extraire des champs static readonly nuirait à la lisibilité du document de référence
// (même relâchement que PivotContractGoldenTests).
#pragma warning disable CA1861

/// <summary>
/// Tests de contrat CROSS-RUNTIME du canal GED (F19 §4.2/§4.6). Ce fichier est LIÉ dans les deux
/// projets de test : la plateforme (.NET 10, <c>src/Liakont.sln</c>) ET l'agent (net48,
/// <c>agent/Liakont.Agent.sln</c>). Le MÊME document GED golden (champs bruts en ordre d'insertion
/// MÊLÉ, axes/entités/relations, contenu binaire, horodatage UTC, non-ASCII), sérialisé par l'UNIQUE
/// <see cref="GedCanonicalJson"/> partagé, doit produire la MÊME empreinte des deux côtés. L'empreinte
/// figée <see cref="GoldenIngestedSha256"/> est l'ancre d'identité : si net48 et .NET 10 divergeaient
/// d'un seul octet — ou en cas de régression de format — ce test casse.
///
/// RL-39 (cœur du canal) : <see cref="IngestedDocumentDto.SourceFields"/> est un dictionnaire (ordre
/// d'itération NON garanti) ; il est émis TRIÉ PAR CLÉ (ordinal), sinon l'anti-doublon
/// <c>(tenant, payload_hash)</c> du registre GED casserait selon l'ordre de parcours.
/// </summary>
public sealed class IngestedDocumentContractGoldenTests
{
    // Empreinte SHA-256 (hex minuscule) figée du JSON canonique du document GED golden.
    private const string GoldenIngestedSha256 = "62e23d9113c13daf0ab0942fb4a37b2999052bc24b6e506aa953e779383ef628";

    /// <summary>
    /// Construit le document GED golden (données FICTIVES uniquement). Les <c>SourceFields</c> sont
    /// insérés dans un ordre NON trié (« zeta » avant « alpha », « 10 » avant « 2 ») pour que le test
    /// prouve le tri ordinal à la sérialisation, pas un dictionnaire déjà ordonné.
    /// </summary>
    /// <returns>Le document GED de référence.</returns>
    public static IngestedDocumentDto BuildGoldenManagedDocument()
    {
        // Ordre d'insertion MÊLÉ (et clés non-ASCII / numériques) : le tri ordinal attendu à la sortie est
        // "10" < "2" < "Z" < "alpha" < "café" (comparaison de code d'unité, pas numérique ni culturelle).
        var sourceFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Z"] = "majuscule",
            ["café"] = "décomposé é",
            ["2"] = "deux",
            ["alpha"] = "premier",
            ["10"] = "dix",
        };

        var axes = new[]
        {
            new RawAxisHint("$.fields.date_clôture", new[] { "2026-02-01", "2026-02-02" }),
            new RawAxisHint("$.fields.numéro_lot", new[] { "12" }),
        };

        var entities = new[]
        {
            new RawEntityHint(type: "acheteur", externalId: "BUY-42", display: "Dupont & Fïls"),
            new RawEntityHint(type: "vendeur", externalId: "SEL-7"),
        };

        var relations = new[]
        {
            new RawRelationHint(type: "concerne", targetExternalId: "SALE-99", targetType: "vente"),
        };

        var content = new IngestedContentRef(
            contentRef: "_ged/2026/PV-VENTE/pv-99.pdf",
            mediaType: "application/pdf",
            byteLength: 204_812,
            contentHash: "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");

        return new IngestedDocumentDto(
            sourceReference: "no_vente=99",
            documentType: "PV_VENTE",
            sourceTimestampUtc: new DateTime(2026, 2, 1, 14, 30, 15, DateTimeKind.Utc),
            content: content,
            sourceFields: sourceFields,
            sourceAxes: axes,
            sourceEntities: entities,
            sourceRelations: relations);
    }

    [Fact]
    public void Golden_ingested_document_hash_is_stable_and_identical_across_runtimes()
    {
        string hash = PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(BuildGoldenManagedDocument()));

        hash.Should().Be(
            GoldenIngestedSha256,
            "le JSON canonique GED doit être identique octet par octet entre net48 et .NET 10 (F19 §4.2, RL-39)");
    }

    [Fact]
    public void Ingested_hash_is_64_lowercase_hexadecimal_characters()
    {
        string hash = PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(BuildGoldenManagedDocument()));

        hash.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 en hexadécimal minuscule, 64 caractères");
    }

    [Fact]
    public void Ingested_canonical_json_is_pure_ascii()
    {
        string json = GedCanonicalJson.Serialize(BuildGoldenManagedDocument());

        json.All(c => c >= ' ' && c <= '~').Should().BeTrue(
            "la sortie est ASCII pur : tout caractère non-ASCII (clé ou valeur) est échappé \\uXXXX (ADR-0007)");
    }

    [Fact]
    public void Ingested_serialization_and_hash_are_deterministic()
    {
        var document = BuildGoldenManagedDocument();

        GedCanonicalJson.Serialize(document).Should().Be(GedCanonicalJson.Serialize(document));
        PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(document))
            .Should().Be(PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(document)));
    }

    [Fact]
    public void SourceFields_are_emitted_sorted_by_ordinal_key_regardless_of_insertion_order()
    {
        // RL-39 : le MÊME jeu de champs inséré dans un ordre DIFFÉRENT doit produire le MÊME JSON canonique
        // et le MÊME hash — sinon l'anti-doublon (tenant, payload_hash) du registre GED serait rompu.
        var scrambled = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["10"] = "dix",
            ["café"] = "décomposé é",
            ["Z"] = "majuscule",
            ["2"] = "deux",
            ["alpha"] = "premier",
        };
        var reference = BuildGoldenManagedDocument();
        var reordered = new IngestedDocumentDto(
            sourceReference: reference.SourceReference,
            documentType: reference.DocumentType,
            sourceTimestampUtc: reference.SourceTimestampUtc,
            content: reference.Content,
            sourceFields: scrambled,
            sourceAxes: reference.SourceAxes,
            sourceEntities: reference.SourceEntities,
            sourceRelations: reference.SourceRelations);

        GedCanonicalJson.Serialize(reordered).Should().Be(
            GedCanonicalJson.Serialize(reference),
            "l'ordre d'insertion du dictionnaire ne doit RIEN changer (tri ordinal figé, RL-39)");
        PayloadHasher.ComputeHash(GedCanonicalJson.Serialize(reordered)).Should().Be(
            GoldenIngestedSha256, "un ordre d'insertion différent produit la MÊME empreinte GED");
    }

    [Fact]
    public void SourceFields_ordinal_order_is_by_code_unit_not_numeric_or_culture()
    {
        string json = GedCanonicalJson.Serialize(BuildGoldenManagedDocument());

        // Tri ORDINAL (code d'unité) : "10" AVANT "2" (numériquement l'inverse) ; "Z" AVANT "alpha"
        // (culturellement l'inverse) ; "café" en dernier (« c » = 0x63). La clé non-ASCII « café » est
        // échappée dans le nom de membre (ASCII pur).
        int idx10 = json.IndexOf("\"10\":", StringComparison.Ordinal);
        int idx2 = json.IndexOf("\"2\":", StringComparison.Ordinal);
        int idxZ = json.IndexOf("\"Z\":", StringComparison.Ordinal);
        int idxAlpha = json.IndexOf("\"alpha\":", StringComparison.Ordinal);
        int idxCafe = json.IndexOf("\"caf\\u00e9\":", StringComparison.Ordinal);

        idx10.Should().BeGreaterThan(0);
        idx2.Should().BeGreaterThan(idx10, "« 10 » précède « 2 » en tri ordinal (pas numérique)");
        idxZ.Should().BeGreaterThan(idx2, "« Z » (0x5A) précède « alpha » (0x61) en tri ordinal (pas culturel)");
        idxAlpha.Should().BeGreaterThan(idxZ);
        idxCafe.Should().BeGreaterThan(idxAlpha, "« café » (0x63) trie après « alpha », clé échappée ASCII");
    }

    [Fact]
    public void Optional_fields_when_absent_are_omitted_from_the_canonical_json()
    {
        // Symétrie pivot « champ absent → omis » : un document minimal (sans horodatage, sans binaire, sans
        // libellé d'entité) n'émet aucune de ces clés. SourceFields/axes/entités/relations restent émis (vides).
        var minimal = new IngestedDocumentDto(
            sourceReference: "no_vente=1",
            documentType: "PV_VENTE");

        string json = GedCanonicalJson.Serialize(minimal);

        json.Should().NotContain("SourceTimestampUtc", "un horodatage absent n'émet aucune clé");
        json.Should().NotContain("Content", "un binaire absent n'émet aucune clé");
        json.Should().Be(
            "{\"SourceReference\":\"no_vente=1\",\"DocumentType\":\"PV_VENTE\",\"SourceFields\":{},\"SourceAxes\":[],\"SourceEntities\":[],\"SourceRelations\":[]}",
            "les collections sont émises même vides (objet/tableau), les optionnels scalaires sont omis");
    }

    [Fact]
    public void Entity_display_when_absent_is_omitted()
    {
        var doc = new IngestedDocumentDto(
            sourceReference: "no_vente=2",
            documentType: "PV_VENTE",
            sourceEntities: new[] { new RawEntityHint(type: "vendeur", externalId: "SEL-7") });

        string json = GedCanonicalJson.Serialize(doc);

        json.Should().Contain(
            "\"SourceEntities\":[{\"Type\":\"vendeur\",\"ExternalId\":\"SEL-7\"}]",
            "un libellé d'entité absent n'émet aucune clé Display (symétrie pivot « absent → null »)");
    }

    [Fact]
    public void SourceTimestampUtc_is_emitted_as_second_precision_utc_and_ignores_kind()
    {
        // Format ADR-0007 yyyy-MM-ddTHH:mm:ssZ, composantes verbatim, Kind ignoré : deux DateTime de mêmes
        // composantes mais de Kind différent produisent le MÊME octet (comme WriteDate pour une date).
        var utc = new IngestedDocumentDto("r", "t", sourceTimestampUtc: new DateTime(2026, 2, 1, 14, 30, 15, DateTimeKind.Utc));
        var unspecified = new IngestedDocumentDto("r", "t", sourceTimestampUtc: new DateTime(2026, 2, 1, 14, 30, 15, DateTimeKind.Unspecified));

        GedCanonicalJson.Serialize(utc).Should().Contain(
            "\"SourceTimestampUtc\":\"2026-02-01T14:30:15Z\"", "horodatage canonique à la seconde, suffixe Z littéral");
        GedCanonicalJson.Serialize(unspecified).Should().Be(
            GedCanonicalJson.Serialize(utc), "le DateTimeKind est ignoré (aucune conversion de fuseau)");
    }

    [Fact]
    public void SourceFields_key_in_NFD_produces_the_same_hash_as_NFC()
    {
        // ADR-0007 règle 7 / RL-39 : un nom de champ source (pas seulement une valeur) peut arriver en NFC ou
        // NFD selon le pilote ODBC. « café » précomposé (U+00E9) et décomposé (« cafe » + U+0301 combinant)
        // sont la MÊME clé abstraite et doivent produire le MÊME JSON canonique (donc le MÊME hash), sinon
        // l'anti-doublon (source_reference, payload_hash) se romprait selon la variante d'encodage reçue.
        var reference = BuildGoldenManagedDocument();
        var nfcFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["café"] = "valeur",
        };
        var nfdFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["café"] = "valeur",
        };

        var nfcDocument = new IngestedDocumentDto(
            sourceReference: reference.SourceReference,
            documentType: reference.DocumentType,
            sourceTimestampUtc: reference.SourceTimestampUtc,
            content: reference.Content,
            sourceFields: nfcFields,
            sourceAxes: reference.SourceAxes,
            sourceEntities: reference.SourceEntities,
            sourceRelations: reference.SourceRelations);
        var nfdDocument = new IngestedDocumentDto(
            sourceReference: reference.SourceReference,
            documentType: reference.DocumentType,
            sourceTimestampUtc: reference.SourceTimestampUtc,
            content: reference.Content,
            sourceFields: nfdFields,
            sourceAxes: reference.SourceAxes,
            sourceEntities: reference.SourceEntities,
            sourceRelations: reference.SourceRelations);

        string nfcJson = GedCanonicalJson.Serialize(nfcDocument);
        string nfdJson = GedCanonicalJson.Serialize(nfdDocument);

        nfdJson.Should().Be(nfcJson, "une clé SourceFields NFD doit se canonicaliser en NFC comme une valeur libre");
        PayloadHasher.ComputeHash(nfdJson).Should().Be(
            PayloadHasher.ComputeHash(nfcJson), "NFC et NFD de la même clé doivent produire la même empreinte");
    }

    [Fact]
    public void Serialize_null_document_throws()
    {
        Action act = () => GedCanonicalJson.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
