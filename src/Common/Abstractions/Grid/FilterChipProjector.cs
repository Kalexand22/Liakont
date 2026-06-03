namespace Stratum.Common.Abstractions.Grid;

using System.Globalization;

/// <summary>
/// Projects a <see cref="GridFilterState"/> into an ordered list of
/// <see cref="FilterChipModel"/> for display in the chip bar.
/// Order: global search → root criteria of the unified filter tree (DF-08).
/// Flat root criteria are tagged <see cref="FilterSource.Simple"/> so they
/// route back to the simple builder for edit; a non-empty sub-group tree
/// collapses to a single advanced summary chip (DF-05).
/// </summary>
public static class FilterChipProjector
{
    private static readonly Dictionary<RelativeDatePeriod, string> RelativePeriodLabels = new()
    {
        [RelativeDatePeriod.Today] = "aujourd'hui",
        [RelativeDatePeriod.Yesterday] = "hier",
        [RelativeDatePeriod.Last7Days] = "7 derniers jours",
        [RelativeDatePeriod.Last30Days] = "30 derniers jours",
        [RelativeDatePeriod.ThisMonth] = "ce mois",
        [RelativeDatePeriod.LastMonth] = "mois dernier",
        [RelativeDatePeriod.ThisQuarter] = "ce trimestre",
        [RelativeDatePeriod.ThisYear] = "cette année",
    };

    /// <summary>
    /// Projects the filter state into an ordered list of chip models.
    /// </summary>
    public static IReadOnlyList<FilterChipModel> Project(GridFilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var chips = new List<FilterChipModel>();

        // 1. Global search chip (DF-08: first)
        if (!string.IsNullOrWhiteSpace(state.GlobalSearch))
        {
            chips.Add(new FilterChipModel(
                Label: $"\"{state.GlobalSearch}\"",
                Source: FilterSource.GlobalSearch));
        }

        // 2. Root-level criteria of the unified filter tree (DF-08: second).
        // When the root has no sub-groups, each criterion is an editable simple
        // chip; otherwise the whole tree collapses to a summary chip.
        if (state.AdvancedFilter is not null)
        {
            ProjectAdvancedFilter(state.AdvancedFilter, chips);
        }

        return chips;
    }

    /// <summary>
    /// Returns the badge count: total real criteria excluding global search (DF-03).
    /// </summary>
    public static int GetBadgeCount(GridFilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.CriteriaCount;
    }

    /// <summary>
    /// Formats a criterion into a human-readable French label for chip display.
    /// </summary>
    public static string FormatCriterionLabel(FilterCriterion criterion)
    {
        var field = criterion.Field;
        var op = criterion.Operator;

        return op switch
        {
            FilterOperator.IsNull => $"{field}: vide",
            FilterOperator.IsNotNull => $"{field}: renseigné",
            FilterOperator.Equals => FormatEqualsLabel(field, criterion.Value),
            FilterOperator.NotEquals => $"{field} ≠ {FormatValue(criterion.Value)}",
            FilterOperator.Contains => $"{field} contient \"{criterion.Value}\"",
            FilterOperator.NotContains => $"{field} ne contient pas \"{criterion.Value}\"",
            FilterOperator.StartsWith => $"{field} commence par \"{criterion.Value}\"",
            FilterOperator.EndsWith => $"{field} se termine par \"{criterion.Value}\"",
            FilterOperator.GreaterThan => $"{field} > {FormatValue(criterion.Value)}",
            FilterOperator.GreaterThanOrEqual => $"{field} ≥ {FormatValue(criterion.Value)}",
            FilterOperator.LessThan => $"{field} < {FormatValue(criterion.Value)}",
            FilterOperator.LessThanOrEqual => $"{field} ≤ {FormatValue(criterion.Value)}",
            FilterOperator.Between => $"{field}: {FormatValue(criterion.Value)} → {FormatValue(criterion.ValueEnd)}",
            FilterOperator.NotBetween => $"{field}: pas entre {FormatValue(criterion.Value)} et {FormatValue(criterion.ValueEnd)}",
            FilterOperator.Before => $"{field}: avant {FormatValue(criterion.Value)}",
            FilterOperator.After => $"{field}: après {FormatValue(criterion.Value)}",
            FilterOperator.In => FormatInLabel(field, criterion.Value),
            FilterOperator.NotIn => FormatNotInLabel(field, criterion.Value),
            FilterOperator.RelativePeriod => FormatRelativePeriodLabel(field, criterion.Value),
            _ => $"{field}: {FilterOperatorLabels.GetLabel(op)} {FormatValue(criterion.Value)}",
        };
    }

    private static void ProjectAdvancedFilter(FilterGroup group, List<FilterChipModel> chips)
    {
        var hasSubGroups = group.SubGroups is { Count: > 0 };
        var isAndRoot = group.Logic == FilterLogic.And;

        // DF-05 / GFI16: root AND criteria project as individual simple chips,
        // regardless of whether sub-groups are also present. A flat OR root, by
        // contrast, cannot be decomposed into independent chips without flipping
        // semantics, so it falls through to the summary path below.
        if (isAndRoot)
        {
            foreach (var criterion in group.Criteria)
            {
                chips.Add(new FilterChipModel(
                    Label: FormatCriterionLabel(criterion),
                    Source: FilterSource.Simple,
                    Criterion: criterion,
                    Group: group));
            }
        }

        // DF-05: when we still have anything non-chipped (sub-groups or a flat
        // OR root), add a single "advanced" summary chip that covers the whole
        // non-chipped portion. For an AND root with sub-groups, the summary
        // counts only the sub-group criteria so the badge matches what users
        // can actually edit from the advanced builder alone.
        if (!hasSubGroups && isAndRoot)
        {
            return;
        }

        var summaryCount = isAndRoot
            ? CountSubGroupCriteria(group)
            : CountAllCriteria(group);

        if (summaryCount == 0)
        {
            return;
        }

        chips.Add(new FilterChipModel(
            Label: $"Filtres avancés ({summaryCount} critères)",
            Source: FilterSource.Advanced,
            Group: group));
    }

    private static int CountSubGroupCriteria(FilterGroup group)
    {
        if (group.SubGroups is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var sub in group.SubGroups)
        {
            count += CountAllCriteria(sub);
        }

        return count;
    }

    private static string FormatEqualsLabel(string field, object? value)
    {
        if (value is true)
        {
            return $"{field}: Oui";
        }

        if (value is false)
        {
            return $"{field}: Non";
        }

        return $"{field}: {FormatValue(value)}";
    }

    private static string FormatInLabel(string field, object? value)
    {
        if (value is not System.Collections.IEnumerable enumerable || value is string)
        {
            return $"{field}: {FormatValue(value)}";
        }

        var items = new List<string>();
        foreach (var item in enumerable)
        {
            items.Add(FormatValue(item));
        }

        if (items.Count > 3)
        {
            return $"{field}: {items.Count} sélectionnés";
        }

        return $"{field}: {string.Join(", ", items)}";
    }

    private static string FormatNotInLabel(string field, object? value)
    {
        if (value is not System.Collections.IEnumerable enumerable || value is string)
        {
            return $"{field}: pas parmi {FormatValue(value)}";
        }

        var items = new List<string>();
        foreach (var item in enumerable)
        {
            items.Add(FormatValue(item));
        }

        if (items.Count > 3)
        {
            return $"{field}: pas parmi {items.Count} valeurs";
        }

        return $"{field}: pas parmi {string.Join(", ", items)}";
    }

    private static string FormatRelativePeriodLabel(string field, object? value)
    {
        if (value is RelativeDatePeriod period && RelativePeriodLabels.TryGetValue(period, out var label))
        {
            return $"{field}: {label}";
        }

        if (value is string s && Enum.TryParse<RelativeDatePeriod>(s, ignoreCase: true, out var parsed)
            && RelativePeriodLabels.TryGetValue(parsed, out var parsedLabel))
        {
            return $"{field}: {parsedLabel}";
        }

        return $"{field}: {FormatValue(value)}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "vide",
            string s => s,
            DateTime dt => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            decimal m => m.ToString("G", CultureInfo.GetCultureInfo("fr-FR")),
            double d => d.ToString("G", CultureInfo.GetCultureInfo("fr-FR")),
            float f => f.ToString("G", CultureInfo.GetCultureInfo("fr-FR")),
            _ => value.ToString() ?? "vide",
        };
    }

    private static int CountAllCriteria(FilterGroup group)
    {
        var count = group.Criteria.Count;
        if (group.SubGroups is not null)
        {
            foreach (var sub in group.SubGroups)
            {
                count += CountAllCriteria(sub);
            }
        }

        return count;
    }
}
