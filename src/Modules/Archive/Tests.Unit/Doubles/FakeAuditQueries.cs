namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Audit.Contracts.DTOs;
using Stratum.Modules.Audit.Contracts.Queries;

/// <summary>
/// Double d'<see cref="IAuditQueries"/> pour le test de réversibilité (API03). Le journal opérateur est vide
/// (le test vérifie la PRÉSENCE du fichier + la note de plafond, pas le contenu, couvert ailleurs).
/// </summary>
internal sealed class FakeAuditQueries : IAuditQueries
{
    public Task<IReadOnlyList<FieldChangeDto>> GetFieldChanges(string entityType, string entityId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FieldChangeDto>>([]);

    public Task<IReadOnlyList<AuditPolicyDto>> GetAuditPolicies(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditPolicyDto>>([]);

    public Task<AuditPolicyDto?> GetPolicyByEntityType(string entityType, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuditPolicyDto?>(null);

    public Task<AuditPolicyDto?> GetPolicyById(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuditPolicyDto?>(null);

    public Task<IReadOnlyList<ActivityDto>> GetActivities(string entityType, string entityId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActivityDto>>([]);

    public Task<ActivityDto?> GetActivityById(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActivityDto?>(null);

    public Task<IReadOnlyList<AuditSearchResultDto>> SearchEntries(
        string? actorId = null,
        string? entityType = null,
        string? activityType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? searchText = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditSearchResultDto>>([]);

    public Task<IReadOnlyList<FieldChangeDto>> GetFieldChangesByEntryId(Guid entryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FieldChangeDto>>([]);

    public Task<IReadOnlyList<FieldChangeDto>> GetCorrelatedFieldChanges(string entityType, string entityId, DateTimeOffset activityTime, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FieldChangeDto>>([]);
}
