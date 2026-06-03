namespace Stratum.Modules.Notification.Domain.Services;

using System.Text.Json;
using Stratum.Modules.Notification.Domain.ValueObjects;

/// <summary>
/// Evaluates routing conditions against entity data.
/// Same tree-evaluation pattern as FormEngine's ConditionEvaluator.
/// </summary>
public static class RoutingConditionEvaluator
{
    public static bool Evaluate(RoutingCondition? condition, IReadOnlyDictionary<string, JsonElement> data)
    {
        if (condition is null)
        {
            return true;
        }

        if (condition.IsLeaf)
        {
            return EvaluateLeaf(condition, data);
        }

        if (condition.IsCompound)
        {
            return EvaluateCompound(condition, data);
        }

        return true;
    }

    public static bool EvaluateAll(IReadOnlyList<RoutingCondition> conditions, IReadOnlyDictionary<string, JsonElement> data)
    {
        if (conditions.Count == 0)
        {
            return true;
        }

        return conditions.All(c => Evaluate(c, data));
    }

    private static bool EvaluateLeaf(RoutingCondition condition, IReadOnlyDictionary<string, JsonElement> data)
    {
        var field = condition.Field!;
        var op = condition.Operator!;

        var hasValue = data.TryGetValue(field, out var actualValue);

        return op switch
        {
            "is_empty" => !hasValue || IsEmpty(actualValue),
            "is_not_empty" => hasValue && !IsEmpty(actualValue),
            _ => hasValue && CompareValues(actualValue, op, condition.Value),
        };
    }

    private static bool EvaluateCompound(RoutingCondition condition, IReadOnlyDictionary<string, JsonElement> data)
    {
        if (condition.Children is null || condition.Children.Count == 0)
        {
            return true;
        }

        return condition.LogicalOperator switch
        {
            "and" => condition.Children.All(c => Evaluate(c, data)),
            "or" => condition.Children.Any(c => Evaluate(c, data)),
            _ => true,
        };
    }

    private static bool CompareValues(JsonElement actual, string op, JsonElement? expected)
    {
        return op switch
        {
            "eq" => AreEqual(actual, expected),
            "neq" => !AreEqual(actual, expected),
            "gt" => CompareNumeric(actual, expected) > 0,
            "lt" => CompareNumeric(actual, expected) < 0,
            "gte" => CompareNumeric(actual, expected) >= 0,
            "lte" => CompareNumeric(actual, expected) <= 0,
            "in" => IsIn(actual, expected),
            "not_in" => !IsIn(actual, expected),
            "contains" => ArrayContains(actual, expected),
            "not_contains" => !ArrayContains(actual, expected),
            _ => false,
        };
    }

    private static bool AreEqual(JsonElement actual, JsonElement? expected)
    {
        if (expected is null)
        {
            return IsEmpty(actual);
        }

        var exp = expected.Value;

        if (exp.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var expectedBool = exp.ValueKind == JsonValueKind.True;
            return GetBoolValue(actual) == expectedBool;
        }

        if (actual.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            var actualBool = actual.ValueKind == JsonValueKind.True;
            return GetBoolValue(exp) == actualBool;
        }

        if (actual.ValueKind == JsonValueKind.Number && exp.ValueKind == JsonValueKind.Number)
        {
            return actual.GetDecimal() == exp.GetDecimal();
        }

        var actualStr = GetStringValue(actual);
        var expectedStr = GetStringValue(exp);
        return string.Equals(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareNumeric(JsonElement actual, JsonElement? expected)
    {
        if (expected is null)
        {
            return 1;
        }

        var a = GetNumericValue(actual);
        var b = GetNumericValue(expected.Value);

        if (a.HasValue && b.HasValue)
        {
            return a.Value.CompareTo(b.Value);
        }

        return string.Compare(
            GetStringValue(actual),
            GetStringValue(expected.Value),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIn(JsonElement actual, JsonElement? expected)
    {
        if (expected is null || expected.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in expected.Value.EnumerateArray())
        {
            if (AreEqual(actual, item))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ArrayContains(JsonElement actual, JsonElement? expected)
    {
        if (expected is null || actual.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in actual.EnumerateArray())
        {
            if (AreEqual(item, expected.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmpty(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.Undefined => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };
    }

    private static bool? GetBoolValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var b) ? b : null,
            _ => null,
        };
    }

    private static decimal? GetNumericValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDecimal();
        }

        if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), out var d))
        {
            return d;
        }

        return null;
    }

    private static string? GetStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }
}
