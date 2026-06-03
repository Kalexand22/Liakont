namespace Stratum.Common.Abstractions.Domain;

/// <summary>
/// Convention marker for entities that carry an application-managed optimistic-concurrency counter.
/// The value is incremented by the application on each write and must be included in UPDATE WHERE
/// clauses to detect concurrent modifications.
/// <para>
/// <c>long</c> is used (not <c>uint</c>) to keep the type idiomatic in C# and to avoid unsigned
/// arithmetic pitfalls. Infrastructure maps this to a <c>bigint</c> column, never to PostgreSQL
/// <c>xmin</c>.
/// </para>
/// </summary>
public interface IVersioned
{
    long RowVersion { get; }
}
