namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

/// <summary>Double de <see cref="IAuditQueries"/> renvoyant des jeux fixés (tests bUnit des pages Audit, RB6 P2).</summary>
internal sealed class FakeAuditQueries : IAuditQueries
{
    private readonly IReadOnlyList<AuditSearchResultDto> _entries;
    private readonly IReadOnlyList<AuditPolicyDto> _policies;
    private readonly ActivityDto? _activity;
    private readonly IReadOnlyList<FieldChangeDto> _fieldChanges;

    public FakeAuditQueries(
        IReadOnlyList<AuditSearchResultDto>? entries = null,
        IReadOnlyList<AuditPolicyDto>? policies = null,
        ActivityDto? activity = null,
        IReadOnlyList<FieldChangeDto>? fieldChanges = null)
    {
        _entries = entries ?? [];
        _policies = policies ?? [];
        _activity = activity;
        _fieldChanges = fieldChanges ?? [];
    }

    public Task<IReadOnlyList<FieldChangeDto>> GetFieldChanges(string entityType, string entityId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(_fieldChanges);

    public Task<IReadOnlyList<AuditPolicyDto>> GetAuditPolicies(CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies);

    public Task<AuditPolicyDto?> GetPolicyByEntityType(string entityType, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuditPolicyDto?>(_policies.Count == 0 ? null : _policies[0]);

    public Task<AuditPolicyDto?> GetPolicyById(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies.FirstOrDefault(p => p.Id == id) ?? (_policies.Count == 0 ? null : _policies[0]));

    public Task<IReadOnlyList<ActivityDto>> GetActivities(string entityType, string entityId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDto>>(_activity is null ? [] : [_activity]);

    public Task<ActivityDto?> GetActivityById(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_activity);

    public Task<IReadOnlyList<AuditSearchResultDto>> SearchEntries(string? actorId = null, string? entityType = null, string? activityType = null, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null, string? searchText = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries);

    public Task<IReadOnlyList<FieldChangeDto>> GetFieldChangesByEntryId(Guid entryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_fieldChanges);

    public Task<IReadOnlyList<FieldChangeDto>> GetCorrelatedFieldChanges(string entityType, string entityId, DateTimeOffset activityTime, CancellationToken cancellationToken = default) =>
        Task.FromResult(_fieldChanges);
}
