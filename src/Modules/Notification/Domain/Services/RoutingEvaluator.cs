namespace Stratum.Modules.Notification.Domain.Services;

using System.Text.Json;
using Stratum.Modules.Notification.Domain.Entities;

/// <summary>
/// Evaluates routing rules against entity data and returns matched services/recipients
/// ordered by priority.
/// </summary>
public static class RoutingEvaluator
{
    public static IReadOnlyList<RoutingMatch> Evaluate(
        IReadOnlyList<RoutingRule> rules,
        IReadOnlyDictionary<string, JsonElement> data)
    {
        var matches = new List<RoutingMatch>();

        foreach (var rule in rules)
        {
            if (!rule.IsActive)
            {
                continue;
            }

            if (RoutingConditionEvaluator.EvaluateAll(rule.Conditions, data))
            {
                matches.Add(new RoutingMatch(
                    rule.Code,
                    rule.Name,
                    rule.ServiceCode,
                    rule.RecipientType,
                    rule.RecipientValue,
                    rule.Priority));
            }
        }

        matches.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return matches;
    }
}
