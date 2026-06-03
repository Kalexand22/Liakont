namespace Stratum.Common.Infrastructure.Database;

using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Helper for repositories that implement optimistic concurrency via <see cref="Abstractions.Domain.IVersioned"/>.
/// Call <see cref="EnsureUpdated"/> after every UPDATE that includes a
/// <c>WHERE row_version = @ExpectedRowVersion</c> clause. If zero rows were affected, the entity
/// was modified by another writer since it was loaded, and a <see cref="ConflictException"/> (HTTP 409) is thrown.
/// </summary>
public static class OptimisticLockGuard
{
    /// <summary>
    /// Asserts that an UPDATE affected exactly one row. Call this after executing an UPDATE
    /// statement whose WHERE clause includes <c>row_version = @ExpectedRowVersion</c>.
    /// </summary>
    /// <param name="affectedRows">The number of rows modified by the UPDATE.</param>
    /// <param name="entityType">A human-readable entity type name (e.g. "Company", "Party").</param>
    /// <param name="entityId">The primary key of the entity being updated.</param>
    /// <param name="expectedVersion">The row version that was expected.</param>
    /// <exception cref="ConflictException">
    /// Thrown when <paramref name="affectedRows"/> is zero, indicating a concurrent modification.
    /// The middleware maps this to HTTP 409 Conflict.
    /// </exception>
    public static void EnsureUpdated(int affectedRows, string entityType, object entityId, long expectedVersion)
    {
        if (affectedRows == 0)
        {
            throw new ConflictException(
                $"Concurrent modification detected on {entityType} '{entityId}': expected row version {expectedVersion}.");
        }
    }
}
