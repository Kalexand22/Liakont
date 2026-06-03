namespace Stratum.Common.Abstractions.Grid;

using System.Collections;
using System.Globalization;
using System.Text;

/// <summary>
/// Compact, human-readable URL query-string encoding for the simple filters of a
/// <see cref="GridFilterState"/> (GFI14).
/// </summary>
/// <remarks>
/// <para>
/// Format: one <c>filter</c> query parameter per criterion, shaped
/// <c>Field:op:value</c> (or <c>Field:between:value|valueEnd</c>). Values are
/// percent-encoded via <see cref="Uri.EscapeDataString"/> so arbitrary strings survive
/// the URL round-trip.
/// </para>
/// <para>
/// Example:
/// <code>?filter=Service:eq:finances&amp;filter=Amount:gt:5000</code>
/// </para>
/// <para>
/// Only the flat root criteria of <see cref="GridFilterState.AdvancedFilter"/>
/// (the "simple filters" projection) are exposed in the URL (spec DF-02).
/// Global search and nested sub-groups stay out — they would make the URL
/// unreadable and advanced groups already have their own preference-row
/// persistence path (GFI14 + GFI16).
/// </para>
/// <para>
/// Deserialization is lenient: unknown operators or malformed entries are skipped
/// rather than throwing, so a hand-edited or stale URL can never crash the page.
/// Values are returned as <see cref="string"/>? — the expression builder coerces
/// them to the target CLR type via <c>Convert.ChangeType</c>.
/// </para>
/// </remarks>
public static class SimpleFilterUrlSerializer
{
    /// <summary>Query parameter name used to carry one filter criterion per entry.</summary>
    public const string QueryParameterName = "filter";

    // Compact operator aliases — shorter than the enum name but still self-explaining
    // in a URL. Kept in sync with the FilterOperator enum.
    private static readonly Dictionary<FilterOperator, string> OperatorToCode = new()
    {
        [FilterOperator.Equals] = "eq",
        [FilterOperator.NotEquals] = "neq",
        [FilterOperator.Contains] = "ct",
        [FilterOperator.NotContains] = "nct",
        [FilterOperator.StartsWith] = "sw",
        [FilterOperator.EndsWith] = "ew",
        [FilterOperator.GreaterThan] = "gt",
        [FilterOperator.GreaterThanOrEqual] = "gte",
        [FilterOperator.LessThan] = "lt",
        [FilterOperator.LessThanOrEqual] = "lte",
        [FilterOperator.Between] = "bt",
        [FilterOperator.NotBetween] = "nbt",
        [FilterOperator.Before] = "before",
        [FilterOperator.After] = "after",
        [FilterOperator.In] = "in",
        [FilterOperator.NotIn] = "nin",
        [FilterOperator.IsNull] = "null",
        [FilterOperator.IsNotNull] = "nnull",
        [FilterOperator.RelativePeriod] = "rel",
    };

    private static readonly Dictionary<string, FilterOperator> CodeToOperator = BuildReverse();

    /// <summary>
    /// Serializes the simple filters of <paramref name="state"/> into a query string
    /// fragment (without leading <c>?</c>). Returns <see cref="string.Empty"/> when
    /// there are no simple filters to share, OR when the advanced filter contains
    /// sub-groups — in that case we deliberately keep the URL empty so that shared
    /// deep links stay self-contained and deterministic (GFI16): publishing the
    /// root criteria alone would let the receiver's restore path combine them with
    /// their own saved advanced branch and produce a different result set.
    /// </summary>
    public static string Serialize(GridFilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.SimpleFilters.Count == 0)
        {
            return string.Empty;
        }

        if (state.AdvancedFilter?.SubGroups is { Count: > 0 })
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var criterion in state.SimpleFilters)
        {
            var encoded = EncodeCriterion(criterion);
            if (encoded is null)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(QueryParameterName).Append('=').Append(Uri.EscapeDataString(encoded));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns the absolute URL that a browser should navigate to in order to
    /// mirror the current <paramref name="state"/> in the query string. Preserves
    /// any existing non-<c>filter</c> query parameters from <paramref name="currentUri"/>.
    /// </summary>
    public static string BuildUriWithFilters(string currentUri, GridFilterState state)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentUri);
        ArgumentNullException.ThrowIfNull(state);

        var queryStart = currentUri.IndexOf('?', StringComparison.Ordinal);
        var hashStart = currentUri.IndexOf('#', StringComparison.Ordinal);

        string basePart;
        string existingQuery;
        string hashPart;

        if (queryStart < 0)
        {
            basePart = hashStart < 0 ? currentUri : currentUri[..hashStart];
            existingQuery = string.Empty;
            hashPart = hashStart < 0 ? string.Empty : currentUri[hashStart..];
        }
        else
        {
            basePart = currentUri[..queryStart];
            var queryEnd = hashStart < 0 ? currentUri.Length : hashStart;
            existingQuery = currentUri.Substring(queryStart + 1, queryEnd - queryStart - 1);
            hashPart = hashStart < 0 ? string.Empty : currentUri[hashStart..];
        }

        // Rebuild the query string: keep every non-filter parameter verbatim, then
        // append the freshly serialized filter entries. Order is stable so the URL
        // does not jitter between two equivalent states.
        var preserved = new StringBuilder();
        if (existingQuery.Length > 0)
        {
            foreach (var part in existingQuery.Split('&'))
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                var eq = part.IndexOf('=', StringComparison.Ordinal);
                var name = eq < 0 ? part : part[..eq];
                if (string.Equals(name, QueryParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (preserved.Length > 0)
                {
                    preserved.Append('&');
                }

                preserved.Append(part);
            }
        }

        var filterQuery = Serialize(state);

        var finalQuery = (preserved.Length, filterQuery.Length) switch
        {
            (0, 0) => string.Empty,
            (0, _) => "?" + filterQuery,
            (_, 0) => "?" + preserved,
            _ => "?" + preserved + "&" + filterQuery,
        };

        return basePart + finalQuery + hashPart;
    }

    /// <summary>
    /// Parses the <paramref name="query"/> portion of a URL (leading <c>?</c> is
    /// tolerated) and returns the simple <see cref="FilterCriterion"/> entries it
    /// encodes. Values are returned as <see cref="string"/>? so callers can coerce
    /// them against column type metadata.
    /// </summary>
    public static IReadOnlyList<FilterCriterion> Parse(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<FilterCriterion>();
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        if (trimmed.Length == 0)
        {
            return Array.Empty<FilterCriterion>();
        }

        var result = new List<FilterCriterion>();
        foreach (var segment in trimmed.Split('&'))
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            var name = segment[..eq];
            if (!string.Equals(name, QueryParameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = Uri.UnescapeDataString(segment[(eq + 1)..]);
            var criterion = DecodeCriterion(raw);
            if (criterion is not null)
            {
                result.Add(criterion);
            }
        }

        return result;
    }

    /// <summary>
    /// Convenience overload that replaces the simple filters of
    /// <paramref name="state"/> with whatever the <paramref name="query"/> encodes.
    /// Leaves global search and advanced filter untouched.
    /// </summary>
    public static void ApplyToState(GridFilterState state, string? query)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.ClearSimpleFilters();
        foreach (var criterion in Parse(query))
        {
            state.AddSimpleFilter(criterion);
        }
    }

    // ── internals ───────────────────────────────────────────────────
    private static Dictionary<string, FilterOperator> BuildReverse()
    {
        var map = new Dictionary<string, FilterOperator>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in OperatorToCode)
        {
            map[kv.Value] = kv.Key;
        }

        return map;
    }

    private static string? EncodeCriterion(FilterCriterion criterion)
    {
        if (!OperatorToCode.TryGetValue(criterion.Operator, out var opCode))
        {
            return null;
        }

        if (criterion.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
        {
            return $"{criterion.Field}:{opCode}:";
        }

        var valueText = FormatValue(criterion.Value);
        if (criterion.Operator is FilterOperator.Between or FilterOperator.NotBetween)
        {
            var endText = FormatValue(criterion.ValueEnd);
            return $"{criterion.Field}:{opCode}:{valueText}|{endText}";
        }

        return $"{criterion.Field}:{opCode}:{valueText}";
    }

    private static FilterCriterion? DecodeCriterion(string raw)
    {
        // field:op:value  (or field:op: for null/not null)
        // value may itself contain ':' — we only split on the first two.
        var first = raw.IndexOf(':', StringComparison.Ordinal);
        if (first <= 0)
        {
            return null;
        }

        var second = raw.IndexOf(':', first + 1);
        if (second < 0)
        {
            return null;
        }

        var field = raw[..first];
        var opCode = raw.Substring(first + 1, second - first - 1);
        var value = raw[(second + 1)..];

        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        if (!CodeToOperator.TryGetValue(opCode, out var op))
        {
            return null;
        }

        if (op is FilterOperator.IsNull or FilterOperator.IsNotNull)
        {
            return new FilterCriterion(field, op, Value: null);
        }

        if (op is FilterOperator.Between or FilterOperator.NotBetween)
        {
            var pipe = value.IndexOf('|', StringComparison.Ordinal);
            if (pipe < 0)
            {
                return null;
            }

            var start = value[..pipe];
            var end = value[(pipe + 1)..];
            return new FilterCriterion(field, op, start, end);
        }

        if (op is FilterOperator.In or FilterOperator.NotIn)
        {
            // List filters are comma-joined on the wire. FilterExpressionBuilder.BuildIn
            // requires an IEnumerable for the Value — a plain string would be treated
            // as IEnumerable<char> and each character would be matched individually.
            // Split on ',' and materialize to List<string>; ConvertConstant inside the
            // expression builder coerces each item to the target column type.
            // Limitation: individual list items cannot contain a literal comma when
            // round-tripped through the URL; structured list values belong in the
            // preference blob (GridFilterStateSerializer, jsonb).
            var items = value.Length == 0
                ? new List<string>()
                : new List<string>(value.Split(','));
            return new FilterCriterion(field, op, items);
        }

        return new FilterCriterion(field, op, value);
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        // IEnumerable must be matched BEFORE the scalar switch so lists produced by
        // FilterOperator.In / NotIn round-trip. The check excludes string (which is
        // itself IEnumerable<char>) so plain string values still hit the scalar path.
        if (value is not string && value is IEnumerable enumerable)
        {
            var sb = new StringBuilder();
            foreach (var item in enumerable)
            {
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }

                sb.Append(FormatScalar(item));
            }

            return sb.ToString();
        }

        return FormatScalar(value);
    }

    private static string FormatScalar(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
