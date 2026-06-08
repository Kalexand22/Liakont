namespace Liakont.Host.Tests.Unit.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.TvaMappingTable;
using Liakont.Modules.TvaMapping.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit du formulaire d'édition d'une règle de mapping TVA (WEB07b) : listes FERMÉES (catégorie,
/// part, mode de taux, VATEX rendus en &lt;select&gt; — AUCUNE saisie libre), clé (régime + part) figée
/// en modification, apparition du taux fixe selon le mode, garde de présence avant enregistrement, et
/// callbacks enregistrer / annuler. La vue ne porte aucune logique fiscale.
/// </summary>
public sealed class TvaRuleEditorTests : BunitContext
{
    public TvaRuleEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Closed_lists_are_selects_with_only_the_allowed_options_no_free_text()
    {
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, new TvaRuleFormModel())
            .Add(e => e.IsCreate, true));

        // Catégorie : un <select> (jamais un <input> de saisie libre), avec les codes admis + le placeholder.
        var category = cut.Find("[data-testid='tva-rule-category']");
        category.NodeName.Should().Be("SELECT");
        category.QuerySelectorAll("option").Should().HaveCount(Options().Categories.Count + 1);

        // VATEX et part sont eux aussi des <select> fermés.
        cut.Find("[data-testid='tva-rule-vatex']").NodeName.Should().Be("SELECT");
        cut.Find("[data-testid='tva-rule-part']").NodeName.Should().Be("SELECT");
        cut.Find("[data-testid='tva-rule-ratemode']").NodeName.Should().Be("SELECT");
    }

    [Fact]
    public void Create_mode_allows_editing_the_key_fields()
    {
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, new TvaRuleFormModel())
            .Add(e => e.IsCreate, true));

        cut.Find("[data-testid='tva-rule-code']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='tva-rule-part']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Edit_mode_freezes_the_key_fields()
    {
        var model = new TvaRuleFormModel { SourceRegimeCode = "20", Part = "Adjudication", Category = "S", RateMode = "Fixed", RateValue = 20m };
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, false));

        // La clé (régime, part) identifie la règle : non modifiable en édition (supprimer puis recréer).
        cut.Find("[data-testid='tva-rule-code']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='tva-rule-part']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Fixed_rate_field_appears_only_for_fixed_mode_and_clears_on_computed()
    {
        var model = new TvaRuleFormModel();
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, true));

        // Mode non choisi : pas de champ taux.
        cut.FindAll("[data-testid='tva-rule-rate']").Should().BeEmpty();

        // Mode Fixed : le champ taux apparaît.
        cut.Find("[data-testid='tva-rule-ratemode']").Change("Fixed");
        cut.FindAll("[data-testid='tva-rule-rate']").Should().ContainSingle();

        // Bascule en Calculé : le champ disparaît et la valeur est effacée (jamais un taux deviné).
        cut.Find("[data-testid='tva-rule-rate']").Change("20");
        cut.Find("[data-testid='tva-rule-ratemode']").Change("ComputedFromSource");
        cut.FindAll("[data-testid='tva-rule-rate']").Should().BeEmpty();
        model.RateValue.Should().BeNull();
    }

    [Fact]
    public void Save_is_disabled_until_required_fields_are_present()
    {
        var model = new TvaRuleFormModel();
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, true));

        cut.Find("[data-testid='tva-rule-save-btn']").HasAttribute("disabled").Should().BeTrue();

        cut.Find("[data-testid='tva-rule-code']").Input("6");
        cut.Find("[data-testid='tva-rule-part']").Change("Adjudication");
        cut.Find("[data-testid='tva-rule-category']").Change("E");
        cut.Find("[data-testid='tva-rule-ratemode']").Change("ComputedFromSource");

        // Tous les champs requis présents (catégorie E + mode calculé : pas de taux fixe exigé côté UI).
        cut.Find("[data-testid='tva-rule-save-btn']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Fixed_mode_requires_a_rate_value_before_save()
    {
        var model = new TvaRuleFormModel();
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, true));

        cut.Find("[data-testid='tva-rule-code']").Input("20");
        cut.Find("[data-testid='tva-rule-part']").Change("Adjudication");
        cut.Find("[data-testid='tva-rule-category']").Change("S");
        cut.Find("[data-testid='tva-rule-ratemode']").Change("Fixed");

        // Mode Fixed sans valeur de taux : enregistrement bloqué (présence requise).
        cut.Find("[data-testid='tva-rule-save-btn']").HasAttribute("disabled").Should().BeTrue();

        cut.Find("[data-testid='tva-rule-rate']").Change("20");
        cut.Find("[data-testid='tva-rule-save-btn']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Submitting_invokes_the_submit_callback()
    {
        var submitted = false;
        var model = new TvaRuleFormModel { SourceRegimeCode = "6", Part = "Adjudication", Category = "E", RateMode = "ComputedFromSource" };
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, true)
            .Add(e => e.OnSubmit, () => { submitted = true; }));

        cut.Find("[data-testid='tva-rule-save-btn']").Click();

        submitted.Should().BeTrue();
    }

    [Fact]
    public void Cancelling_invokes_the_cancel_callback()
    {
        var cancelled = false;
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, new TvaRuleFormModel())
            .Add(e => e.IsCreate, true)
            .Add(e => e.OnCancel, () => { cancelled = true; }));

        cut.Find("[data-testid='tva-rule-cancel-btn']").Click();

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Existing_source_flags_are_shown_read_only()
    {
        var model = new TvaRuleFormModel
        {
            SourceRegimeCode = "6",
            Part = "Adjudication",
            Category = "E",
            RateMode = "ComputedFromSource",
            SourceFlags = new Dictionary<string, string> { ["RegimeMarge"] = "true" },
        };
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, model)
            .Add(e => e.IsCreate, false));

        cut.Find("[data-testid='tva-rule-flags']").TextContent.Should().Contain("RegimeMarge = true");
    }

    [Fact]
    public void Editor_error_is_displayed()
    {
        const string Error = "règle #1 : catégorie E (exonéré) sans code VATEX — un motif d'exonération est obligatoire (F03 §2.2)";
        var cut = Render<TvaRuleEditor>(p => p
            .Add(e => e.Options, Options())
            .Add(e => e.Model, new TvaRuleFormModel())
            .Add(e => e.IsCreate, true)
            .Add(e => e.Error, Error));

        cut.Find("[data-testid='tva-rule-editor-error']").TextContent.Should().Contain("sans code VATEX");
    }

    private static TvaMappingEditOptionsDto Options() => new()
    {
        Categories =
        [
            new TvaMappingOptionDto("S", "Taux normal"),
            new TvaMappingOptionDto("E", "Exonéré (motif VATEX requis)"),
            new TvaMappingOptionDto("O", "Hors champ d'application de la TVA"),
        ],
        Parts =
        [
            new TvaMappingOptionDto("Adjudication", "Adjudication (le bien vendu)"),
            new TvaMappingOptionDto("Frais", "Frais"),
            new TvaMappingOptionDto("Autre", "Autre"),
        ],
        RateModes =
        [
            new TvaMappingOptionDto("Fixed", "Taux fixe"),
            new TvaMappingOptionDto("ComputedFromSource", "Calculé depuis la source"),
        ],
        VatexCodes =
        [
            new TvaMappingOptionDto("VATEX-EU-J", "VATEX-EU-J — Objets de collection"),
            new TvaMappingOptionDto("VATEX-EU-O", "VATEX-EU-O — Non soumis à la TVA"),
        ],
    };
}
