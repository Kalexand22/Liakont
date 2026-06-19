namespace Liakont.Host.Tests.Unit.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Host.Demo;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.UI;
using Xunit;

/// <summary>
/// Rendu PUR de l'écran « Démo e-reporting B2C — Essentiel » (B2C04) : le chemin Essentiel de bout en bout
/// (extraction BA → pivot → mapping → 10.3 → lien B2C-03 → export) est affiché, l'état transmis/accusé
/// (<c>Issued</c>) et le blocage régime non mappé (<c>Blocked</c>) sont VISIBLES, le déclenchement manuel est
/// offert sous permission d'action et câblé au callback. Aucune logique métier, aucune valeur inventée.
/// </summary>
public sealed class DemoB2cViewTests : BunitContext
{
    public DemoB2cViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddLocalization();
        Services.AddCommonUI();
    }

    [Fact]
    public void Renders_The_End_To_End_Pipeline_Strip()
    {
        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, Model()));

        // Acceptance n°1 : le chemin extraction BA → pivot → mapping → 10.3 → lien B2C-03 → export est VISIBLE.
        var strip = cut.Find("[data-testid='demo-b2c-pipeline']").TextContent;
        strip.Should().Contain("bordereau acheteur");
        strip.Should().Contain("pivot");
        strip.Should().Contain("mapping");
        strip.Should().Contain("10.3");
        strip.Should().Contain("B2C-03");
        strip.Should().Contain("export");
    }

    [Fact]
    public void Renders_All_Declaration_Rows()
    {
        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, Model(
            Row("B2C-1", "Issued", hasLink: true),
            Row("B2C-2", "Blocked", hasLink: false),
            Row("B2C-3", "ReadyToSend", hasLink: false))));

        cut.FindAll("[data-testid='demo-b2c-row']").Should().HaveCount(3);
    }

    [Fact]
    public void Shows_Transmitted_State_For_An_Issued_Declaration()
    {
        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, Model(Row("B2C-1", "Issued", hasLink: true))));

        // L'état « transmis/accusé » (Issued) est rendu par la pastille d'état partagée.
        cut.FindAll("[data-testid='demo-b2c-state']").Should().ContainSingle();
        cut.Find("[data-testid='demo-b2c-row']").TextContent.Should().Contain("Gelé", "le lien B2C-03 est gelé pour une déclaration émise.");
    }

    [Fact]
    public void Shows_Blocked_State_For_An_Unmapped_Regime_Declaration()
    {
        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, Model(Row("B2C-r6", "Blocked", hasLink: false))));

        // Blocage régime non mappé (p. ex. régime 6) VISIBLE : pastille d'état + mention du blocage dans l'intro.
        cut.FindAll("[data-testid='demo-b2c-state']").Should().ContainSingle();
        cut.Find("[data-testid='demo-b2c-intro']").TextContent.Should().Contain("régime");
    }

    [Fact]
    public void Link_Column_Reflects_Reporting_Piece_Link_Presence()
    {
        var withLink = Render<DemoB2cView>(p => p.Add(v => v.Model, Model(Row("B2C-1", "Issued", hasLink: true))));
        withLink.Find("[data-testid='demo-b2c-link']").TextContent.Trim().Should().Be("Gelé");

        var withoutLink = Render<DemoB2cView>(p => p.Add(v => v.Model, Model(Row("B2C-2", "ReadyToSend", hasLink: false))));
        withoutLink.Find("[data-testid='demo-b2c-link']").TextContent.Trim().Should().Be("—");
    }

    [Fact]
    public void Export_Anchor_Points_To_The_Audit_Export_Endpoint()
    {
        var id = Guid.NewGuid();
        var model = Model(new DemoB2cDeclarationRow(id, "B2C-1", new DateOnly(2026, 1, 20), 144.00m, "Issued", true, $"/api/v1/documents/{id}/audit-export"));

        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, model));

        var anchor = cut.Find("[data-testid='demo-b2c-export']");
        anchor.GetAttribute("href").Should().Be($"/api/v1/documents/{id}/audit-export");
        anchor.HasAttribute("download").Should().BeTrue();
    }

    [Fact]
    public void Trigger_Button_Is_Offered_And_Invokes_Callback_When_CanAct()
    {
        var invoked = false;
        var cut = Render<DemoB2cView>(p => p
            .Add(v => v.Model, Model(Row("B2C-1", "ReadyToSend", hasLink: false)))
            .Add(v => v.CanAct, true)
            .Add(v => v.OnTrigger, () => invoked = true));

        cut.Find("[data-testid='demo-b2c-trigger']").Click();

        invoked.Should().BeTrue("le déclenchement manuel est câblé au callback de la page.");
    }

    [Fact]
    public void Trigger_Button_Is_Hidden_Without_Action_Permission()
    {
        var cut = Render<DemoB2cView>(p => p
            .Add(v => v.Model, Model(Row("B2C-1", "ReadyToSend", hasLink: false)))
            .Add(v => v.CanAct, false));

        cut.FindAll("[data-testid='demo-b2c-trigger']").Should().BeEmpty();
    }

    [Fact]
    public void Shows_Feedback_Message_As_Alert_When_Error()
    {
        var cut = Render<DemoB2cView>(p => p
            .Add(v => v.Model, Model())
            .Add(v => v.Message, "Le déclenchement a échoué.")
            .Add(v => v.IsError, true));

        var feedback = cut.Find("[data-testid='demo-b2c-feedback']");
        feedback.GetAttribute("role").Should().Be("alert");
        feedback.TextContent.Should().Contain("échoué");
    }

    [Fact]
    public void Empty_Model_Shows_The_Empty_Hint()
    {
        var cut = Render<DemoB2cView>(p => p.Add(v => v.Model, Model()));

        cut.FindAll("[data-testid='demo-b2c-empty']").Should().ContainSingle();
        cut.FindAll("[data-testid='demo-b2c-row']").Should().BeEmpty();
    }

    private static DemoB2cDeclarationRow Row(string number, string state, bool hasLink)
    {
        var id = Guid.NewGuid();
        return new DemoB2cDeclarationRow(id, number, new DateOnly(2026, 1, 20), 144.00m, state, hasLink, $"/api/v1/documents/{id}/audit-export");
    }

    private static DemoB2cViewModel Model(params DemoB2cDeclarationRow[] rows) =>
        new() { Declarations = rows.ToList() };
}
