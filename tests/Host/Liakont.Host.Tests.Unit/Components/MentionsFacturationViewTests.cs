namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.BillingMentions;
using Liakont.Host.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Rendu PUR de l'écran « Paramétrage › Mentions de facturation » (BUG-26, F12-A §3.4) : les 4 champs de
/// texte libre du contrat éditables, pré-remplissage et enregistrement câblé vers le callback. Aucun contenu
/// inventé, aucun défaut.
/// </summary>
public sealed class MentionsFacturationViewTests : BunitContext
{
    public MentionsFacturationViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_The_Four_Contract_Fields()
    {
        var cut = Render<MentionsFacturationView>(p => p.Add(v => v.Model, Model()));

        cut.FindAll("[data-testid='mentions-payment-terms']").Should().ContainSingle();
        cut.FindAll("[data-testid='mentions-late-penalty-terms']").Should().ContainSingle();
        cut.FindAll("[data-testid='mentions-recovery-fee-terms']").Should().ContainSingle();
        cut.FindAll("[data-testid='mentions-discount-terms']").Should().ContainSingle();
    }

    [Fact]
    public void Prefills_The_Current_Mentions()
    {
        var model = Model(new BillingMentionsFormModel
        {
            PaymentTerms = "Paiement comptant exigible à la vente",
            LatePenaltyTerms = "Pénalités de retard au taux légal",
            RecoveryFeeTerms = "Indemnité forfaitaire de 40 €",
            DiscountTerms = "Pas d'escompte pour paiement anticipé",
        });

        var cut = Render<MentionsFacturationView>(p => p.Add(v => v.Model, model));

        // Blazor rend la valeur liée d'un <textarea> dans l'attribut value (et non en contenu textuel).
        cut.Find("[data-testid='mentions-payment-terms']").GetAttribute("value").Should().Contain("Paiement comptant");
        cut.Find("[data-testid='mentions-discount-terms']").GetAttribute("value").Should().Contain("Pas d'escompte");
    }

    [Fact]
    public void Saving_Invokes_The_Callback()
    {
        var saved = false;
        var cut = Render<MentionsFacturationView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.OnSave, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='mentions-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Saving_An_Entirely_Unset_Form_Still_Invokes_The_Callback()
    {
        // « Non renseigné » partout (null = mention non renseignée) reste un état enregistrable : l'opérateur
        // peut effacer une mention. Aucun champ obligatoire imposé par l'écran (jamais de défaut).
        var saved = false;
        var cut = Render<MentionsFacturationView>(p => p
            .Add(v => v.Model, Model(new BillingMentionsFormModel
            {
                PaymentTerms = string.Empty,
                LatePenaltyTerms = string.Empty,
                RecoveryFeeTerms = string.Empty,
                DiscountTerms = string.Empty,
            }))
            .Add(v => v.OnSave, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='mentions-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Shows_The_Feedback_Message_When_Provided()
    {
        var cut = Render<MentionsFacturationView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.Message, "L'enregistrement des mentions de facturation a échoué.")
            .Add(v => v.IsError, true));

        cut.Find("[data-testid='mentions-feedback']").TextContent.Should().Contain("a échoué");
    }

    private static BillingMentionsViewModel Model(BillingMentionsFormModel? form = null) => new()
    {
        Form = form ?? new BillingMentionsFormModel
        {
            PaymentTerms = string.Empty,
            LatePenaltyTerms = string.Empty,
            RecoveryFeeTerms = string.Empty,
            DiscountTerms = string.Empty,
        },
    };
}
