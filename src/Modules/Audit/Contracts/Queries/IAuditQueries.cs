namespace Stratum.Modules.Audit.Contracts.Queries;

using Stratum.Modules.Audit.Contracts.DTOs;

public interface IAuditQueries
{
    Task<IReadOnlyList<FieldChangeDto>> GetFieldChanges(
        string entityType,
        string entityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditPolicyDto>> GetAuditPolicies(
        CancellationToken cancellationToken = default);

    Task<AuditPolicyDto?> GetPolicyByEntityType(
        string entityType,
        CancellationToken cancellationToken = default);

    Task<AuditPolicyDto?> GetPolicyById(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityDto>> GetActivities(
        string entityType,
        string entityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ActivityDto?> GetActivityById(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditSearchResultDto>> SearchEntries(
        string? actorId = null,
        string? entityType = null,
        string? activityType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? searchText = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FieldChangeDto>> GetFieldChangesByEntryId(
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FieldChangeDto>> GetCorrelatedFieldChanges(
        string entityType,
        string entityId,
        DateTimeOffset activityTime,
        CancellationToken cancellationToken = default);
}
