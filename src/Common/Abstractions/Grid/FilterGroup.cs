namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// A group of filter criteria combined with a logical operator.
/// Supports nested sub-groups for complex expressions like
/// "(A AND B) OR (C AND D)".
/// </summary>
/// <param name="Logic">How criteria and sub-groups are combined.</param>
/// <param name="Criteria">Individual filter conditions in this group.</param>
/// <param name="SubGroups">Nested filter groups for complex logic.</param>
public sealed record FilterGroup(
    FilterLogic Logic,
    IReadOnlyList<FilterCriterion> Criteria,
    IReadOnlyList<FilterGroup>? SubGroups = null);
