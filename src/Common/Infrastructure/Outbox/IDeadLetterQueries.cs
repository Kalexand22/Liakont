namespace Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Read-only queries over the outbox dead-letter table.
/// </summary>
public interface IDeadLetterQueries
{
    /// <summary>
    /// Returns a page of dead-letter events ordered by <c>moved_at</c> descending.
    /// </summary>
    Task<IReadOnlyList<DeadLetterEvent>> GetPagedAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single dead-letter event by its original event id, or <c>null</c> if not found.
    /// </summary>
    Task<DeadLetterEvent?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
