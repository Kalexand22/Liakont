namespace Stratum.Common.Abstractions.Queries;

/// <summary>
/// Standardized result model for paginated list queries.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed record ListResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int TotalCount { get; init; }
}
