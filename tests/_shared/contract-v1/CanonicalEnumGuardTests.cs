namespace Liakont.Agent.Contracts.ContractTests;

using System;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Xunit;

/// <summary>
/// Garde CROSS-RUNTIME (RDL01) de la sérialisation canonique des énumérations du contrat. Ce fichier
/// est LIÉ dans les deux projets de test — plateforme (.NET 10) ET agent (net48) — et exécuté des deux
/// côtés (acceptance RDL01 : « tests des deux côtés »). Prouve que :
/// <list type="bullet">
/// <item>une valeur d'enum DÉFINIE est émise par son NOM (règle 4 d'<c>ADR-0007</c>) ;</item>
/// <item>une valeur d'enum NON DÉFINIE LÈVE à la sérialisation (rejet) au lieu d'émettre le NOMBRE muet
/// que produisait l'ancien <c>ToString()</c> — qui aurait été hashé puis archivé (WORM) ;</item>
/// <item>la garde tient AUX DEUX sites du contrat (<see cref="PivotDocumentDto.OperationCategory"/> et
/// <see cref="PivotLineTaxDto.CategoryCode"/>) via <c>CanonicalJson.Serialize</c>.</item>
/// </list>
/// « Bloquer plutôt qu'envoyer faux » (CLAUDE.md n°3).
/// </summary>
public sealed class CanonicalEnumGuardTests
{
    [Theory]
    [InlineData(OperationCategory.LivraisonBiens, "LivraisonBiens")]
    [InlineData(OperationCategory.PrestationServices, "PrestationServices")]
    [InlineData(OperationCategory.Mixte, "Mixte")]
    public void WriteEnum_emits_defined_operation_category_by_name(OperationCategory value, string expectedName)
    {
        var writer = new CanonicalJsonWriter();

        writer.WriteEnum(value);

        writer.ToString().Should().Be("\"" + expectedName + "\"", "une valeur définie est émise par son nom (ADR-0007 règle 4)");
    }

    [Theory]
    [InlineData(VatCategory.S, "S")]
    [InlineData(VatCategory.E, "E")]
    [InlineData(VatCategory.AE, "AE")]
    public void WriteEnum_emits_defined_vat_category_by_name(VatCategory value, string expectedName)
    {
        var writer = new CanonicalJsonWriter();

        writer.WriteEnum(value);

        writer.ToString().Should().Be("\"" + expectedName + "\"");
    }

    [Fact]
    public void WriteEnum_throws_on_undefined_operation_category()
    {
        var writer = new CanonicalJsonWriter();

        Action write = () => writer.WriteEnum((OperationCategory)99);

        write.Should().Throw<ArgumentOutOfRangeException>(
            "une valeur hors plage doit LEVER, jamais émettre le nombre « 99 » (RDL01)");
    }

    [Fact]
    public void WriteEnum_throws_on_undefined_vat_category()
    {
        var writer = new CanonicalJsonWriter();

        Action write = () => writer.WriteEnum((VatCategory)99);

        write.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Serialize_emits_defined_enums_by_name()
    {
        PivotDocumentDto document = BuildDocument(
            operationCategory: OperationCategory.Mixte,
            lineCategory: VatCategory.E);

        string json = CanonicalJson.Serialize(document);

        json.Should().Contain("\"OperationCategory\":\"Mixte\"");
        json.Should().Contain("\"CategoryCode\":\"E\"");
        json.Should().NotMatchRegex("\"OperationCategory\":[0-9]", "jamais la valeur numérique de l'enum");
    }

    [Fact]
    public void Serialize_throws_when_operation_category_is_undefined()
    {
        PivotDocumentDto document = BuildDocument(
            operationCategory: (OperationCategory)99,
            lineCategory: VatCategory.S);

        Action serialize = () => CanonicalJson.Serialize(document);

        serialize.Should().Throw<ArgumentOutOfRangeException>(
            "une catégorie d'opération hors plage est REJETÉE à la sérialisation, jamais hashée en nombre muet (RDL01)");
    }

    [Fact]
    public void Serialize_throws_when_line_tax_category_is_undefined()
    {
        PivotDocumentDto document = BuildDocument(
            operationCategory: OperationCategory.LivraisonBiens,
            lineCategory: (VatCategory)99);

        Action serialize = () => CanonicalJson.Serialize(document);

        serialize.Should().Throw<ArgumentOutOfRangeException>(
            "une catégorie de TVA hors plage est REJETÉE à la sérialisation (RDL01)");
    }

    private static PivotDocumentDto BuildDocument(OperationCategory operationCategory, VatCategory lineCategory)
    {
        var lineTax = new PivotLineTaxDto(taxAmount: 0m, categoryCode: lineCategory);
        var line = new PivotLineDto(
            description: "Ligne fictive",
            netAmount: 0m,
            taxes: new[] { lineTax });

        return new PivotDocumentDto(
            sourceDocumentKind: "B",
            number: "DOC-1",
            issueDate: new DateTime(2026, 1, 1),
            sourceReference: "ref",
            supplier: new PivotPartyDto("Fournisseur Fictif"),
            totals: new PivotTotalsDto(0m, 0m, 0m),
            operationCategory: operationCategory,
            lines: new[] { line });
    }
}
