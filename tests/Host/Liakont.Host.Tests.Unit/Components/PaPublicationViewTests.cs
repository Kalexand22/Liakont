namespace Liakont.Host.Tests.Unit.Components;

using System;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.PaAccounts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la vue PURE « Publication du SIREN / transmission » (FIX201) : état publié / non publié /
/// indisponible / sans compte actif, ouverture du formulaire (SIREN lu du profil, en lecture seule), et
/// déclenchement du callback de publication. Aucune injection métier — le wiring page ↔ service est couvert
/// par les tests de service.
/// </summary>
public sealed class PaPublicationViewTests : BunitContext
{
    public PaPublicationViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void No_active_account_shows_the_no_account_message()
    {
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState { HasActiveAccount = false, StateAvailable = false })
            .Add(v => v.Form, new PaPublicationFormModel()));

        cut.FindAll("[data-testid='pa-publication-no-account']").Should().ContainSingle();
        cut.FindAll("[data-testid='pa-publication-open-btn']").Should().BeEmpty("rien à publier sans compte actif");
    }

    [Fact]
    public void Unpublished_state_shows_the_unpublished_badge_and_the_open_button()
    {
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = true,
                IsPublished = false,
            })
            .Add(v => v.Form, new PaPublicationFormModel()));

        cut.FindAll("[data-testid='pa-publication-unpublished']").Should().ContainSingle();
        cut.FindAll("[data-testid='pa-publication-open-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Published_state_shows_the_published_date()
    {
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = true,
                IsPublished = true,
                StartDate = new DateOnly(2026, 1, 1),
            })
            .Add(v => v.Form, new PaPublicationFormModel()));

        var published = cut.Find("[data-testid='pa-publication-published']");
        published.TextContent.Should().Contain("01/01/2026");
    }

    [Fact]
    public void Scheduled_state_shows_a_future_activation_date()
    {
        // StartDate renseignée mais non « publié » (IsPublished = false) ⇒ date future : programmé.
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = true,
                IsPublished = false,
                StartDate = new DateOnly(2026, 9, 1),
            })
            .Add(v => v.Form, new PaPublicationFormModel()));

        var scheduled = cut.Find("[data-testid='pa-publication-scheduled']");
        scheduled.TextContent.Should().Contain("01/09/2026");
        cut.FindAll("[data-testid='pa-publication-published']").Should().BeEmpty();
        cut.FindAll("[data-testid='pa-publication-unpublished']").Should().BeEmpty();
    }

    [Fact]
    public void Unavailable_state_shows_a_degraded_message()
    {
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = false,
            })
            .Add(v => v.Form, new PaPublicationFormModel()));

        cut.FindAll("[data-testid='pa-publication-unavailable']").Should().ContainSingle();
    }

    [Fact]
    public void Open_form_shows_the_siren_read_only_and_the_fields()
    {
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = true,
                IsPublished = false,
                Siren = "123456782",
            })
            .Add(v => v.Form, new PaPublicationFormModel { StartDate = new DateOnly(2026, 1, 1) })
            .Add(v => v.FormOpen, true));

        var siren = cut.Find("[data-testid='pa-publication-siren']");
        siren.HasAttribute("disabled").Should().BeTrue("le SIREN vient du profil, jamais saisi");
        siren.GetAttribute("value").Should().Be("123456782");
        cut.FindAll("[data-testid='pa-publication-startdate']").Should().ContainSingle();
        cut.FindAll("[data-testid='pa-publication-typeoperation']").Should().ContainSingle();
        cut.FindAll("[data-testid='pa-publication-enterprisesize']").Should().ContainSingle();
    }

    [Fact]
    public void Fields_required_when_the_pa_consumes_them_submit_disabled_while_empty()
    {
        // BUG-13 : PA qui CONSOMME les champs (RequiresTaxReportSettingFields = true) ⇒ saisie obligatoire,
        // bouton « Publier » désactivé tant qu'ils sont vides.
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "B2Brouter",
                Environment = "Staging",
                StateAvailable = true,
                Siren = "123456782",
                RequiresTaxReportSettingFields = true,
            })
            .Add(v => v.Form, new PaPublicationFormModel { StartDate = new DateOnly(2026, 1, 1) })
            .Add(v => v.FormOpen, true));

        cut.Find("[data-testid='pa-publication-submit-btn']").HasAttribute("disabled")
            .Should().BeTrue("une PA qui consomme ces champs exige leur saisie");
    }

    [Fact]
    public void Fields_optional_when_the_pa_ignores_them_submit_enabled_while_empty()
    {
        // BUG-13 : PA qui IGNORE les champs (RequiresTaxReportSettingFields = false, ex. Super PDP) ⇒ saisie
        // facultative, bouton « Publier » actif même champs vides (CLAUDE.md n°8 : ne pas imposer un champ sans effet).
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "SuperPdp",
                Environment = "Staging",
                StateAvailable = true,
                Siren = "000000002",
                RequiresTaxReportSettingFields = false,
            })
            .Add(v => v.Form, new PaPublicationFormModel { StartDate = new DateOnly(2026, 1, 1) })
            .Add(v => v.FormOpen, true));

        cut.Find("[data-testid='pa-publication-submit-btn']").HasAttribute("disabled")
            .Should().BeFalse("une PA qui ignore ces champs n'impose pas leur saisie");
    }

    [Fact]
    public void Submit_invokes_the_callback_when_the_form_is_complete()
    {
        var submitted = false;
        var cut = Render<PaPublicationView>(p => p
            .Add(v => v.State, new PaPublicationState
            {
                HasActiveAccount = true,
                PluginType = "Fake",
                Environment = "Staging",
                StateAvailable = true,
                Siren = "123456782",
            })
            .Add(v => v.Form, new PaPublicationFormModel
            {
                StartDate = new DateOnly(2026, 1, 1),
                TypeOperation = "LBS",
                EnterpriseSize = "PME",
            })
            .Add(v => v.FormOpen, true)
            .Add(v => v.OnSubmit, () => submitted = true));

        cut.Find("[data-testid='pa-publication-submit-btn']").Click();

        submitted.Should().BeTrue();
    }
}
