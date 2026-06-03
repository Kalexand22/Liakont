namespace Stratum.Common.Infrastructure.GridPreferences;

using System.Globalization;
using Dapper;
using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Translates a <see cref="FilterGroup"/> into a parameterized SQL WHERE clause
/// for use with Dapper queries. Uses a field-to-column whitelist to prevent SQL injection.
/// </summary>
public sealed class FilterGroupSqlBuilder
{
    private readonly IReadOnlyDictionary<string, string> _fieldMap;
    private readonly DynamicParameters _parameters = new();
    private int _paramIndex;

    /// <summary>
    /// Initializes a new builder with a mapping from DTO property paths to SQL column expressions.
    /// </summary>
    /// <param name="fieldMap">
    /// Keys are DTO property paths (case-insensitive, e.g. "LegalName", "Party.LegalName").
    /// Values are SQL column references (e.g. "p.legal_name").
    /// </param>
    public FilterGroupSqlBuilder(IReadOnlyDictionary<string, string> fieldMap)
    {
        _fieldMap = fieldMap ?? throw new ArgumentNullException(nameof(fieldMap));
    }

    /// <summary>
    /// Builds a SQL WHERE clause (without the WHERE keyword) and its parameters.
    /// Returns null if the filter group produces no conditions.
    /// </summary>
    public (string? Sql, DynamicParameters Parameters) Build(FilterGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var sql = BuildGroup(group);
        return string.IsNullOrWhiteSpace(sql) ? (null, _parameters) : (sql, _parameters);
    }

    private static object? ConvertValue(object? value)
    {
        return value switch
        {
            null => null,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) => dt,
            string s when decimal.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
            string s when bool.TryParse(s, out var b) => b,
            string s => s,
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => dt,
            bool b => b,
            _ when IsNumeric(value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static bool IsNumeric(object value)
    {
        return value is byte or sbyte or short or ushort or int or uint
            or long or ulong or float or double or decimal;
    }

    private string NextParam() => $"af{_paramIndex++}";

    private string? BuildGroup(FilterGroup group)
    {
        var parts = new List<string>();

        foreach (var criterion in group.Criteria)
        {
            var sql = BuildCriterion(criterion);
            if (sql is not null)
            {
                parts.Add(sql);
            }
        }

        if (group.SubGroups is { Count: > 0 })
        {
            foreach (var subGroup in group.SubGroups)
            {
                var sql = BuildGroup(subGroup);
                if (sql is not null)
                {
                    parts.Add($"({sql})");
                }
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var connector = group.Logic == FilterLogic.And ? " AND " : " OR ";
        return string.Join(connector, parts);
    }

    private string? BuildCriterion(FilterCriterion criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion.Field))
        {
            return null;
        }

        if (!_fieldMap.TryGetValue(criterion.Field, out var column))
        {
            // Unknown field — skip silently (security: never interpolate unknown fields)
            return null;
        }

        return criterion.Operator switch
        {
            FilterOperator.Equals => BuildEquals(column, criterion.Value),
            FilterOperator.NotEquals => BuildNotEquals(column, criterion.Value),
            FilterOperator.Contains => BuildLike(column, criterion.Value, "%{0}%"),
            FilterOperator.NotContains => BuildNotLike(column, criterion.Value, "%{0}%"),
            FilterOperator.StartsWith => BuildLike(column, criterion.Value, "{0}%"),
            FilterOperator.EndsWith => BuildLike(column, criterion.Value, "%{0}"),
            FilterOperator.GreaterThan => BuildComparison(column, criterion.Value, ">"),
            FilterOperator.GreaterThanOrEqual => BuildComparison(column, criterion.Value, ">="),
            FilterOperator.LessThan => BuildComparison(column, criterion.Value, "<"),
            FilterOperator.LessThanOrEqual => BuildComparison(column, criterion.Value, "<="),
            FilterOperator.Between => BuildBetween(column, criterion.Value, criterion.ValueEnd),
            FilterOperator.NotBetween => BuildNotBetween(column, criterion.Value, criterion.ValueEnd),
            FilterOperator.In => BuildIn(column, criterion.Value),
            FilterOperator.NotIn => BuildNotIn(column, criterion.Value),
            FilterOperator.Before => BuildComparison(column, criterion.Value, "<"),
            FilterOperator.After => BuildComparison(column, criterion.Value, ">"),
            FilterOperator.RelativePeriod => BuildRelativePeriod(column, criterion.Value),
            FilterOperator.IsNull => $"{column} IS NULL",
            FilterOperator.IsNotNull => $"{column} IS NOT NULL",
            _ => null,
        };
    }

    private string BuildEquals(string column, object? value)
    {
        var paramName = NextParam();
        _parameters.Add(paramName, ConvertValue(value));
        return $"{column} = @{paramName}";
    }

    private string BuildNotEquals(string column, object? value)
    {
        var paramName = NextParam();
        _parameters.Add(paramName, ConvertValue(value));
        return $"{column} <> @{paramName}";
    }

    private string BuildLike(string column, object? value, string pattern)
    {
        var paramName = NextParam();
        var text = value?.ToString() ?? string.Empty;

        // Escape ILIKE special characters
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        _parameters.Add(paramName, string.Format(CultureInfo.InvariantCulture, pattern, escaped));
        return $"{column} ILIKE @{paramName} ESCAPE '\\'";
    }

    private string BuildComparison(string column, object? value, string op)
    {
        var paramName = NextParam();
        _parameters.Add(paramName, ConvertValue(value));
        return $"{column} {op} @{paramName}";
    }

    private string BuildBetween(string column, object? valueStart, object? valueEnd)
    {
        var paramStart = NextParam();
        var paramEnd = NextParam();
        _parameters.Add(paramStart, ConvertValue(valueStart));
        _parameters.Add(paramEnd, ConvertValue(valueEnd));
        return $"{column} >= @{paramStart} AND {column} <= @{paramEnd}";
    }

    private string BuildNotLike(string column, object? value, string pattern)
    {
        var paramName = NextParam();
        var text = value?.ToString() ?? string.Empty;
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        _parameters.Add(paramName, string.Format(CultureInfo.InvariantCulture, pattern, escaped));
        return $"{column} NOT ILIKE @{paramName} ESCAPE '\\'";
    }

    private string BuildNotBetween(string column, object? valueStart, object? valueEnd)
    {
        var paramStart = NextParam();
        var paramEnd = NextParam();
        _parameters.Add(paramStart, ConvertValue(valueStart));
        _parameters.Add(paramEnd, ConvertValue(valueEnd));
        return $"NOT ({column} >= @{paramStart} AND {column} <= @{paramEnd})";
    }

    private string? BuildIn(string column, object? value)
    {
        if (value is not System.Collections.IEnumerable enumerable)
        {
            return null;
        }

        var values = new List<object?>();
        foreach (var item in enumerable)
        {
            values.Add(ConvertValue(item));
        }

        if (values.Count == 0)
        {
            return "1 = 0"; // Empty IN → always false
        }

        var paramName = NextParam();
        _parameters.Add(paramName, values.ToArray());
        return $"{column} = ANY(@{paramName})";
    }

    private string? BuildNotIn(string column, object? value)
    {
        if (value is not System.Collections.IEnumerable enumerable)
        {
            return null;
        }

        var values = new List<object?>();
        foreach (var item in enumerable)
        {
            values.Add(ConvertValue(item));
        }

        if (values.Count == 0)
        {
            return "1 = 1"; // Empty NOT IN → always true
        }

        var paramName = NextParam();
        _parameters.Add(paramName, values.ToArray());
        return $"NOT ({column} = ANY(@{paramName}))";
    }

    private string? BuildRelativePeriod(string column, object? value)
    {
        RelativeDatePeriod period;
        if (value is RelativeDatePeriod p)
        {
            period = p;
        }
        else if (value is string s && Enum.TryParse<RelativeDatePeriod>(s, ignoreCase: true, out var parsed))
        {
            period = parsed;
        }
        else
        {
            return null;
        }

        var (start, end) = RelativeDatePeriodResolver.Resolve(period, DateTimeOffset.UtcNow);
        return BuildBetween(column, start.UtcDateTime, end.UtcDateTime);
    }
}
