namespace Stratum.Common.UI.Models;

/// <summary>
/// Immutable tree node for <c>LogicTreeEditor</c> (UIE01).
/// Each node is either a <see cref="LogicNodeType.Group"/> (AND/OR with children)
/// or a <see cref="LogicNodeType.Condition"/> (variable + operator + value).
///
/// The DSL is JSON-serialisable and structurally compatible with
/// <c>FormEngine.VisibilityCondition</c>: groups map to <c>logicalOperator + children</c>,
/// conditions map to <c>fieldCode + operator + value</c>.
///
/// Immutability: all mutations return a new tree. The editor calls
/// <c>OnChange</c> with the new root after every edit, enabling undo/redo
/// at the consumer level.
/// </summary>
public sealed class LogicNode
{
    /// <summary>Unique id for keyed rendering and drag-drop identity.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Discriminator — group or condition.</summary>
    public LogicNodeType NodeType { get; init; }

    // ── Group fields ──

    /// <summary>AND / OR combinator (only when <see cref="NodeType"/> is <see cref="LogicNodeType.Group"/>).</summary>
    public LogicGroupOperator GroupOperator { get; init; }

    /// <summary>Child nodes (only when <see cref="NodeType"/> is <see cref="LogicNodeType.Group"/>).</summary>
    public IReadOnlyList<LogicNode> Children { get; init; } = [];

    // ── Condition fields ──

    /// <summary>Variable code (leaf condition).</summary>
    public string? Variable { get; init; }

    /// <summary>Comparison operator code (leaf condition).</summary>
    public string? Operator { get; init; }

    /// <summary>Expected value as string (leaf condition). Null for unary operators.</summary>
    public string? Value { get; init; }

    // ── Factories ──

    /// <summary>Create a new AND/OR group.</summary>
    public static LogicNode Group(LogicGroupOperator op, params LogicNode[] children)
        => new() { NodeType = LogicNodeType.Group, GroupOperator = op, Children = [.. children] };

    /// <summary>Create a new leaf condition.</summary>
    public static LogicNode Condition(string? variable = null, string? op = null, string? value = null)
        => new() { NodeType = LogicNodeType.Condition, Variable = variable, Operator = op, Value = value };

    /// <summary>Create a default empty AND group with one empty condition.</summary>
    public static LogicNode DefaultRoot()
        => Group(LogicGroupOperator.And, Condition());
}
