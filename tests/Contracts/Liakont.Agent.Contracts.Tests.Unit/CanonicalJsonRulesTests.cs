namespace Liakont.Agent.Contracts.Tests.Unit;

using System;
using System.Linq;
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
    public void Serialize_rejects_a_null_document()
    {
        Action act = () => CanonicalJson.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static PivotDocumentDto Build(
        string number = "AV-1",
        DateTime? issueDate = null,
        PivotPartyDto? supplier = null,
        PivotPartyDto? customer = null,
        PivotTotalsDto? totals = null,
        OperationCategory category = OperationCategory.LivraisonBiens,
        PivotLineDto[]? lines = null,
        decimal? prepaid = null)
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
            prepaidAmount: prepaid);
    }
}
