namespace Stratum.Common.UI.Models;

/// <summary>
/// Logical combinator for <see cref="LogicNode"/> groups.
/// </summary>
public enum LogicGroupOperator
{
    /// <summary>All children must be true.</summary>
    And,

    /// <summary>At least one child must be true.</summary>
    Or,
}
