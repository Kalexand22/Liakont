namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.PaAccounts;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Tests bUnit de la vue PURE « Comptes plateforme agréée » (FIX01c) : rendu des comptes (clé toujours
/// masquée), listes FERMÉES en création (type de plug-in depuis le registre, environnement), champ clé en
/// type <c>password</c> jamais pré-rempli, type figé en édition, message « aucun plug-in » sur registre
/// vide, et confirmation de désactivation. Aucune injection métier — wiring page ↔ service couvert ailleurs.
/// </summary>
public sealed class ComptesPaViewTests : BunitContext
{
    public ComptesPaViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_accounts_with_masked_key_and_never_the_secret()
    {
        var model = Model([Account(hasApiKey: true)], ["Fake"]);

        var cut = Render<ComptesPaView>(p => p.Add(v => v.Model, model));

        var account = cut.Find("[data-testid='comptes-pa-account']");
        account.TextContent.Should().Contain("Fake — Staging");
        var secret = cut.Find("[data-testid='comptes-pa-secret']");
        secret.TextContent.Should().Contain("configurée (masquée)");
        secret.TextContent.Should().NotContain("SECRET");
    }

    [Fact]
    public void Empty_shows_a_no_account_message_and_the_add_button()
    {
        var cut = Render<ComptesPaView>(p => p.Add(v => v.Model, Model([], ["Fake"])));

        cut.FindAll("[data-testid='comptes-pa-none']").Should().ContainSingle();
        cut.FindAll("[data-testid='comptes-pa-add-btn']").Should().ContainSingle();
    }

    [Fact]
    public void Renders_capability_details_when_the_plugin_is_loaded()
    {
        // Le DÉTAIL des capacités vit ICI depuis le lot polish UX/UI (déplacé du hub Paramétrage) :
        // c'est là où l'opérateur configure le compte que la capacité éclaire la décision.
        var caps = Capabilities(b2c: true, domesticPayment: false);
        var model = new PaAccountConsoleModel
        {
            Accounts = [new PaAccountSettingsDto { Account = Account(), PluginAvailable = true, Capabilities = caps }],
            RegisteredPluginTypes = ["Fake"],
        };

        var cut = Render<ComptesPaView>(p => p.Add(v => v.Model, model));

        cut.FindAll("[data-testid='comptes-pa-capabilities']").Should().ContainSingle();
        cut.FindAll("[data-testid='comptes-pa-capability']").Should().HaveCount(9);
        var details = cut.Find("[data-testid='comptes-pa-capabilities']");
        details.TextContent.Should().Contain("Déclaration B2C");
        details.TextContent.Should().Contain("Auto-facturation sous mandat (389)", "la capacité 389 est affichée (MND07)");
        details.TextContent.Should().Contain("Disponible");
        details.TextContent.Should().Contain("Non disponible");
    }

    [Fact]
    public void Shows_plugin_unavailable_when_capabilities_are_missing()
    {
        var cut = Render<ComptesPaView>(p => p.Add(v => v.Model, Model([Account()], ["Fake"])));

        cut.FindAll("[data-testid='comptes-pa-plugin-unavailable']").Should().ContainSingle();
        cut.FindAll("[data-testid='comptes-pa-capabilities']").Should().BeEmpty();
    }

    [Fact]
    public void Create_editor_proposes_plugin_types_from_the_registry_as_a_closed_select()
    {
        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([], ["Fake", "B2Brouter"]))
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, true)
            .Add(v => v.EditorModel, new PaAccountFormModel()));

        var pluginType = cut.Find("[data-testid='comptes-pa-plugintype']");
        pluginType.NodeName.Should().Be("SELECT", "le type est proposé depuis le registre, jamais en saisie libre");

        // Deux types + le placeholder « — Choisir — ».
        pluginType.QuerySelectorAll("option").Should().HaveCount(3);
    }

    [Fact]
    public void Create_editor_api_key_input_is_password_and_not_prefilled()
    {
        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([], ["Fake"]))
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, true)
            .Add(v => v.EditorModel, new PaAccountFormModel()));

        var apiKey = cut.Find("[data-testid='comptes-pa-apikey']");
        apiKey.GetAttribute("type").Should().Be("password", "la clé est saisie masquée (CLAUDE.md n°10)");
        apiKey.GetAttribute("value").Should().BeNullOrEmpty("la clé n'est jamais pré-remplie");
    }

    [Fact]
    public void Create_editor_with_empty_registry_shows_no_plugin_message_and_no_select()
    {
        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([], []))
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, true)
            .Add(v => v.EditorModel, new PaAccountFormModel()));

        cut.FindAll("[data-testid='comptes-pa-no-plugins']").Should().ContainSingle();
        cut.FindAll("[data-testid='comptes-pa-plugintype']").Should().BeEmpty();
    }

    [Fact]
    public void Edit_editor_freezes_the_plugin_type()
    {
        var model = new PaAccountFormModel
        {
            PaAccountId = Guid.NewGuid(),
            PluginType = "Fake",
            Environment = "Staging",
        };

        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([Account()], ["Fake"]))
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, false)
            .Add(v => v.EditorModel, model));

        var pluginType = cut.Find("[data-testid='comptes-pa-plugintype']");
        pluginType.NodeName.Should().Be("INPUT");
        pluginType.HasAttribute("disabled").Should().BeTrue("le type identifie le plug-in : figé en édition");
        pluginType.GetAttribute("value").Should().Be("Fake");
    }

    [Fact]
    public void Save_button_invokes_the_submit_callback()
    {
        var submitted = false;
        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([], ["Fake"]))
            .Add(v => v.EditorOpen, true)
            .Add(v => v.EditorIsCreate, true)
            .Add(v => v.EditorModel, new PaAccountFormModel { PluginType = "Fake", Environment = "Staging" })
            .Add(v => v.OnSubmitEditor, () => submitted = true));

        cut.Find("[data-testid='comptes-pa-save-btn']").Click();

        submitted.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_confirmation_renders_for_the_target_account()
    {
        var account = Account();
        var cut = Render<ComptesPaView>(p => p
            .Add(v => v.Model, Model([account], ["Fake"]))
            .Add(v => v.DeactivateTarget, account));

        cut.FindAll("[data-testid='comptes-pa-deactivate-confirm']").Should().ContainSingle();
        cut.Find("[data-testid='comptes-pa-deactivate-confirm']").TextContent.Should().Contain("Fake — Staging");
    }

    private static PaAccountConsoleModel Model(IReadOnlyList<PaAccountDto> accounts, IReadOnlyList<string> pluginTypes) =>
        new()
        {
            // Capacités résolues par défaut absentes (plug-in non chargé) : les tests dédiés aux
            // capacités construisent leur propre PaAccountSettingsDto.
            Accounts = accounts.Select(a => new PaAccountSettingsDto { Account = a, PluginAvailable = false, Capabilities = null }).ToList(),
            RegisteredPluginTypes = pluginTypes,
        };

    private static PaAccountDto Account(bool hasApiKey = false) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        PluginType = "Fake",
        Environment = "Staging",
        AccountIdentifiers = "{}",
        HasApiKey = hasApiKey,
        IsActive = true,
        CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static PaCapabilitiesSummaryDto Capabilities(bool b2c = true, bool domesticPayment = true) => new()
    {
        PaName = "B2Brouter",
        SupportsB2cReporting = b2c,
        SupportsDomesticPaymentReporting = domesticPayment,
        SupportsInternationalPaymentReporting = false,
        SupportsB2bInvoicing = true,
        SupportsCreditNotes = true,
        SupportsTaxReportRetrieval = false,
        SupportsDocumentRetrieval = false,
        SupportsReportRectification = true,
        SupportsSelfBilling = false,
        MaxDocumentsPerRequest = 100,
    };
}
