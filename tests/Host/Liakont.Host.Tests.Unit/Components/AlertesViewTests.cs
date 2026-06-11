namespace Liakont.Host.Tests.Unit.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Alertes;
using Liakont.Host.Components;
using Liakont.Modules.Supervision.Contracts.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Rendu PUR de « Paramétrage › Alertes » (FIX210) : règles actives + gelées avec gravité et seuil effectif,
/// état de l'e-mail opérateur, et édition (seuils, contact) câblée vers les callbacks. Aucune adresse opérateur
/// affichée. Contact non éditable tant que le profil n'existe pas.
/// </summary>
public sealed class AlertesViewTests : BunitContext
{
    public AlertesViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_Active_And_Frozen_Rules_With_Their_Threshold()
    {
        var cut = Render<AlertesView>(p => p.Add(v => v.Model, Model()));

        cut.FindAll("[data-testid='alertes-rule-row']").Should().HaveCount(3);

        // Une règle gelée porte le badge « Gelée » (ne pas laisser croire à une couverture).
        cut.FindAll("[data-testid='alertes-rule-frozen']").Should().ContainSingle();
        cut.Find("[data-testid='alertes-rules-table']").TextContent.Should().Contain("> 24 h").And.Contain("J-3");
    }

    [Fact]
    public void Operator_Email_State_Is_Shown_Without_The_Address()
    {
        var configured = Render<AlertesView>(p => p.Add(v => v.Model, Model(operatorEmailConfigured: true)));
        configured.Find("[data-testid='alertes-operator-email']").TextContent.Should().Contain("configuré");

        var notConfigured = Render<AlertesView>(p => p.Add(v => v.Model, Model(operatorEmailConfigured: false)));
        notConfigured.Find("[data-testid='alertes-operator-email']").TextContent.Should().Contain("non configuré");
    }

    [Fact]
    public void Saving_Thresholds_Invokes_The_Callback()
    {
        var saved = false;
        var cut = Render<AlertesView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.OnSaveThresholds, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='alertes-thresholds-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Saving_Contact_Invokes_The_Callback_When_Profile_Exists()
    {
        var saved = false;
        var cut = Render<AlertesView>(p => p
            .Add(v => v.Model, Model(profileExists: true))
            .Add(v => v.OnSaveContact, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='alertes-contact-save-btn']").Click();

        saved.Should().BeTrue();
    }

    [Fact]
    public void Contact_Is_Not_Editable_Without_A_Profile()
    {
        var cut = Render<AlertesView>(p => p.Add(v => v.Model, Model(profileExists: false)));

        cut.FindAll("[data-testid='alertes-contact-no-profile']").Should().ContainSingle();
        cut.FindAll("[data-testid='alertes-contact-editor']").Should().BeEmpty();
    }

    [Fact]
    public void Empty_Routing_Matrix_Shows_The_Default_Model_Notice()
    {
        var cut = Render<AlertesView>(p => p.Add(v => v.Model, Model()));

        // Matrice vide : le routage par défaut (modèle simple) est annoncé, aucune ligne.
        cut.FindAll("[data-testid='alertes-routing-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='alertes-routing-row']").Should().BeEmpty();
    }

    [Fact]
    public void Routing_Matrix_Renders_Existing_Rows()
    {
        var routing = new AlertesRoutingFormModel
        {
            Rows =
            {
                new AlertesRoutingRow { Selector = "rule:documents.pa_rejected", RecipientsCsv = "compta@acme.test" },
                new AlertesRoutingRow { Selector = "severity:Critical", RecipientsCsv = "it@acme.test, admin@acme.test" },
            },
        };

        var cut = Render<AlertesView>(p => p.Add(v => v.Model, Model(routing: routing)));

        cut.FindAll("[data-testid='alertes-routing-row']").Should().HaveCount(2);
    }

    [Fact]
    public void Adding_A_Routing_Entry_Appends_A_Row()
    {
        var cut = Render<AlertesView>(p => p.Add(v => v.Model, Model()));

        cut.Find("[data-testid='alertes-routing-add-btn']").Click();

        cut.FindAll("[data-testid='alertes-routing-row']").Should().ContainSingle();
    }

    [Fact]
    public void Saving_The_Routing_Matrix_Invokes_The_Callback()
    {
        var saved = false;
        var cut = Render<AlertesView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.OnSaveRouting, EventCallback.Factory.Create(this, () => saved = true)));

        cut.Find("[data-testid='alertes-routing-save-btn']").Click();

        saved.Should().BeTrue();
    }

    private static AlertesViewModel Model(
        bool operatorEmailConfigured = true,
        bool profileExists = true,
        AlertesRoutingFormModel? routing = null) => new()
    {
        Device = new AlertDeviceStatusDto
        {
            OperatorEmailConfigured = operatorEmailConfigured,
            EvaluationIntervalMinutes = 15,
            Rules = new List<AlertRuleStatusDto>
            {
                new() { RuleKey = "agent.mute", DisplayName = "Agent muet", Severity = "Critique", IsActive = true, ThresholdDisplay = "> 24 h" },
                new() { RuleKey = "documents.pa_rejected", DisplayName = "Rejets PA", Severity = "Critique", IsActive = true, ThresholdDisplay = "> 2 j" },
                new() { RuleKey = "period.deadline_near", DisplayName = "Échéance proche", Severity = "Critique", IsActive = false, ThresholdDisplay = "J-3" },
            },
        },
        Form = new AlertesFormModel
        {
            AgentSilentHours = 24,
            BlockedDocumentsDays = 5,
            PaRejectionsDays = 2,
            AlertTenantContact = false,
            ContactEmailAlerte = "alertes@exemple.test",
        },
        Routing = routing ?? new AlertesRoutingFormModel(),
        ProfileExists = profileExists,
    };
}
