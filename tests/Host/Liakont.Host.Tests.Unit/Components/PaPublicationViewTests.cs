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
