namespace Stratum.Common.Abstractions.Queries;

using Stratum.Common.Abstractions.Grid;

/// <summary>
/// Standardized query model for server-side paging, sorting, and filtering.
/// Used as the contract between UI pages and module query handlers.
/// </summary>
/// <remarks>
/// <para>Validation is the responsibility of each query handler, not this record.</para>
/// <para><see cref="Page"/> must be >= 1. <see cref="PageSize"/> must be >= 1 and bounded
/// by the handler (recommended max: 200).</para>
/// <para><see cref="Filters"/> uses reference equality in record comparisons.
/// Two instances with identical filter entries but different dictionary references
/// will not compare equal via <c>Equals</c>.</para>
/// </remarks>
public sealed record ListQuery
{
    public string? Search { get; init; }

    /// <summary>Page number (1-based). Must be >= 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page. Must be >= 1; handlers should cap at a reasonable max (e.g. 200).</summary>
    public int PageSize { get; init; } = 25;

    public string? SortField { get; init; }

    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;

    /// <summary>
    /// Optional structured filters as key-value pairs.
    /// Note: uses reference equality in record comparisons.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Filters { get; init; }

    /// <summary>
    /// Optional advanced filter expression from the FilterBuilder UI.
    /// When set, query handlers use <see cref="IFilterExpressionBuilder{TItem}"/>
    /// to convert this to a LINQ predicate. Coexists with <see cref="Filters"/>
    /// and <see cref="Search"/> — all are additive (AND).
    /// </summary>
    public FilterGroup? AdvancedFilter { get; init; }

    /// <summary>
    /// Property paths of visible columns to include in full-text search when
    /// <see cref="Search"/> is set. When non-empty, query handlers search across
    /// ALL listed columns (OR). When null or empty, query handlers fall back to
    /// their default search fields for backward compatibility.
    /// </summary>
    public IReadOnlyList<string>? SearchColumns { get; init; }
}
