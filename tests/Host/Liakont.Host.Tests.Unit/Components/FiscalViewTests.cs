namespace Liakont.Host.Tests.Unit.Components;

using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Fiscal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Rendu PUR de l'écran « Paramétrage › Fiscal » (FIX301) : les 4 champs du contrat éditables, les listes
/// déroulantes n'offrant QUE les valeurs admises (listes fermées + « non renseigné »), pré-remplissage et
/// enregistrement câblé vers le callback. Aucune valeur inventée, aucun défaut.
/// </summary>
public sealed class FiscalViewTests : BunitContext
{
    public FiscalViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_The_Four_Contract_Fields()
    {
        var cut = Render<FiscalView>(p => p.Add(v => v.Model, Model()));

        cut.FindAll("[data-testid='fiscal-vat-on-debits']").Should().ContainSingle();
        cut.FindAll("[data-testid='fiscal-operation-category']").Should().ContainSingle();
        cut.FindAll("[data-testid='fiscal-fee-imputation']").Should().ContainSingle();
        cut.FindAll("[data-testid='fiscal-reporting-frequency']").Should().ContainSingle();
    }

    [Fact]
    public void Operation_Category_Offers_Only_The_Closed_List_Plus_Pending()
    {
        var cut = Render<FiscalView>(p => p.Add(v => v.Model, Model()));

        var values = cut.FindAll("[data-testid='fiscal-operation-category'] option")
            .Select(o => o.GetAttribute("value"))
            .ToArray();

        // Liste fermée = exactement les valeurs du contrat + le choix « non renseigné » (chaîne vide).
        // Aucune valeur inventée (CLAUDE.md n°2).
        values.Should().Equal(string.Empty, "LivraisonBiens", "PrestationServices", "Mixte");
    }

    [Fact]
    public void Fee_Imputation_Offers_Only_The_Closed_List_Plus_Pending()
    {
        var cut = Render<FiscalView>(p => p.Add(v => v.Model, Model()));

        var values = cut.FindAll("[data-testid='fiscal-fee-imputation'] option")
            .Select(o => o.GetAttribute("value"))
            .ToArray();

        values.Should().Equal(string.Empty, "Prorata", "AgregationJourTaux");
    }

    [Fact]
    public void Vat_On_Debits_Is_A_Tri_State()
    {
        var cut = Render<FiscalView>(p => p.Add(v => v.Model, Model()));

        var values = cut.FindAll("[data-testid='fiscal-vat-on-debits'] option")
            .Select(o => o.GetAttribute("value"))
            .ToArray();

        values.Should().Equal(string.Empty, "true", "false");
    }

    [Fact]
    public void Prefills_The_Current_Settings()
    {
        var model = Model(new FiscalFormModel
        {
            VatOnDebits = "false",
            OperationCategory = "PrestationServices",
            FeeImputationMethod = "Prorata",
            ReportingFrequency = "mensuelle",
        });

        var cut = Render<FiscalView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='fiscal-operation-category']").GetAttribute("value").Should().Be("PrestationServices");
        cut.Find("[data-testid='fiscal-reporting-frequency']").GetAttribute("value").Should().Be("mensuelle");
    }

    [Fact]
    public void Saving_Invokes_The_Callback()
    {
        var saved = false;
        var cut = Render<FiscalView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.OnSave, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='fiscal-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Saving_An_Entirely_Unset_Form_Still_Invokes_The_Callback()
    {
        // « Non renseigné » partout (null = suspension conservée) reste un état enregistrable : l'opérateur
        // peut effacer une décision. Aucun champ obligatoire imposé par l'écran (jamais de défaut).
        var saved = false;
        var cut = Render<FiscalView>(p => p
            .Add(v => v.Model, Model(new FiscalFormModel
            {
                VatOnDebits = string.Empty,
                OperationCategory = string.Empty,
                FeeImputationMethod = string.Empty,
                ReportingFrequency = string.Empty,
            }))
            .Add(v => v.OnSave, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='fiscal-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Shows_The_Feedback_Message_When_Provided()
    {
        var cut = Render<FiscalView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.Message, "Valeur de paramètre fiscal non reconnue.")
            .Add(v => v.IsError, true));

        cut.Find("[data-testid='fiscal-feedback']").TextContent.Should().Contain("non reconnue");
    }

    private static FiscalViewModel Model(FiscalFormModel? form = null) => new()
    {
        Form = form ?? new FiscalFormModel
        {
            VatOnDebits = string.Empty,
            OperationCategory = string.Empty,
            FeeImputationMethod = string.Empty,
            ReportingFrequency = string.Empty,
        },
        OperationCategoryOptions = FiscalSettingsOptions.OperationCategories,
        FeeImputationMethodOptions = FiscalSettingsOptions.FeeImputationMethods,
    };
}
