namespace Stratum.Modules.Identity.Domain.Services;

using System.Text.RegularExpressions;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Minimal expression parser for ABAC conditions on grants.
/// Supports == and != operators on actor/record fields and string literals.
/// </summary>
public static class ConditionParser
{
    private static readonly Regex ConditionPattern = new(
        @"^(?<left>(?:record|actor)\.\w+)\s*(?<op>==|!=)\s*(?<right>(?:(?:record|actor)\.\w+)|""[^""]*"")$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ActorFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "company_id",
        "user_id",
        "timezone",
        "language",
    };

    public static ConditionParseResult Validate(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return new ConditionParseResult
            {
                IsValid = false,
                ErrorMessage = "Condition cannot be empty.",
            };
        }

        var match = ConditionPattern.Match(condition.Trim());
        if (!match.Success)
        {
            return new ConditionParseResult
            {
                IsValid = false,
                ErrorMessage = "Condition must be in format: {operand} {==|!=} {operand}. " +
                    "Operands: record.{field}, actor.{field}, or \"literal\".",
            };
        }

        var left = match.Groups["left"].Value;
        var right = match.Groups["right"].Value;

        var leftError = ValidateOperand(left);
        if (leftError is not null)
        {
            return new ConditionParseResult { IsValid = false, ErrorMessage = leftError };
        }

        if (!right.StartsWith('"'))
        {
            var rightError = ValidateOperand(right);
            if (rightError is not null)
            {
                return new ConditionParseResult { IsValid = false, ErrorMessage = rightError };
            }
        }

        return new ConditionParseResult { IsValid = true };
    }

    /// <summary>
    /// Evaluates a condition expression against actor context and resource context.
    /// INV-IDENT-017: invalid condition evaluation = deny (returns false, no exception).
    /// </summary>
    public static bool Evaluate(
        string condition,
        IActorContext actor,
        IDictionary<string, object?>? resourceContext)
    {
        try
        {
            var match = ConditionPattern.Match(condition.Trim());
            if (!match.Success)
            {
                return false;
            }

            var leftStr = match.Groups["left"].Value;
            var op = match.Groups["op"].Value;
            var rightStr = match.Groups["right"].Value;

            var leftValue = ResolveOperand(leftStr, actor, resourceContext);
            var rightValue = ResolveOperand(rightStr, actor, resourceContext);

            return op switch
            {
                "==" => string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }
        catch
        {
            // INV-IDENT-017: evaluation failure = deny
            return false;
        }
    }

    private static string? ValidateOperand(string operand)
    {
        var parts = operand.Split('.', 2);
        if (parts.Length != 2)
        {
            return $"Invalid operand '{operand}': expected format prefix.field.";
        }

        var prefix = parts[0];
        var field = parts[1];

        if (prefix == "actor" && !ActorFields.Contains(field))
        {
            return $"Unknown actor field '{field}'. Available: {string.Join(", ", ActorFields)}.";
        }

        // record fields are dynamic — no validation needed
        return null;
    }

    private static string? ResolveOperand(
        string operand,
        IActorContext actor,
        IDictionary<string, object?>? resourceContext)
    {
        // String literal
        if (operand.StartsWith('"') && operand.EndsWith('"'))
        {
            return operand[1..^1];
        }

        var parts = operand.Split('.', 2);
        var prefix = parts[0];
        var field = parts[1];

        if (prefix == "actor")
        {
            return ResolveActorField(field, actor);
        }

        if (prefix == "record")
        {
            if (resourceContext is null || !resourceContext.TryGetValue(field, out var value))
            {
                return null;
            }

            return value?.ToString();
        }

        return null;
    }

    private static string? ResolveActorField(string field, IActorContext actor)
    {
        return field.ToLowerInvariant() switch
        {
            "company_id" => actor.CompanyId?.ToString(),
            "user_id" => actor.UserId.ToString(),
            "timezone" => actor.Timezone,
            "language" => actor.Language,
            _ => null,
        };
    }
}
