namespace Stratum.Common.Abstractions.Grid;

/// <summary>
/// Unified filter state for a grid. Combines a global search string and an
/// <see cref="AdvancedFilter"/> group; "simple" filters are a view over the
/// flat root criteria of that group so that everything the user enters — via
/// the simple popover, the advanced builder, the cell menu or the URL — is
/// reflected in a single source of truth (GFI16).
/// </summary>
public sealed class GridFilterState
{
    /// <summary>Global search text (debounced input from the search bar).</summary>
    public string? GlobalSearch { get; set; }

    /// <summary>
    /// Unified filter tree. Flat root criteria are editable as individual
    /// "simple" chips; any sub-group escalates the state to a single advanced
    /// summary chip (DF-05). Null when no structured filter is active.
    /// </summary>
    public FilterGroup? AdvancedFilter { get; set; }

    /// <summary>
    /// Projection of the root AND-combined criteria of <see cref="AdvancedFilter"/>.
    /// Empty when the state is empty or when the root logic is OR (a flat OR
    /// expression cannot round-trip through a list of independent criteria;
    /// exposing it here would let callers like <see cref="SimpleFilterUrlSerializer"/>
    /// silently rebuild it as an AND on reload). Root AND criteria are exposed
    /// even when sub-groups are present, so URL sync keeps the flat additive
    /// criteria shareable alongside an advanced nested branch.
    /// </summary>
    public IReadOnlyList<FilterCriterion> SimpleFilters
    {
        get
        {
            var current = AdvancedFilter;
            if (current is null || current.Logic != FilterLogic.And)
            {
                return Array.Empty<FilterCriterion>();
            }

            return current.Criteria;
        }
    }

    /// <summary>
    /// Returns the total count of real filter criteria in <see cref="AdvancedFilter"/>,
    /// excluding the global search. Used for the badge count (DF-03).
    /// </summary>
    public int CriteriaCount
        => AdvancedFilter is null ? 0 : CountCriteria(AdvancedFilter);

    /// <summary>Returns true when any filter source is active.</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(GlobalSearch)
        || AdvancedFilter is not null;

    /// <summary>
    /// Appends a criterion to the filter state. When the advanced filter is empty
    /// or its root is AND, the criterion is appended to the root Criteria list
    /// (sub-groups, if any, are kept intact). When the root logic is OR, the
    /// existing filter is wrapped as a sub-group of a new AND root so the new
    /// criterion AND-combines with whatever the existing expression was.
    /// </summary>
    public void AddSimpleFilter(FilterCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        var current = AdvancedFilter;
        if (current is null)
        {
            AdvancedFilter = new FilterGroup(FilterLogic.And, new[] { criterion });
            return;
        }

        if (current.Logic == FilterLogic.And)
        {
            var merged = new List<FilterCriterion>(current.Criteria.Count + 1);
            merged.AddRange(current.Criteria);
            merged.Add(criterion);
            AdvancedFilter = new FilterGroup(FilterLogic.And, merged, current.SubGroups);
            return;
        }

        // Root is OR: wrap the existing expression under a new AND root so the
        // caller's "additive AND" contract is preserved.
        AdvancedFilter = new FilterGroup(
            FilterLogic.And,
            new[] { criterion },
            new[] { current });
    }

    /// <summary>
    /// Removes the first criterion equal to <paramref name="criterion"/> from
    /// the root AND group of <see cref="AdvancedFilter"/>. No-op when the
    /// advanced filter is null, the root logic is OR, or the criterion is
    /// absent.
    /// </summary>
    public void RemoveSimpleFilter(FilterCriterion criterion)
    {
        var current = AdvancedFilter;
        if (current is null || current.Logic != FilterLogic.And)
        {
            return;
        }

        var idx = -1;
        for (var i = 0; i < current.Criteria.Count; i++)
        {
            if (Equals(current.Criteria[i], criterion))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
        {
            return;
        }

        RemoveSimpleFilterAt(idx);
    }

    /// <summary>
    /// Removes the root AND criterion at the given index, preserving sub-groups
    /// if any. No-op when the advanced filter is null, the root logic is OR, or
    /// the index is out of range. Clears <see cref="AdvancedFilter"/> entirely
    /// when the removal leaves the root empty (no criteria and no sub-groups).
    /// </summary>
    public void RemoveSimpleFilterAt(int index)
    {
        var current = AdvancedFilter;
        if (current is null || current.Logic != FilterLogic.And)
        {
            return;
        }

        if (index < 0 || index >= current.Criteria.Count)
        {
            return;
        }

        var remaining = new List<FilterCriterion>(current.Criteria.Count - 1);
        for (var i = 0; i < current.Criteria.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            remaining.Add(current.Criteria[i]);
        }

        var hasSubGroups = current.SubGroups is { Count: > 0 };
        if (remaining.Count == 0 && !hasSubGroups)
        {
            AdvancedFilter = null;
            return;
        }

        AdvancedFilter = new FilterGroup(FilterLogic.And, remaining, current.SubGroups);
    }

    /// <summary>
    /// Replaces the root AND criterion at the given index in place, preserving
    /// chip order and sub-groups. No-op when the advanced filter is null, the
    /// root logic is OR, or the index is out of range.
    /// </summary>
    public void ReplaceSimpleFilterAt(int index, FilterCriterion criterion)
    {
        ArgumentNullException.ThrowIfNull(criterion);

        var current = AdvancedFilter;
        if (current is null || current.Logic != FilterLogic.And)
        {
            return;
        }

        if (index < 0 || index >= current.Criteria.Count)
        {
            return;
        }

        var replaced = new List<FilterCriterion>(current.Criteria);
        replaced[index] = criterion;
        AdvancedFilter = new FilterGroup(FilterLogic.And, replaced, current.SubGroups);
    }

    /// <summary>
    /// Clears the root AND criteria of <see cref="AdvancedFilter"/>, preserving
    /// any sub-groups. If the removal leaves the tree empty, the advanced filter
    /// is set to null. No-op when the root logic is OR (there are no additive
    /// root criteria to clear).
    /// </summary>
    public void ClearSimpleFilters()
    {
        var current = AdvancedFilter;
        if (current is null || current.Logic != FilterLogic.And)
        {
            return;
        }

        var hasSubGroups = current.SubGroups is { Count: > 0 };
        if (!hasSubGroups)
        {
            AdvancedFilter = null;
            return;
        }

        if (current.Criteria.Count == 0)
        {
            return;
        }

        AdvancedFilter = new FilterGroup(
            FilterLogic.And,
            Array.Empty<FilterCriterion>(),
            current.SubGroups);
    }

    /// <summary>Clears all filter sources.</summary>
    public void ClearAll()
    {
        GlobalSearch = null;
        AdvancedFilter = null;
    }

    private static int CountCriteria(FilterGroup group)
    {
        var count = group.Criteria.Count;
        if (group.SubGroups is not null)
        {
            foreach (var sub in group.SubGroups)
            {
                count += CountCriteria(sub);
            }
        }

        return count;
    }
}
