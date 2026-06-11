namespace Liakont.Modules.Supervision.Tests.Unit;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Supervision.Domain;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.TenantSettings.Contracts.DTOs;
using Xunit;

/// <summary>
/// Évaluation PURE de la matrice de routage (FIX212, F12 §5.3.1) : correspondance par règle et/ou gravité,
/// union dédoublonnée des destinataires, et liste vide quand rien ne correspond (l'appelant retombe alors
/// sur le modèle simple).
/// </summary>
public sealed class AlertRoutingMatcherTests
{
    [Fact]
    public void Empty_Matrix_Resolves_To_No_Recipients()
    {
        var result = AlertRoutingMatcher.ResolveRecipients([], "agent.mute", AlertSeverity.Critical);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Matches_By_Rule_Key()
    {
        var matrix = new List<AlertRoutingRuleDto> { Entry("agent.mute", null, "it@acme.test") };

        AlertRoutingMatcher.ResolveRecipients(matrix, "agent.mute", AlertSeverity.Critical)
            .Should().Equal("it@acme.test");

        AlertRoutingMatcher.ResolveRecipients(matrix, "documents.blocked", AlertSeverity.Warning)
            .Should().BeEmpty();
    }

    [Fact]
    public void Matches_By_Severity_Across_Rules()
    {
        var matrix = new List<AlertRoutingRuleDto> { Entry(null, "Critical", "crit@acme.test") };

        AlertRoutingMatcher.ResolveRecipients(matrix, "agent.mute", AlertSeverity.Critical)
            .Should().Equal("crit@acme.test");
        AlertRoutingMatcher.ResolveRecipients(matrix, "documents.pa_rejected", AlertSeverity.Critical)
            .Should().Equal("crit@acme.test");
        AlertRoutingMatcher.ResolveRecipients(matrix, "documents.blocked", AlertSeverity.Warning)
            .Should().BeEmpty();
    }

    [Fact]
    public void Unions_And_Dedupes_Recipients_Across_Matching_Entries()
    {
        var matrix = new List<AlertRoutingRuleDto>
        {
            Entry("agent.mute", null, "it@acme.test", "shared@acme.test"),
            Entry(null, "Critical", "shared@acme.test", "crit@acme.test"),
        };

        AlertRoutingMatcher.ResolveRecipients(matrix, "agent.mute", AlertSeverity.Critical)
            .Should().Equal("it@acme.test", "shared@acme.test", "crit@acme.test");
    }

    [Fact]
    public void Rule_And_Severity_Selector_Requires_Both_To_Match()
    {
        var matrix = new List<AlertRoutingRuleDto> { Entry("agent.mute", "Critical", "x@acme.test") };

        AlertRoutingMatcher.ResolveRecipients(matrix, "agent.mute", AlertSeverity.Critical)
            .Should().Equal("x@acme.test");

        // Bonne règle mais mauvaise gravité : pas de correspondance.
        AlertRoutingMatcher.ResolveRecipients(matrix, "agent.mute", AlertSeverity.Warning)
            .Should().BeEmpty();
    }

    private static AlertRoutingRuleDto Entry(string? ruleKey, string? severity, params string[] recipients) => new()
    {
        RuleKey = ruleKey,
        Severity = severity,
        Recipients = recipients,
        Ordinal = 0,
    };
}
