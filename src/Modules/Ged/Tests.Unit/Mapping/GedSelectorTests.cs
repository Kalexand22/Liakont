namespace Liakont.Modules.Ged.Tests.Unit.Mapping;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Liakont.Agent.Contracts.Ged;
using Liakont.Modules.Ged.Domain.Mapping;
using Xunit;

/// <summary>
/// Tests unitaires du sélecteur JSONPath RESTREINT (F19 §4.5, GED12) : chemins simples + filtre d'égalité +
/// joker, sur un <see cref="IngestedDocumentDto"/> BRUT. Prouve la résolution (0..n valeurs) et le REFUS d'une
/// syntaxe invalide à la validation (jamais deviner l'intention, règle 3).
/// </summary>
public sealed class GedSelectorTests
{
    [Fact]
    public void Field_selector_returns_the_matching_source_field()
    {
        var doc = Build(fields: new Dictionary<string, string> { ["date_champ"] = "2026-03-15" });

        GedSelector.Evaluate("$.fields.date_champ", doc).Should().ContainSingle().Which.Should().Be("2026-03-15");
    }

    [Fact]
    public void Missing_field_returns_no_value()
    {
        var doc = Build(fields: new Dictionary<string, string> { ["autre"] = "x" });

        GedSelector.Evaluate("$.fields.absent", doc).Should().BeEmpty();
    }

    [Fact]
    public void Document_type_selector_returns_the_raw_type()
    {
        var doc = Build(documentType: "typ_a");

        GedSelector.Evaluate("$.documentType", doc).Should().ContainSingle().Which.Should().Be("typ_a");
    }

    [Fact]
    public void Axis_wildcard_selector_returns_all_values_of_the_matching_axis()
    {
        var doc = Build(axes: new[] { new RawAxisHint("refs", new List<string> { "L1", "L2", "L3" }) });

        GedSelector.Evaluate("$.axes[?name=='refs'].values[*]", doc)
            .Should().Equal("L1", "L2", "L3");
    }

    [Fact]
    public void Axis_selector_without_wildcard_flattens_the_values_array()
    {
        var doc = Build(axes: new[] { new RawAxisHint("refs", new List<string> { "L1", "L2" }) });

        GedSelector.Evaluate("$.axes[?name=='refs'].values", doc).Should().Equal("L1", "L2");
    }

    [Fact]
    public void Entity_filter_selects_only_the_matching_type_external_ids()
    {
        var doc = Build(entities: new[]
        {
            new RawEntityHint("partenaire", "E-7", "Sept"),
            new RawEntityHint("autre", "X-1", "Un"),
            new RawEntityHint("partenaire", "E-9", "Neuf"),
        });

        GedSelector.Evaluate("$.entities[?type=='partenaire'].externalId", doc).Should().Equal("E-7", "E-9");
    }

    [Fact]
    public void Entity_display_selector_skips_null_display()
    {
        var doc = Build(entities: new[] { new RawEntityHint("partenaire", "E-7", display: null) });

        GedSelector.Evaluate("$.entities[?type=='partenaire'].display", doc).Should().BeEmpty();
    }

    [Fact]
    public void Filter_with_no_match_returns_no_value()
    {
        var doc = Build(entities: new[] { new RawEntityHint("partenaire", "E-7", "Sept") });

        GedSelector.Evaluate("$.entities[?type=='inexistant'].externalId", doc).Should().BeEmpty();
    }

    [Fact]
    public void Bracketed_key_selector_targets_a_field_with_spaces_or_accents()
    {
        // Les clés de SourceFields sont BRUTES (espaces, accents) — non exprimables par « .ident ».
        var doc = Build(fields: new Dictionary<string, string> { ["Réf facture"] = "F-1" });

        GedSelector.Evaluate("$.fields['Réf facture']", doc).Should().ContainSingle().Which.Should().Be("F-1");
    }

    [Fact]
    public void Filter_literal_supports_an_escaped_apostrophe()
    {
        var doc = Build(entities: new[] { new RawEntityHint("l'établissement", "E-1", "Un") });

        GedSelector.Evaluate("$.entities[?type=='l''établissement'].externalId", doc)
            .Should().ContainSingle().Which.Should().Be("E-1");
    }

    [Theory]
    [InlineData("fields.x")] // absence de $
    [InlineData("$.fields.")] // propriété vide
    [InlineData("$.axes[?name==]")] // littéral manquant
    [InlineData("$.axes[?name=='x'")] // crochet non fermé
    [InlineData("$.axes[bad]")] // contenu de crochet invalide
    [InlineData("$fields")] // séparateur manquant
    [InlineData("$.fields['non terminé")] // clé entre crochets non terminée
    public void Malformed_selector_is_rejected_at_validation(string selector)
    {
        var act = () => GedSelector.Validate(selector);

        act.Should().Throw<InvalidGedSelectorException>();
    }

    [Fact]
    public void Valid_selector_passes_validation()
    {
        var act = () => GedSelector.Validate("$.entities[?type=='t'].externalId");

        act.Should().NotThrow();
    }

    private static IngestedDocumentDto Build(
        string documentType = "typ_a",
        IReadOnlyDictionary<string, string>? fields = null,
        IReadOnlyList<RawAxisHint>? axes = null,
        IReadOnlyList<RawEntityHint>? entities = null,
        IReadOnlyList<RawRelationHint>? relations = null)
    {
        return new IngestedDocumentDto(
            sourceReference: "ref-" + Guid.NewGuid().ToString("N"),
            documentType: documentType,
            sourceFields: fields,
            sourceAxes: axes,
            sourceEntities: entities,
            sourceRelations: relations);
    }
}
