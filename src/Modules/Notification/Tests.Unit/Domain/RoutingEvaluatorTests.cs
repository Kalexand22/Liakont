namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using System.Text.Json;
using FluentAssertions;
using Stratum.Modules.Notification.Domain.Entities;
using Stratum.Modules.Notification.Domain.Services;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Xunit;

public class RoutingEvaluatorTests
{
    private static readonly string[] TablesChaises = ["tables", "chaises"];

    private static JsonElement Json(object value)
    {
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
    }

    private static Dictionary<string, JsonElement> Data(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = Json(value);
        }

        return dict;
    }

    private static RoutingRule CreateRule(
        string code,
        string serviceCode,
        int priority,
        IReadOnlyList<RoutingCondition> conditions,
        bool isActive = true)
    {
        var rule = RoutingRule.Create(
            code, $"Rule {code}", "reservation", serviceCode,
            RecipientType.ServiceEmail, $"{serviceCode}@commune.fr",
            conditions, priority, null);

        if (!isActive)
        {
            rule.Update(rule.Name, rule.ServiceCode, rule.RecipientType, rule.RecipientValue, rule.Conditions, rule.Priority, false);
        }

        return rule;
    }

    [Fact]
    public void Empty_rules_returns_empty_matches()
    {
        var matches = RoutingEvaluator.Evaluate([], Data());

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Rule_with_no_conditions_matches_everything()
    {
        var rule = CreateRule("gestion-salles", "gestion-salles", 10, []);
        var data = Data(("type", "some_type"));

        var matches = RoutingEvaluator.Evaluate([rule], data);

        matches.Should().HaveCount(1);
        matches[0].ServiceCode.Should().Be("gestion-salles");
    }

    [Fact]
    public void Inactive_rule_is_skipped()
    {
        var rule = CreateRule("gestion-salles", "gestion-salles", 10, [], isActive: false);
        var data = Data();

        var matches = RoutingEvaluator.Evaluate([rule], data);

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Rule_with_condition_that_matches()
    {
        var cond = RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true));
        var rule = CreateRule("voirie", "voirie", 20, [cond]);
        var data = Data(("fermeture_voirie", true));

        var matches = RoutingEvaluator.Evaluate([rule], data);

        matches.Should().HaveCount(1);
        matches[0].ServiceCode.Should().Be("voirie");
    }

    [Fact]
    public void Rule_with_condition_that_does_not_match()
    {
        var cond = RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true));
        var rule = CreateRule("voirie", "voirie", 20, [cond]);
        var data = Data(("fermeture_voirie", false));

        var matches = RoutingEvaluator.Evaluate([rule], data);

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Multiple_rules_ordered_by_priority()
    {
        var rule1 = CreateRule("communication", "communication", 30,
        [
            RoutingCondition.Leaf("public", "eq", Json(true)),
        ]);
        var rule2 = CreateRule("gestion-salles", "gestion-salles", 10, []);
        var rule3 = CreateRule("voirie", "voirie", 20,
        [
            RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true)),
        ]);

        var data = Data(("public", true), ("fermeture_voirie", true));

        var matches = RoutingEvaluator.Evaluate([rule1, rule2, rule3], data);

        matches.Should().HaveCount(3);
        matches[0].Priority.Should().Be(10);
        matches[1].Priority.Should().Be(20);
        matches[2].Priority.Should().Be(30);
    }

    [Fact]
    public void Mixed_matching_and_non_matching_rules()
    {
        var rule1 = CreateRule("gestion-salles", "gestion-salles", 10, []);
        var rule2 = CreateRule("technique", "technique", 20,
        [
            RoutingCondition.Leaf("materiel", "contains", Json("sono")),
        ]);
        var rule3 = CreateRule("voirie", "voirie", 30,
        [
            RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true)),
        ]);

        var data = Data(
            ("materiel", TablesChaises),
            ("fermeture_voirie", false));

        var matches = RoutingEvaluator.Evaluate([rule1, rule2, rule3], data);

        matches.Should().HaveCount(1);
        matches[0].ServiceCode.Should().Be("gestion-salles");
    }
}
