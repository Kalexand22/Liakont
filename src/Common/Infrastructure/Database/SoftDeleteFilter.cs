namespace Stratum.Common.Infrastructure.Database;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Reusable SQL helper for <see cref="Abstractions.Domain.ISoftDeletable"/> query filtering.
/// Repositories opt-in by appending the filter clause to their SQL queries.
/// <para>
/// <strong>Standard pattern:</strong> use <see cref="AndNotDeleted"/> in WHERE clauses
/// to exclude soft-deleted records. For queries that need deleted records, omit the filter
/// entirely (see <c>ListAllIncludingDeleted</c> pattern in coding-standards.md).
/// </para>
/// </summary>
public static class SoftDeleteFilter
{
    /// <summary>
    /// SQL predicate: <c>deleted_at IS NULL</c>.
    /// Use when the soft-delete check is the only condition or the first condition in a WHERE clause.
    /// </summary>
    public const string NotDeleted = "deleted_at IS NULL";

    private static readonly Regex ValidAlias = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Returns a soft-delete SQL predicate with an optional table alias.
    /// </summary>
    /// <param name="alias">Optional table alias (e.g., <c>"d"</c> → <c>"d.deleted_at IS NULL"</c>).</param>
    /// <returns>SQL predicate string.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias"/> contains invalid characters.</exception>
    public static string WhereNotDeleted(string? alias = null)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return NotDeleted;
        }

        ValidateAlias(alias);
        return $"{alias}.deleted_at IS NULL";
    }

    /// <summary>
    /// Returns <c>" AND deleted_at IS NULL"</c> for appending to an existing WHERE clause.
    /// </summary>
    /// <param name="alias">Optional table alias.</param>
    /// <returns>SQL fragment starting with <c>" AND "</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="alias"/> contains invalid characters.</exception>
    public static string AndNotDeleted(string? alias = null)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return $" AND {NotDeleted}";
        }

        ValidateAlias(alias);
        return $" AND {alias}.deleted_at IS NULL";
    }

    private static void ValidateAlias(string alias)
    {
        if (!ValidAlias.IsMatch(alias))
        {
            throw new ArgumentException(
                $"Table alias must be a valid SQL identifier (letters, digits, underscores): '{alias}'.",
                nameof(alias));
        }
    }
}
