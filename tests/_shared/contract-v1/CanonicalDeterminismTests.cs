namespace Liakont.Agent.Contracts.ContractTests;

using System;
using System.IO;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

/// <summary>
/// Déterminisme de la sérialisation canonique (PIV02, ADR-0007), prouvé des DEUX côtés (net48 + .NET 10)
/// — ce fichier est LIÉ dans les deux projets de test. Trois invariants ajoutés par RDL05 :
/// <list type="bullet">
/// <item>normalisation Unicode NFC du texte LIBRE (règle 7) : NFC ≡ NFD → même octet, même empreinte ;</item>
/// <item>invariant « date calendaire » de <c>WriteDate</c> (règle 6) : <c>DateTimeKind</c> et heure ignorés ;</item>
/// <item>verrouillage octets-bruts des golden files : pas de BOM, LF strict, pas de newline finale.</item>
/// </list>
/// </summary>
public sealed class CanonicalDeterminismTests
{
    [Fact]
    public void Free_text_in_nfc_and_nfd_produces_the_same_canonical_bytes_and_hash()
    {
        // « café » sous deux formes Unicode canoniquement équivalentes, construites PAR CODE (échappements
        // \u) pour que le fichier source reste sans ambiguïté d'octets : précomposé (U+00E9) vs décomposé
        // (lettre « e » suivie de l'accent aigu combinant U+0301).
        string nfc = "caf" + (char)0x00E9;  // « café », é PRÉCOMPOSÉ (U+00E9)
        string nfd = "cafe" + (char)0x0301; // « café », e + accent aigu COMBINANT (U+0301)
        nfc.Should().NotBe(nfd, "les deux formes diffèrent au niveau des unités de code UTF-16 (prérequis du test)");

        PivotDocumentDto withNfc = BuildWithSupplierName(nfc);
        PivotDocumentDto withNfd = BuildWithSupplierName(nfd);

        string jsonNfc = CanonicalJson.Serialize(withNfc);
        string jsonNfd = CanonicalJson.Serialize(withNfd);

        jsonNfd.Should().Be(
            jsonNfc,
            "NFC et NFD sont la même chaîne abstraite → même JSON canonique octet par octet (ADR-0007 règle 7)");
        jsonNfc.Should().Contain(
            "caf\\u00e9",
            "la forme canonique émise est NFC (é précomposé, échappé \\u00e9), jamais la décomposée");
        PayloadHasher.ComputeHash(withNfd).Should().Be(
            PayloadHasher.ComputeHash(withNfc),
            "deux extractions NFC/NFD du même document produisent la MÊME empreinte (anti-doublon PIV04)");
    }

    [Fact]
    public void Lone_surrogate_is_serialized_deterministically_without_throwing()
    {
        // Surrogate UTF-16 ISOLÉ (nvarchar tronqué côté source) : String.Normalize lèverait « Unicode invalide ».
        // RDL05 ne doit PAS introduire un nouveau rejet hors périmètre — on préserve l'échappement code-unité
        // déterministe d'avant (chaque unité → \udXXX), identique des deux côtés, sans exception.
        string loneSurrogate = "lot" + (char)0xD83D; // high surrogate sans low surrogate associé

        string json = CanonicalJson.Serialize(BuildWithSupplierName(loneSurrogate));

        json.Should().Contain(
            "lot\\ud83d",
            "le surrogate isolé est échappé code-unité (\\ud83d), sans normalisation NFC ni exception");
    }

    [Fact]
    public void WriteDate_ignores_kind_and_time_of_day_for_the_same_calendar_date()
    {
        // Même date calendaire 2026-03-15, trois Kind et trois heures du jour différentes.
        string utc = WriteDateOnly(new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Utc));
        string local = WriteDateOnly(new DateTime(2026, 3, 15, 1, 15, 0, DateTimeKind.Local));
        string unspecified = WriteDateOnly(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Unspecified));

        utc.Should().Be("\"2026-03-15\"", "seule la composante calendaire est émise (ADR-0007 règle 6)");
        local.Should().Be(utc, "DateTimeKind.Local ne décale pas la date — aucune conversion de fuseau");
        unspecified.Should().Be(utc, "Unspecified et l'heure du jour sont ignorés (même octet)");
    }

    [Fact]
    public void Golden_fixture_files_are_locked_lf_no_bom_no_trailing_newline()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "contrat-v1");
        string[] files = Directory.GetFiles(directory, "*.json");

        files.Should().NotBeEmpty("les golden files du contrat v1 doivent être copiés en sortie de test");

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            byte[] bytes = File.ReadAllBytes(path);

            bytes.Length.Should().BeGreaterThan(0, "un golden vide n'a pas d'empreinte stable : " + name);
            HasUtf8Bom(bytes).Should().BeFalse(
                "aucun BOM UTF-8 — les octets bruts du fichier sont hashés (ADR-0007) : " + name);
            bytes.Should().NotContain(
                (byte)0x0D, "aucun CR — fins de ligne LF strictes (verrou .gitattributes -text) : " + name);
            bytes[bytes.Length - 1].Should().NotBe(
                (byte)0x0A, "aucune newline finale — le fichier EST l'octet canonique hashé : " + name);
        }
    }

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static string WriteDateOnly(DateTime value)
    {
        var writer = new CanonicalJsonWriter();
        writer.WriteDate(value);
        return writer.ToString();
    }

    private static PivotDocumentDto BuildWithSupplierName(string supplierName)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: "F",
            number: "F-2026-0001",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "no_ba=1",
            supplier: new PivotPartyDto(supplierName),
            totals: new PivotTotalsDto(0m, 0m, 0m),
            operationCategory: OperationCategory.LivraisonBiens);
    }
}
