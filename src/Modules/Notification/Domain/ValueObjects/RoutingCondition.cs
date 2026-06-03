namespace Stratum.Modules.Notification.Domain.ValueObjects;

using System.Text.Json;

/// <summary>
/// Expression evaluated against entity data to decide whether a routing rule matches.
/// Reuses the same tree structure as FormEngine's VisibilityCondition:
/// leaf nodes compare a field to a value; compound nodes combine children with AND/OR.
/// </summary>
public sealed class RoutingCondition
{
    private RoutingCondition()
    {
    }

    public string? Field { get; private set; }

    public string? Operator { get; private set; }

    public JsonElement? Value { get; private set; }

    public string? LogicalOperator { get; private set; }

    public IReadOnlyList<RoutingCondition>? Children { get; private set; }

    public bool IsLeaf => Field is not null;

    public bool IsCompound => LogicalOperator is not null;

    public static RoutingCondition Leaf(string field, string op, JsonElement? value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("RoutingCondition field cannot be empty.", nameof(field));
        }

        if (string.IsNullOrWhiteSpace(op))
        {
            throw new ArgumentException("RoutingCondition operator cannot be empty.", nameof(op));
        }

        return new RoutingCondition
        {
            Field = field.Trim(),
            Operator = op.Trim().ToLowerInvariant(),
            Value = value,
        };
    }

    public static RoutingCondition Compound(string logicalOperator, IReadOnlyList<RoutingCondition> children)
    {
        if (logicalOperator is not ("and" or "or"))
        {
            throw new ArgumentException("LogicalOperator must be 'and' or 'or'.", nameof(logicalOperator));
        }

        if (children is null || children.Count < 2)
        {
            throw new ArgumentException("Compound condition must have at least 2 children.", nameof(children));
        }

        return new RoutingCondition
        {
            LogicalOperator = logicalOperator,
            Children = children,
        };
    }

    public static RoutingCondition Reconstitute(
        string? field,
        string? op,
        JsonElement? value,
        string? logicalOperator,
        IReadOnlyList<RoutingCondition>? children)
    {
        return new RoutingCondition
        {
            Field = field,
            Operator = op,
            Value = value,
            LogicalOperator = logicalOperator,
            Children = children,
        };
    }
}
