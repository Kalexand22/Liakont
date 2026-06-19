namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

// Petits tableaux littéraux de données de test passés en argument (CA1861) : volontaires, lisibilité.
#pragma warning disable CA1861

/// <summary>
/// Règles de format figées de la sérialisation canonique (PIV02, ADR-0007), vérifiées une à une sur
/// la plateforme (.NET 10). L'identité de la sortie entre net48 et .NET 10 et le round-trip sont,
/// eux, prouvés des DEUX côtés par <see cref="ContractTests.PivotContractGoldenTests"/>.
/// </summary>
public sealed class CanonicalJsonRulesTests
{
    [Fact]
    public void Decimal_amounts_preserve_source_scale_without_exponent()
    {
        string json = CanonicalJson.Serialize(
            Build(totals: new PivotTotalsDto(totalNet: 10.00m, totalTax: 0m, totalGross: 1234.5m)));

        json.Should().Contain("\"TotalNet\":10.00", "l'échelle source 2 est préservée");
        json.Should().Contain("\"TotalTax\":0", "un montant entier reste « 0 », sans décimales superflues");
        json.Should().Contain("\"TotalGross\":1234.5", "l'échelle source 1 est préservée");
        json.Should().NotContain("E+").And.NotContain("e+", "jamais de notation exponentielle");
    }

    [Fact]
    public void Negative_amounts_keep_their_sign_and_scale()
    {
        string json = CanonicalJson.Serialize(
            Build(totals: new PivotTotalsDto(totalNet: -1000.05m, totalTax: 0m, totalGross: -1000.05m)));

        json.Should().Contain("\"TotalNet\":-1000.05");
    }

    [Fact]
    public void Dates_use_iso_yyyy_MM_dd_and_drop_time_of_day()
    {
        string json = CanonicalJson.Serialize(Build(issueDate: new DateTime(2026, 2, 1, 13, 45, 30)));

        json.Should().Contain("\"IssueDate\":\"2026-02-01\"");
    }

    [Fact]
    public void Enumerations_are_serialized_by_name()
    {
        var line = new PivotLineDto(
            description: "ligne",
            netAmount: 0m,
            taxes: new[] { new PivotLineTaxDto(taxAmount: 0m, categoryCode: VatCategory.AE) });
        string json = CanonicalJson.Serialize(Build(category: OperationCategory.Mixte, lines: new[] { line }));

        json.Should().Contain("\"OperationCategory\":\"Mixte\"");
        json.Should().Contain("\"CategoryCode\":\"AE\"");
    }

    [Fact]
    public void Null_optional_members_are_omitted()
    {
        string json = CanonicalJson.Serialize(Build(supplier: new PivotPartyDto("Fournisseur Fictif")));

        json.Should().NotContain("\"Siren\"", "un champ optionnel nul est OMIS, jamais émis à null");
        json.Should().NotContain("\"Email\"");
        json.Should().NotContain("\"Customer\"");
        json.Should().NotContain("\"PrepaidAmount\"");
        json.Should().NotContain("null", "aucune valeur null n'apparaît dans le JSON canonique");
    }

    [Fact]
    public void Collections_are_always_emitted_even_when_empty()
    {
        string json = CanonicalJson.Serialize(Build());

        json.Should().Contain("\"Lines\":[]");
        json.Should().Contain("\"CreditNoteRefs\":[]");
        json.Should().Contain("\"Payments\":[]");
        json.Should().Contain("\"DocumentCharges\":[]");
    }

    [Fact]
    public void Members_are_emitted_in_declaration_order()
    {
        string json = CanonicalJson.Serialize(Build());

        json.IndexOf("\"SourceDocumentKind\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"Number\"", StringComparison.Ordinal));
        json.IndexOf("\"Number\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"IssueDate\"", StringComparison.Ordinal));
        json.IndexOf("\"OperationCategory\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"CurrencyCode\"", StringComparison.Ordinal));
        json.IndexOf("\"Lines\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"IsSelfBilled\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Quotes_and_backslashes_are_escaped()
    {
        string json = CanonicalJson.Serialize(Build(supplier: new PivotPartyDto("a\"b\\c")));

        json.Should().Contain("a\\\"b\\\\c", "\" devient \\\" et \\ devient \\\\");
    }

    [Fact]
    public void Non_ascii_characters_are_escaped_so_output_stays_ascii()
    {
        string json = CanonicalJson.Serialize(Build(supplier: new PivotPartyDto("café déjà vu")));

        json.All(c => c >= ' ' && c <= '~').Should().BeTrue("sortie ASCII pur");
        json.Should().NotContain("é", "les caractères non-ASCII sont échappés \\uXXXX, pas émis bruts");
    }

    [Fact]
    public void Control_and_non_bmp_characters_are_escaped_to_lowercase_4hex()
    {
        // Cas limites d'échappement (A7-fmt-4) : un caractère de contrôle (NUL), le DEL (0x7F, juste
        // au-dessus de '~' = 0x7E), et un caractère HORS-BMP (émoji) émis comme paire de substitution
        // UTF-16 déterministe, code-unité par code-unité. Construits par code (jamais de NUL brut ni
        // d'échappement \uXXXX dans le source) pour rester robustes au passage par les outils.
        string nul = ((char)0x00).ToString(CultureInfo.InvariantCulture);
        string del = ((char)0x7F).ToString(CultureInfo.InvariantCulture);
        string nonBmp = char.ConvertFromUtf32(0x1F600); // 😀 → paire de substitution U+D83D U+DE00

        string json = CanonicalJson.Serialize(Build(supplier: new PivotPartyDto(nul + del + nonBmp)));

        json.Should().Contain(Escape(0x00), "NUL (< 0x20) est échappé en \\u0000");
        json.Should().Contain(Escape(0x7F), "DEL (> 0x7E) est échappé en hexadécimal minuscule");
        json.Should().Contain(Escape(0xD83D) + Escape(0xDE00), "un caractère hors-BMP est émis comme sa paire de substitution UTF-16, déterministe");
        json.All(c => c >= ' ' && c <= '~').Should().BeTrue("la sortie reste ASCII pur même pour les caractères de contrôle et hors-BMP");
    }

    [Fact]
    public void Decimal_zero_with_explicit_scale_keeps_its_trailing_zeros()
    {
        // A7-fmt-4 : 0.00m garde son échelle source (2), jamais réduit à « 0 » — l'échelle fait partie
        // de l'empreinte (deux montants d'échelle différente ne sont pas le même payload).
        string json = CanonicalJson.Serialize(
            Build(totals: new PivotTotalsDto(totalNet: 0.00m, totalTax: 0m, totalGross: 0.0m)));

        json.Should().Contain("\"TotalNet\":0.00", "0.00m conserve son échelle 2");
        json.Should().Contain("\"TotalTax\":0", "0m (échelle 0) reste « 0 »");
        json.Should().Contain("\"TotalGross\":0.0", "0.0m conserve son échelle 1");
    }

    [Fact]
    public void Decimal_with_maximum_scale_is_emitted_in_full_without_exponent()
    {
        // A7-fmt-4 : un decimal à l'échelle MAXIMALE (28 décimales) est émis en entier, culture
        // invariante, JAMAIS en notation exponentielle (garanti par le type decimal).
        const decimal maxScale = 0.0000000000000000000000000001m; // 1e-28, échelle 28

        string json = CanonicalJson.Serialize(
            Build(totals: new PivotTotalsDto(totalNet: maxScale, totalTax: 0m, totalGross: 0m)));

        json.Should().Contain("\"TotalNet\":0.0000000000000000000000000001", "l'échelle max est préservée intégralement");
        json.Should().NotContain("E+").And.NotContain("E-", "jamais d'exposant, même à l'échelle maximale");
    }

    [Fact]
    public void Hash_of_document_equals_hash_of_its_canonical_json()
    {
        var document = Build();

        PayloadHasher.ComputeHash(document)
            .Should().Be(PayloadHasher.ComputeHash(CanonicalJson.Serialize(document)));
    }

    [Fact]
    public void Hash_is_sensitive_to_a_field_change()
    {
        string a = PayloadHasher.ComputeHash(Build(number: "AV-1"));
        string b = PayloadHasher.ComputeHash(Build(number: "AV-2"));

        a.Should().NotBe(b, "un seul champ qui change doit changer l'empreinte (anti-doublon PIV04)");
    }

    [Fact]
    public void Serialize_rejects_a_null_document()
    {
        Action act = () => CanonicalJson.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelfBilled_keeps_source_number_in_canonical_and_no_allocated_fiscal_number()
    {
        // INV-BT1-1 (ADR-0025 §3, MND05) : en 389, le payload canonique est INCHANGÉ — `Number` reste
        // l'identifiant source (déjà hashé), et le BT-1 FISCAL alloué par mandant vit HORS du payload hashé
        // (assigné à l'émission sur l'acceptation, jamais dans le pivot). Aucune branche de format au flux 389.
        string json = CanonicalJson.Serialize(Build(number: "F00172473", isSelfBilled: true));

        json.Should().Contain("\"Number\":\"F00172473\"", "en 389, Number reste l'identifiant source, jamais un BT-1 fiscal alloué.");
        json.Should().Contain("\"IsSelfBilled\":true");
        json.Should().NotContain("Allocated", "le BT-1 fiscal alloué n'entre jamais dans le payload canonique (hors hash).");
    }

    [Fact]
    public void SelfBilled_flag_is_the_only_canonical_difference_no_format_branch()
    {
        // Aucune branche de format conditionnelle au flux 389 (ADR-0007 préservé) : un pivot self-billed et son
        // équivalent standard ne diffèrent QUE par le booléen IsSelfBilled — aucun champ de numéro fiscal ajouté.
        string selfBilled = CanonicalJson.Serialize(Build(number: "F1", isSelfBilled: true));
        string standard = CanonicalJson.Serialize(Build(number: "F1", isSelfBilled: false));

        selfBilled.Replace("\"IsSelfBilled\":true", "\"IsSelfBilled\":false")
            .Should().Be(standard, "le seul différenciateur 389/standard au canonique est le booléen : le hash reste celui des champs du pivot.");
    }

    /// <summary>
    /// Garantit que chaque propriété publique de chaque DTO pivot apparaît comme clé JSON dans
    /// la sortie canonique d'un document entièrement peuplé. Un champ ajouté à un DTO sans mise
    /// à jour du writer serait détecté immédiatement (PIV04 / TRK03).
    /// </summary>
    [Fact]
    public void All_public_properties_of_every_pivot_dto_appear_as_json_keys_in_fully_populated_document()
    {
        WalkFullyPopulatedDtoNodes(AssertAllPropertiesArePresent);
    }

    /// <summary>
    /// Au-delà de la PRÉSENCE (test ci-dessus), garantit que l'ORDRE d'émission JSON de CHAQUE DTO
    /// imbriqué suit l'ordre de DÉCLARATION du DTO (règle 1 d'<c>ADR-0007</c>). Un champ inséré au
    /// milieu d'un DTO mais écrit en fin par le writer passerait la présence mais casserait
    /// l'empreinte du premier document réel qui le porte : ce test l'attrape immédiatement (RDL03).
    /// </summary>
    [Fact]
    public void Json_key_order_of_every_pivot_dto_matches_its_declaration_order()
    {
        WalkFullyPopulatedDtoNodes(AssertKeyOrderMatchesDeclarationOrder);
    }

    /// <summary>
    /// Parcourt chaque nœud DTO du document entièrement peuplé et applique <paramref name="assert"/>
    /// (couverture par présence ET ordre partagent la même navigation, source unique).
    /// </summary>
    private static void WalkFullyPopulatedDtoNodes(Action<IDictionary<string, object?>, Type> assert)
    {
        string json = CanonicalJson.Serialize(ContractTests.ContractFixtures.BuildFullyPopulatedDocument());
        var root = ContractTests.PivotCanonicalReader.ParseToMap(json);

        assert(root, typeof(PivotDocumentDto));
        var supplier = Child(root, "Supplier");
        assert(supplier, typeof(PivotPartyDto));
        assert(Child(supplier, "Address"), typeof(PivotAddressDto));
        assert(Child(root, "Totals"), typeof(PivotTotalsDto));
        var line = Element(root, "Lines", 0);
        assert(line, typeof(PivotLineDto));
        assert(Element(line, "Taxes", 0), typeof(PivotLineTaxDto));
        assert(Element(root, "CreditNoteRefs", 0), typeof(PivotDocumentRefDto));
        assert(Element(root, "Payments", 0), typeof(PivotPaymentDto));
        assert(Element(root, "DocumentCharges", 0), typeof(PivotDocumentChargeDto));
    }

    private static void AssertAllPropertiesArePresent(IDictionary<string, object?> node, Type dtoType)
    {
        foreach (string propertyName in DeclaredPropertyNames(dtoType))
        {
            node.Keys.Should().Contain(
                propertyName,
                $"la propriété {dtoType.Name}.{propertyName} doit être une clé de SON objet JSON (complétude par-DTO : anti-doublon PIV04 / détection d'altération TRK03)");
        }
    }

    private static void AssertKeyOrderMatchesDeclarationOrder(IDictionary<string, object?> node, Type dtoType)
    {
        // Document entièrement peuplé ⇒ chaque propriété est émise : l'ordre des clés JSON doit être
        // EXACTEMENT l'ordre de déclaration du DTO (≠ simple présence — détecte l'insertion-au-milieu).
        node.Keys.Should().Equal(
            DeclaredPropertyNames(dtoType),
            $"l'ordre d'émission JSON de {dtoType.Name} doit suivre l'ordre de déclaration du DTO (ADR-0007 règle 1)");
    }

    /// <summary>Noms des propriétés publiques d'instance d'un DTO, dans l'ordre de DÉCLARATION
    /// (jeton de métadonnées — l'ordre de <see cref="Type.GetProperties()"/> n'est pas garanti).</summary>
    private static List<string> DeclaredPropertyNames(Type dtoType) =>
        dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToList();

    /// <summary>Échappement canonique attendu d'un code de caractère (<c>\uXXXX</c> minuscule), construit
    /// par code pour ne jamais écrire un échappement \u littéral dans le source.</summary>
    private static string Escape(int code) =>
        ((char)92).ToString(CultureInfo.InvariantCulture) + "u" + code.ToString("x4", CultureInfo.InvariantCulture);

    private static IDictionary<string, object?> Child(IDictionary<string, object?> node, string key) =>
        (IDictionary<string, object?>)node[key]!;

    private static IDictionary<string, object?> Element(IDictionary<string, object?> node, string key, int index) =>
        (IDictionary<string, object?>)((List<object?>)node[key]!)[index]!;

    private static PivotDocumentDto Build(
        string number = "AV-1",
        DateTime? issueDate = null,
        PivotPartyDto? supplier = null,
        PivotPartyDto? customer = null,
        PivotTotalsDto? totals = null,
        OperationCategory category = OperationCategory.LivraisonBiens,
        PivotLineDto[]? lines = null,
        decimal? prepaid = null,
        bool isSelfBilled = false)
    {
        return new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: number,
            issueDate: issueDate ?? new DateTime(2026, 1, 1),
            sourceReference: "ref",
            supplier: supplier ?? new PivotPartyDto("Fournisseur Fictif"),
            totals: totals ?? new PivotTotalsDto(0m, 0m, 0m),
            operationCategory: category,
            customer: customer,
            lines: lines,
            prepaidAmount: prepaid,
            isSelfBilled: isSelfBilled);
    }
}
