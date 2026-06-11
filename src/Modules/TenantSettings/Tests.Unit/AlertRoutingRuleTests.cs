namespace Liakont.Modules.TenantSettings.Tests.Unit;

using System;
using FluentAssertions;
using Liakont.Modules.TenantSettings.Domain.Entities;
using Xunit;

/// <summary>
/// <see cref="AlertRoutingRule"/> (FIX212, INV-TENANTSETTINGS-011) : une entrée cible au moins une règle ou
/// une gravité, comporte au moins un destinataire e-mail valide (normalisé, dédoublonné), et refuse les
/// saisies incohérentes (aucun sélecteur, gravité inconnue, e-mail invalide).
/// </summary>
public sealed class AlertRoutingRuleTests
{
    private static readonly Guid Company = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Creates_A_Rule_Targeted_Entry()
    {
        var rule = AlertRoutingRule.Create(Company, "documents.pa_rejected", null, ["compta@acme.test"], 0);

        rule.RuleKey.Should().Be("documents.pa_rejected");
        rule.Severity.Should().BeNull();
        rule.Recipients.Should().Equal("compta@acme.test");
        rule.Ordinal.Should().Be(0);
    }

    [Fact]
    public void Creates_A_Severity_Targeted_Entry()
    {
        var rule = AlertRoutingRule.Create(Company, null, AlertRoutingRule.SeverityCritical, ["it@acme.test"], 1);

        rule.RuleKey.Should().BeNull();
        rule.Severity.Should().Be("Critical");
    }

    [Fact]
    public void Normalizes_Blank_Selectors_To_Null_And_Trims_Recipients()
    {
        var rule = AlertRoutingRule.Create(Company, "  agent.mute ", "   ", ["  it@acme.test  "], 0);

        rule.RuleKey.Should().Be("agent.mute");
        rule.Severity.Should().BeNull();
        rule.Recipients.Should().Equal("it@acme.test");
    }

    [Fact]
    public void Dedupes_Recipients_Case_Insensitively()
    {
        var rule = AlertRoutingRule.Create(Company, "agent.mute", null, ["it@acme.test", "IT@acme.test", "admin@acme.test"], 0);

        rule.Recipients.Should().Equal("it@acme.test", "admin@acme.test");
    }

    [Fact]
    public void Rejects_An_Entry_Without_Any_Selector()
    {
        var act = () => AlertRoutingRule.Create(Company, null, null, ["it@acme.test"], 0);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-011*");
    }

    [Fact]
    public void Rejects_An_Unknown_Severity()
    {
        var act = () => AlertRoutingRule.Create(Company, null, "Fatal", ["it@acme.test"], 0);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-011*");
    }

    [Fact]
    public void Rejects_An_Entry_Without_Any_Recipient()
    {
        var act = () => AlertRoutingRule.Create(Company, "agent.mute", null, ["   "], 0);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-011*");
    }

    [Theory]
    [InlineData("pas-un-email")]
    [InlineData("deux@@arobases.fr")]
    [InlineData("espace dans@email.fr")]
    [InlineData("sansdomaine@x")]
    public void Rejects_An_Invalid_Email(string badEmail)
    {
        var act = () => AlertRoutingRule.Create(Company, "agent.mute", null, [badEmail], 0);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-011*");
    }

    [Fact]
    public void Rejects_A_Negative_Ordinal()
    {
        var act = () => AlertRoutingRule.Create(Company, "agent.mute", null, ["it@acme.test"], -1);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-TENANTSETTINGS-011*");
    }
}
