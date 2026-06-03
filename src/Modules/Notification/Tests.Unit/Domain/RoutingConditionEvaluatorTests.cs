namespace Stratum.Modules.Notification.Tests.Unit.Domain;

using System.Text.Json;
using FluentAssertions;
using Stratum.Modules.Notification.Domain.Services;
using Stratum.Modules.Notification.Domain.ValueObjects;
using Xunit;

public class RoutingConditionEvaluatorTests
{
    private static readonly string[] IndoorOutdoor = ["indoor", "outdoor"];
    private static readonly string[] MaterialItems = ["tables", "chaises", "sono"];

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

    [Fact]
    public void Null_condition_returns_true()
    {
        RoutingConditionEvaluator.Evaluate(null, Data()).Should().BeTrue();
    }

    [Fact]
    public void Eq_operator_matches_boolean()
    {
        var cond = RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true));
        var data = Data(("fermeture_voirie", true));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Eq_operator_does_not_match_different_value()
    {
        var cond = RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true));
        var data = Data(("fermeture_voirie", false));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeFalse();
    }

    [Fact]
    public void Eq_operator_matches_string()
    {
        var cond = RoutingCondition.Leaf("type", "eq", Json("concert"));
        var data = Data(("type", "concert"));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Neq_operator_works()
    {
        var cond = RoutingCondition.Leaf("status", "neq", Json("cancelled"));
        var data = Data(("status", "pending"));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Gt_operator_numeric()
    {
        var cond = RoutingCondition.Leaf("affluence", "gt", Json(500));
        var data = Data(("affluence", 600));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Lt_operator_numeric()
    {
        var cond = RoutingCondition.Leaf("affluence", "lt", Json(500));
        var data = Data(("affluence", 300));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void In_operator_matches_from_list()
    {
        var cond = RoutingCondition.Leaf("type", "in", Json(IndoorOutdoor));
        var data = Data(("type", "indoor"));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void In_operator_rejects_non_member()
    {
        var cond = RoutingCondition.Leaf("type", "in", Json(IndoorOutdoor));
        var data = Data(("type", "hybrid"));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeFalse();
    }

    [Fact]
    public void Contains_operator_checks_array_field()
    {
        var cond = RoutingCondition.Leaf("materiel", "contains", Json("tables"));
        var data = Data(("materiel", MaterialItems));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Is_empty_operator()
    {
        var cond = RoutingCondition.Leaf("notes", "is_empty", null);
        var data = Data(("notes", string.Empty));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Is_not_empty_operator()
    {
        var cond = RoutingCondition.Leaf("notes", "is_not_empty", null);
        var data = Data(("notes", "some note"));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Missing_field_returns_false_for_comparison()
    {
        var cond = RoutingCondition.Leaf("unknown_field", "eq", Json(true));
        var data = Data();

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeFalse();
    }

    [Fact]
    public void And_compound_all_true()
    {
        var cond = RoutingCondition.Compound("and",
        [
            RoutingCondition.Leaf("public", "eq", Json(true)),
            RoutingCondition.Leaf("nocturne", "eq", Json(true)),
        ]);
        var data = Data(("public", true), ("nocturne", true));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void And_compound_one_false()
    {
        var cond = RoutingCondition.Compound("and",
        [
            RoutingCondition.Leaf("public", "eq", Json(true)),
            RoutingCondition.Leaf("nocturne", "eq", Json(true)),
        ]);
        var data = Data(("public", true), ("nocturne", false));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeFalse();
    }

    [Fact]
    public void Or_compound_one_true()
    {
        var cond = RoutingCondition.Compound("or",
        [
            RoutingCondition.Leaf("public", "eq", Json(true)),
            RoutingCondition.Leaf("nocturne", "eq", Json(true)),
        ]);
        var data = Data(("public", true), ("nocturne", false));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeTrue();
    }

    [Fact]
    public void Or_compound_none_true()
    {
        var cond = RoutingCondition.Compound("or",
        [
            RoutingCondition.Leaf("public", "eq", Json(true)),
            RoutingCondition.Leaf("nocturne", "eq", Json(true)),
        ]);
        var data = Data(("public", false), ("nocturne", false));

        RoutingConditionEvaluator.Evaluate(cond, data).Should().BeFalse();
    }

    [Fact]
    public void EvaluateAll_with_empty_conditions_returns_true()
    {
        RoutingConditionEvaluator.EvaluateAll([], Data()).Should().BeTrue();
    }

    [Fact]
    public void EvaluateAll_with_all_matching()
    {
        var conditions = new List<RoutingCondition>
        {
            RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true)),
            RoutingCondition.Leaf("public", "eq", Json(true)),
        };
        var data = Data(("fermeture_voirie", true), ("public", true));

        RoutingConditionEvaluator.EvaluateAll(conditions, data).Should().BeTrue();
    }

    [Fact]
    public void EvaluateAll_with_one_not_matching()
    {
        var conditions = new List<RoutingCondition>
        {
            RoutingCondition.Leaf("fermeture_voirie", "eq", Json(true)),
            RoutingCondition.Leaf("public", "eq", Json(true)),
        };
        var data = Data(("fermeture_voirie", true), ("public", false));

        RoutingConditionEvaluator.EvaluateAll(conditions, data).Should().BeFalse();
    }
}
