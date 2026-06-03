namespace Stratum.Common.Infrastructure.GridPreferences;

using System.Globalization;
using Dapper;

/// <summary>
/// Builds a parameterized SQL WHERE clause that ORs ILIKE across multiple columns
/// for full-text search. Uses a field-to-column whitelist to prevent SQL injection.
/// Columns not in the whitelist are silently skipped (logged as warning by caller).
/// </summary>
public sealed class FullTextSearchSqlBuilder
{
    private readonly IReadOnlyDictionary<string, string> _fieldMap;

    /// <summary>
    /// Initializes the builder with a mapping from DTO property paths to SQL column expressions.
    /// </summary>
    /// <param name="fieldMap">
    /// Keys are DTO property paths (case-insensitive, e.g. "LegalName", "Party.LegalName").
    /// Values are SQL column references (e.g. "p.legal_name").
    /// </param>
    public FullTextSearchSqlBuilder(IReadOnlyDictionary<string, string> fieldMap)
    {
        _fieldMap = fieldMap ?? throw new ArgumentNullException(nameof(fieldMap));
    }

    /// <summary>
    /// Builds a SQL WHERE clause (without the WHERE keyword) that ORs ILIKE
    /// across all resolvable search columns. Returns null if no columns could be resolved.
    /// </summary>
    /// <param name="searchTerm">The user's search text.</param>
    /// <param name="searchColumns">Property paths of columns to search across.</param>
    /// <param name="parameters">DynamicParameters to add the search parameter to.</param>
    /// <param name="skippedFields">
    /// Property paths that were requested but not found in the field map.
    /// Callers can log these as warnings.
    /// </param>
    public string? Build(
        string searchTerm,
        IReadOnlyList<string> searchColumns,
        DynamicParameters parameters,
        out IReadOnlyList<string> skippedFields)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var skipped = new List<string>();
        skippedFields = skipped;

        if (string.IsNullOrWhiteSpace(searchTerm) || searchColumns is not { Count: > 0 })
        {
            return null;
        }

        var escaped = searchTerm
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        var paramName = "fts";
        parameters.Add(paramName, $"%{escaped}%");

        var parts = new List<string>();
        foreach (var field in searchColumns)
        {
            if (_fieldMap.TryGetValue(field, out var column))
            {
                // CAST ensures ILIKE works for non-text columns (e.g. integer, numeric).
                // PostgreSQL optimizes CAST(text_col AS TEXT) to a no-op.
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"CAST({column} AS TEXT) ILIKE @{paramName} ESCAPE '\\'"));
            }
            else
            {
                skipped.Add(field);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" OR ", parts);
    }
}
