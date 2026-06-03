namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Logical combinator for grouping filter criteria.
/// </summary>
public enum FilterLogic
{
    /// <summary>All criteria in the group must match.</summary>
    And,

    /// <summary>At least one criterion in the group must match.</summary>
    Or,
}
