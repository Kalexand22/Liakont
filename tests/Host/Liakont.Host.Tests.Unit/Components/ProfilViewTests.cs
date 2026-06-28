namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Profil;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la vue PURE d'édition du profil légal (BUG-15) : le SIREN est rendu en LECTURE SEULE (clé
/// fonctionnelle immuable, INV-TENANTSETTINGS-001) ; la raison sociale, l'adresse et le contact sont éditables ;
/// la garde de présence désactive l'enregistrement tant qu'un champ obligatoire est vide ; le callback OnSave
/// est déclenché. La vue ne porte aucune logique métier (CLAUDE.md n°19).
/// </summary>
public sealed class ProfilViewTests : BunitContext
{
    public ProfilViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Siren_field_is_rendered_read_only_with_its_value()
    {
        var cut = Render<ProfilView>(p => p.Add(v => v.Model, Model()));

        var siren = cut.Find("[data-testid='profil-siren']");
        siren.HasAttribute("disabled").Should().BeTrue("le SIREN est la clé fonctionnelle immuable du tenant");
        siren.GetAttribute("value").Should().Be("123456782");
    }

    [Fact]
    public void Editable_fields_are_prefilled_from_the_model()
    {
        var cut = Render<ProfilView>(p => p.Add(v => v.Model, Model()));

        cut.Find("[data-testid='profil-raison-sociale']").GetAttribute("value").Should().Be("Étude des Enchères");
        cut.Find("[data-testid='profil-street']").GetAttribute("value").Should().Be("1 rue de l'Exemple");
        cut.Find("[data-testid='profil-postal-code']").GetAttribute("value").Should().Be("35000");
        cut.Find("[data-testid='profil-city']").GetAttribute("value").Should().Be("Rennes");
        cut.Find("[data-testid='profil-country']").GetAttribute("value").Should().Be("FR");
        cut.Find("[data-testid='profil-contact']").GetAttribute("value").Should().Be("alerte@exemple.fr");

        // Les champs éditables ne sont PAS désactivés (hors enregistrement en cours).
        cut.Find("[data-testid='profil-raison-sociale']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Save_is_disabled_when_a_required_field_is_blank()
    {
        var model = Model();
        model.Form.City = string.Empty;

        var cut = Render<ProfilView>(p => p.Add(v => v.Model, model));

        cut.Find("[data-testid='profil-save-btn']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Save_is_enabled_and_invokes_the_callback_when_required_fields_are_present()
    {
        var saved = false;
        var cut = Render<ProfilView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.OnSave, () => { saved = true; }));

        cut.Find("[data-testid='profil-save-btn']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='profil-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Feedback_message_is_displayed_when_provided()
    {
        var cut = Render<ProfilView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.Message, "INV-TENANTSETTINGS-001 : le SIREN ne peut pas être modifié.")
            .Add(v => v.IsError, true));

        cut.Find("[data-testid='profil-feedback']").TextContent.Should().Contain("le SIREN ne peut pas être modifié");
    }

    private static ProfilViewModel Model() => new()
    {
        Siren = "123456782",
        Form = new ProfilFormModel
        {
            RaisonSociale = "Étude des Enchères",
            Street = "1 rue de l'Exemple",
            PostalCode = "35000",
            City = "Rennes",
            Country = "FR",
            ContactEmailAlerte = "alerte@exemple.fr",
        },
    };
}
