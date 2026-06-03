namespace Stratum.Common.Abstractions.Grid;

using System.Globalization;

/// <summary>
/// Structural equality for <see cref="FilterGroup"/>.
/// The default C# record equality does NOT compare nested list contents
/// (Criteria/SubGroups are reference-compared), so a dedicated comparer
/// is required to detect "dirty" state in the filter builder between a
/// loaded saved filter and the user's working edits.
/// </summary>
public static class FilterGroupStructuralComparer
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="a"/> and <paramref name="b"/>
    /// have the same logic, the same ordered criteria, and the same nested
    /// sub-groups. Null and empty sub-group lists are treated as equivalent.
    /// </summary>
    public static bool Equals(FilterGroup? a, FilterGroup? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a.Logic != b.Logic)
        {
            return false;
        }

        if (!CriteriaEqual(a.Criteria, b.Criteria))
        {
            return false;
        }

        return SubGroupsEqual(a.SubGroups, b.SubGroups);
    }

    private static bool CriteriaEqual(IReadOnlyList<FilterCriterion> a, IReadOnlyList<FilterCriterion> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (!string.Equals(ca.Field, cb.Field, StringComparison.Ordinal))
            {
                return false;
            }

            if (ca.Operator != cb.Operator)
            {
                return false;
            }

            if (!ValueEqual(ca.Value, cb.Value))
            {
                return false;
            }

            if (!ValueEqual(ca.ValueEnd, cb.ValueEnd))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SubGroupsEqual(IReadOnlyList<FilterGroup>? a, IReadOnlyList<FilterGroup>? b)
    {
        var ac = a?.Count ?? 0;
        var bc = b?.Count ?? 0;
        if (ac != bc)
        {
            return false;
        }

        if (ac == 0)
        {
            return true;
        }

        for (var i = 0; i < ac; i++)
        {
            if (!Equals(a![i], b![i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        // Values may be deserialized from JSON (JsonElement, string, number, bool)
        // or typed in by the builder (string). Normalize via invariant-culture
        // string form so "100.5" round-trips across fr-FR / en-US threads
        // without flipping dirty detection.
        return string.Equals(
            InvariantString(a),
            InvariantString(b),
            StringComparison.Ordinal);
    }

    private static string InvariantString(object value)
    {
        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
