namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using System.Collections.Generic;
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
    public void UnitCode_absent_is_omitted_so_line_hash_is_unchanged()
    {
        // RD407 : champ additif BT-130. Une ligne sans unité (UnitCode null) ne doit RIEN ajouter au
        // JSON canonique — empreinte des goldens B2C inchangée octet par octet.
        var line = new PivotLineDto(description: "ligne", netAmount: 100m);
        string json = CanonicalJson.Serialize(Build(lines: new[] { line }));

        json.Should().NotContain("\"UnitCode\"", "absent = OMIS, jamais émis (hash B2C inchangé)");
    }

    [Fact]
    public void UnitCode_blank_is_normalized_to_absent()
    {
        // Une chaîne vide/blanche équivaut à « absent » : jamais émise (sinon elle changerait le hash).
        var line = new PivotLineDto(description: "ligne", netAmount: 100m, unitCode: "   ");
        string json = CanonicalJson.Serialize(Build(lines: new[] { line }));

        json.Should().NotContain("\"UnitCode\"");
    }

    [Fact]
    public void UnitCode_surrounding_whitespace_is_trimmed()
    {
        // Normalisation de surface : un code padné est borné, sinon les espaces fuiraient dans l'attribut
        // CII et brouilleraient l'empreinte canonique pour un simple écart d'espacement source.
        var line = new PivotLineDto(description: "ligne", netAmount: 100m, unitCode: "  KGM ");
        string json = CanonicalJson.Serialize(Build(lines: new[] { line }));

        json.Should().Contain("\"UnitCode\":\"KGM\"").And.NotContain("KGM ", "les espaces de bord sont retirés");
    }

    [Fact]
    public void UnitCode_when_present_is_emitted_at_end_of_line_and_changes_the_hash()
    {
        var withUnit = new PivotLineDto(description: "ligne", netAmount: 100m, unitCode: "KGM");
        string json = CanonicalJson.Serialize(Build(lines: new[] { withUnit }));

        json.Should().Contain("\"UnitCode\":\"KGM\"", "le code UN/ECE Rec 20 porté est recopié tel quel");
        json.IndexOf("\"SourceData\"", StringComparison.Ordinal)
            .Should().BeLessThan(
                json.IndexOf("\"UnitCode\"", StringComparison.Ordinal),
                "champ ADDITIF émis en FIN de la ligne (ADR-0007)");

        var withoutUnit = new PivotLineDto(description: "ligne", netAmount: 100m);
        PayloadHasher.ComputeHash(Build(lines: new[] { withUnit }))
            .Should().NotBe(
                PayloadHasher.ComputeHash(Build(lines: new[] { withoutUnit })),
                "porter une unité distingue l'empreinte (sensibilité), l'absence la laisse intacte");
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
        string json = CanonicalJson.Serialize(BuildFullyPopulated());
        var root = ContractTests.PivotCanonicalReader.ParseToMap(json);

        AssertAllPropertiesArePresent(root, typeof(PivotDocumentDto));
        var supplier = Child(root, "Supplier");
        AssertAllPropertiesArePresent(supplier, typeof(PivotPartyDto));
        AssertAllPropertiesArePresent(Child(supplier, "Address"), typeof(PivotAddressDto));
        AssertAllPropertiesArePresent(Child(root, "Totals"), typeof(PivotTotalsDto));
        var line = Element(root, "Lines", 0);
        AssertAllPropertiesArePresent(line, typeof(PivotLineDto));
        AssertAllPropertiesArePresent(Element(line, "Taxes", 0), typeof(PivotLineTaxDto));
        AssertAllPropertiesArePresent(Element(root, "CreditNoteRefs", 0), typeof(PivotDocumentRefDto));
        AssertAllPropertiesArePresent(Element(root, "Payments", 0), typeof(PivotPaymentDto));
        AssertAllPropertiesArePresent(Element(root, "DocumentCharges", 0), typeof(PivotDocumentChargeDto));
        AssertAllPropertiesArePresent(Child(root, "InvoicePeriod"), typeof(PivotInvoicePeriodDto));
    }

    private static void AssertAllPropertiesArePresent(IDictionary<string, object?> node, Type dtoType)
    {
        foreach (PropertyInfo property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            node.Keys.Should().Contain(
                property.Name,
                $"la propriété {dtoType.Name}.{property.Name} doit être une clé de SON objet JSON (complétude par-DTO : anti-doublon PIV04 / détection d'altération TRK03)");
        }
    }

    private static IDictionary<string, object?> Child(IDictionary<string, object?> node, string key) =>
        (IDictionary<string, object?>)node[key]!;

    private static IDictionary<string, object?> Element(IDictionary<string, object?> node, string key, int index) =>
        (IDictionary<string, object?>)((List<object?>)node[key]!)[index]!;

    private static PivotDocumentDto BuildFullyPopulated()
    {
        var address = new PivotAddressDto(
            line1: "1 rue de la Paix",
            line2: "Bât B",
            postalCode: "75001",
            city: "Paris",
            countryCode: "FR");

        var supplier = new PivotPartyDto(
            name: "Fournisseur Fictif SA",
            siren: "123456789",
            siret: "12345678900012",
            vatNumber: "FR12345678901",
            address: address,
            email: "contact@fournisseur-fictif.example",
            isCompanyHint: true);

        var customer = new PivotPartyDto(
            name: "Client Fictif SARL",
            siren: "987654321",
            siret: "98765432100099",
            vatNumber: "FR98765432109",
            address: new PivotAddressDto(
                line1: "2 avenue de l'Opéra",
                line2: "Étage 3",
                postalCode: "69001",
                city: "Lyon",
                countryCode: "FR"),
            email: "facturation@client-fictif.example",
            isCompanyHint: true);

        var invoicer = new PivotPartyDto(
            name: "Emetteur Fictif SAS",
            siren: "111222333",
            siret: "11122233300011",
            vatNumber: "FR11122233301",
            address: new PivotAddressDto(
                line1: "3 boulevard Haussmann",
                line2: "Suite 12",
                postalCode: "75009",
                city: "Paris",
                countryCode: "FR"),
            email: "emission@emetteur-fictif.example",
            isCompanyHint: true);

        var payee = new PivotPartyDto(
            name: "Bénéficiaire Fictif SNC",
            siren: "444555666",
            siret: "44455566600044",
            vatNumber: "FR44455566604",
            address: new PivotAddressDto(
                line1: "4 place Vendôme",
                line2: "RDC",
                postalCode: "75001",
                city: "Paris",
                countryCode: "FR"),
            email: "paiement@beneficiaire-fictif.example",
            isCompanyHint: true);

        var totals = new PivotTotalsDto(
            totalNet: 1000.00m,
            totalTax: 200.00m,
            totalGross: 1200.00m,
            sourceTotalGross: 1200.00m);

        var lineTax = new PivotLineTaxDto(
            taxAmount: 200.00m,
            rate: 20.00m,
            categoryCode: VatCategory.S,
            vatexCode: "VATEX-EU-G");

        var line = new PivotLineDto(
            description: "Prestation fictive de test",
            netAmount: 1000.00m,
            quantity: 2m,
            unitPriceNet: 500.00m,
            sourceRegimeCodes: new[] { "TVA_20" },
            taxes: new[] { lineTax },
            sourceLineRef: "LIG-001",
            sourceData: "{\"raw\":\"line\"}",
            unitCode: "C62");

        var creditNoteRef = new PivotDocumentRefDto(
            number: "FA-2026-001",
            issueDate: new DateTime(2026, 1, 15),
            sourceReference: "SRC-FA-001");

        var payment = new PivotPaymentDto(
            paymentDate: new DateTime(2026, 2, 1),
            amount: 1200.00m,
            method: "virement",
            relatedDocumentNumber: "FA-2026-001",
            sourceReference: "PAY-001");

        var documentCharge = new PivotDocumentChargeDto(
            isCharge: true,
            amount: 10.00m,
            reason: "éco-contribution",
            reasonCode: "ECO",
            sourceRegimeCodes: new[] { "ECO_CONTRIB" });

        return new PivotDocumentDto(
            sourceDocumentKind: "FA",
            number: "FA-2026-TEST",
            issueDate: new DateTime(2026, 3, 1),
            sourceReference: "SRC-2026-TEST",
            supplier: supplier,
            totals: totals,
            operationCategory: OperationCategory.PrestationServices,
            currencyCode: "EUR",
            customer: customer,
            lines: new[] { line },
            creditNoteRefs: new[] { creditNoteRef },
            payments: new[] { payment },
            documentCharges: new[] { documentCharge },
            invoicer: invoicer,
            payee: payee,
            isSelfBilled: true,
            prepaidAmount: 100.00m,
            sourceData: "{\"raw\":\"doc\"}",
            paymentDueDate: new DateTime(2026, 3, 31),
            invoicePeriod: new PivotInvoicePeriodDto(
                startDate: new DateTime(2026, 1, 1),
                endDate: new DateTime(2026, 1, 31)));
    }

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
