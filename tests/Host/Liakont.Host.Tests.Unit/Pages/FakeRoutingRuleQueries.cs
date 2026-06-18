namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double de <see cref="IRoutingRuleQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeRoutingRuleQueries : IRoutingRuleQueries
{
    private readonly IReadOnlyList<RoutingRuleDto> _rules;

    public FakeRoutingRuleQueries(IReadOnlyList<RoutingRuleDto>? rules = null) => _rules = rules ?? [];

    public Task<IReadOnlyList<RoutingRuleDto>> List(Guid? companyId = null, CancellationToken ct = default) =>
        Task.FromResult(_rules);

    public Task<IReadOnlyList<RoutingRuleDto>> ListByEntityType(string entityType, Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RoutingRuleDto>>(_rules.Where(r => r.EntityType == entityType).ToList());

    public Task<RoutingRuleDto?> GetByCode(string code, string entityType, CancellationToken ct = default) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Code == code && r.EntityType == entityType));

    public Task<RoutingRuleDto?> GetById(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_rules.FirstOrDefault(r => r.Id == id) ?? (_rules.Count == 0 ? null : _rules[0]));
}
